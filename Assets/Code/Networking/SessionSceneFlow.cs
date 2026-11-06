using System;
using System.Collections.Generic;
using ArtDock.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArtDock.Networking
{
    /// <summary> Coordinates arena loading, client synchronization and return to the menu </summary>
    internal class SessionSceneFlow : IDisposable
    {
        private readonly NetworkManager manager;
        private readonly NetworkContext context;
        private readonly GameSettings settings;
        
        private NetworkSceneManager sceneManager;
        private NetworkWorld world;
        private AsyncOperation loadingOperation;
        private AsyncOperation menuOperation;
        
        private float synchronizationDeadline;
        private bool stopping;

        public event Action Changed;
        public event Action<string> Failed;
        
        public bool Synchronizing { get; private set; }
        public bool SynchronizationTimedOut => Time.realtimeSinceStartup > synchronizationDeadline;
        public bool Loading => Synchronizing || world && world.Phase.Value == SessionPhase.Loading;
        public bool MenuReady => SceneManager.GetActiveScene().name == GameConstants.MenuScene;
        /// <summary> Allows the host to start after all connected players appear in the world roster </summary>
        public bool CanStart => !stopping && manager.IsServer && world && world.Phase.Value == SessionPhase.Lobby &&
            manager.PendingClients.Count == 0 && world.Players.Count == manager.ConnectedClientsIds.Count;

        public SessionSceneFlow(NetworkManager manager, NetworkContext context, GameSettings settings)
        {
            this.manager = manager;
            this.context = context;
            this.settings = settings;
            
            manager.OnServerStarted += BindSceneManager;
            manager.OnClientStarted += BindSceneManager;
            context.WorldChanged += BindWorld;
        }

        /// <summary> Subscribes to the current NGO scene manager after network startup </summary>
        private void BindSceneManager()
        {
            stopping = false;
            UnbindSceneManager();
            
            sceneManager = manager.SceneManager;
            if (sceneManager != null)
                sceneManager.OnSceneEvent += OnSceneEvent;
        }

        /// <summary> Removes callbacks from the previous NGO scene manager </summary>
        private void UnbindSceneManager()
        {
            if (sceneManager != null)
                sceneManager.OnSceneEvent -= OnSceneEvent;
            sceneManager = null;
        }

        /// <summary> Switches change subscriptions to the current network world </summary>
        private void BindWorld()
        {
            if (world is not null) world.Changed -= OnWorldChanged;
            world = context.World;
            if (world) world.Changed += OnWorldChanged;
            Changed?.Invoke();
        }

        private void OnWorldChanged() => Changed?.Invoke();

        /// <summary> Creates and initializes the shared network world once on the server </summary>
        public void SpawnWorld(int capacity)
        {
            if (!manager.IsServer || context.World) return;
            if (!context.WorldPrefab) throw new InvalidOperationException(GameErrors.WorldPrefabNotAssigned);
            
            var instance = UnityEngine.Object.Instantiate(context.WorldPrefab);
            instance.Initialize(capacity);
            instance.NetworkObject.Spawn();
        }

        /// <summary> Starts the server arena load and returns to the lobby phase if loading cannot begin </summary>
        public void StartGame()
        {
            if (!CanStart || sceneManager == null) return;
            
            world.BeginLoading();
            var result = sceneManager.LoadScene(GameConstants.ArenaScene, LoadSceneMode.Single);
            if (result == SceneEventProgressStatus.Started) return;
            
            world.SetPhase(SessionPhase.Lobby);
            Failed?.Invoke(string.Format(GameErrors.SceneLoadFailed, result));
        }

        /// <summary> Routes scene events to client synchronization and arena loading handlers </summary>
        private void OnSceneEvent(SceneEvent sceneEvent)
        {
            if (stopping) return;
            HandleSynchronization(sceneEvent);
            if (sceneEvent.SceneName == GameConstants.ArenaScene) HandleArenaEvent(sceneEvent);
        }

        /// <summary> Tracks the local client synchronization deadline and completion </summary>
        private void HandleSynchronization(SceneEvent sceneEvent)
        {
            if (manager.IsServer) return;
            if (sceneEvent.SceneEventType == SceneEventType.Synchronize)
            {
                Synchronizing = true;
                synchronizationDeadline = Time.realtimeSinceStartup + settings.SceneLoadTimeout;
                Changed?.Invoke();
            }
            else if (sceneEvent.SceneEventType == SceneEventType.SynchronizeComplete &&
                sceneEvent.ClientId == manager.LocalClientId)
            {
                Synchronizing = false;
                Changed?.Invoke();
            }
        }

        /// <summary> Handles arena load events and marks players who finished loading </summary>
        private void HandleArenaEvent(SceneEvent sceneEvent)
        {
            switch (sceneEvent.SceneEventType)
            {
                case SceneEventType.Load:
                    TrackSceneLoading(sceneEvent.AsyncOperation);
                    break;
                case SceneEventType.LoadComplete when manager.IsServer && world:
                    world.MarkLoaded(sceneEvent.ClientId);
                    break;
                case SceneEventType.LoadEventCompleted:
                    CompleteLoading(sceneEvent);
                    break;
            }
        }

        /// <summary> Keeps the active load operation so shutdown can wait for it </summary>
        private void TrackSceneLoading(AsyncOperation operation)
        {
            loadingOperation = operation;
            if (!manager.IsServer) Synchronizing = true;
            Changed?.Invoke();
        }

        /// <summary> Validates completed loads and lets surviving players enter gameplay </summary>
        private void CompleteLoading(SceneEvent sceneEvent)
        {
            Synchronizing = false;
            // The host enters gameplay after validating its load and disconnecting timed-out clients
            if (manager.IsServer && world && world.Phase.Value == SessionPhase.Loading)
            {
                var completed = new HashSet<ulong>(sceneEvent.ClientsThatCompleted);
                if (!ValidateHostLoaded(completed)) return;
                
                DisconnectTimedOutClients(sceneEvent, completed);
                EnterPlaying(completed);
            }
            Changed?.Invoke();
        }

        /// <summary> Fails the session if the host did not finish loading the arena </summary>
        private bool ValidateHostLoaded(HashSet<ulong> completed)
        {
            if (completed.Contains(NetworkManager.ServerClientId)) return true;
            Failed?.Invoke(GameErrors.HostSceneLoadFailed);
            return false;
        }

        /// <summary> Disconnects clients missing from the completed roster as well as explicit timeouts </summary>
        private void DisconnectTimedOutClients(SceneEvent sceneEvent, HashSet<ulong> completed)
        {
            var timedOut = new HashSet<ulong>(sceneEvent.ClientsThatTimedOut);
            
            for (var i = 0; i < world.Players.Count; i++)
            {
                if (!completed.Contains(world.Players[i].ClientId))
                    timedOut.Add(world.Players[i].ClientId);
            }
            foreach (var client in timedOut)
            {
                if (manager.ConnectedClients.ContainsKey(client))
                    manager.DisconnectClient(client, GameErrors.SceneLoadTimedOut);
            }
        }

        /// <summary> Marks completed players ready, enters gameplay and creates the initial arena items </summary>
        private void EnterPlaying(IEnumerable<ulong> completed)
        {
            foreach (var client in completed)
                world.MarkLoaded(client);
            world.SetPhase(SessionPhase.Playing);
            UnityEngine.Object.FindFirstObjectByType<ArenaLayout>()?.SpawnInitialItems(context);
        }

        /// <summary> Stops processing scene events while the network session shuts down </summary>
        public void BeginShutdown()
        {
            stopping = true;
            Synchronizing = false;
            UnbindSceneManager();
        }

        /// <summary> Waits for the active arena load before starting or completing the local menu load </summary>
        public bool ReturnToMenu()
        {
            if (!Application.isPlaying) return true;
            if (loadingOperation is { isDone: false }) return false;
            if (menuOperation != null) return menuOperation.isDone;
            
            var scene = SceneManager.GetActiveScene().name;
            if (scene is GameConstants.MenuScene or GameConstants.BootScene) return true;
            
            menuOperation = SceneManager.LoadSceneAsync(GameConstants.MenuScene, LoadSceneMode.Single);
            return menuOperation == null || menuOperation.isDone;
        }

        /// <summary> Clears load operations and synchronization state before another session </summary>
        public void Reset()
        {
            loadingOperation = null;
            menuOperation = null;
            Synchronizing = false;
        }

        /// <summary> Removes scene, network startup and world subscriptions </summary>
        public void Dispose()
        {
            UnbindSceneManager();
            manager.OnServerStarted -= BindSceneManager;
            manager.OnClientStarted -= BindSceneManager;
            context.WorldChanged -= BindWorld;
            if (world is not null) world.Changed -= OnWorldChanged;
        }
    }
}
