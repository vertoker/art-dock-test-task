using ArtDock.Core;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArtDock.Networking
{
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

        private void BindSceneManager()
        {
            stopping = false;
            UnbindSceneManager();
            
            sceneManager = manager.SceneManager;
            if (sceneManager != null)
                sceneManager.OnSceneEvent += OnSceneEvent;
        }

        private void UnbindSceneManager()
        {
            if (sceneManager != null)
                sceneManager.OnSceneEvent -= OnSceneEvent;
            sceneManager = null;
        }

        private void BindWorld()
        {
            if (world is not null) world.Changed -= OnWorldChanged;
            world = context.World;
            if (world) world.Changed += OnWorldChanged;
            Changed?.Invoke();
        }

        private void OnWorldChanged() => Changed?.Invoke();

        public void SpawnWorld(int capacity)
        {
            if (!manager.IsServer || context.World) return;
            if (!context.WorldPrefab) throw new InvalidOperationException(GameErrors.WorldPrefabNotAssigned);
            
            var instance = UnityEngine.Object.Instantiate(context.WorldPrefab);
            instance.Initialize(capacity);
            instance.NetworkObject.Spawn();
        }

        public void StartGame()
        {
            if (!CanStart || sceneManager == null) return;
            
            world.BeginLoading();
            var result = sceneManager.LoadScene(GameConstants.ArenaScene, LoadSceneMode.Single);
            if (result == SceneEventProgressStatus.Started) return;
            
            world.SetPhase(SessionPhase.Lobby);
            Failed?.Invoke(string.Format(GameErrors.SceneLoadFailed, result));
        }

        private void OnSceneEvent(SceneEvent sceneEvent)
        {
            if (stopping) return;
            HandleSynchronization(sceneEvent);
            if (sceneEvent.SceneName == GameConstants.ArenaScene) HandleArenaEvent(sceneEvent);
        }

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

        private void TrackSceneLoading(AsyncOperation operation)
        {
            loadingOperation = operation;
            if (!manager.IsServer) Synchronizing = true;
            Changed?.Invoke();
        }

        private void CompleteLoading(SceneEvent sceneEvent)
        {
            Synchronizing = false;
            if (manager.IsServer && world && world.Phase.Value == SessionPhase.Loading)
            {
                var completed = new HashSet<ulong>(sceneEvent.ClientsThatCompleted);
                if (!ValidateHostLoaded(completed)) return;
                DisconnectTimedOutClients(sceneEvent, completed);
                EnterPlaying(completed);
            }
            Changed?.Invoke();
        }

        private bool ValidateHostLoaded(HashSet<ulong> completed)
        {
            if (completed.Contains(NetworkManager.ServerClientId)) return true;
            Failed?.Invoke(GameErrors.HostSceneLoadFailed);
            return false;
        }

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

        private void EnterPlaying(IEnumerable<ulong> completed)
        {
            foreach (var client in completed)
                world.MarkLoaded(client);
            world.SetPhase(SessionPhase.Playing);
        }

        public void BeginShutdown()
        {
            stopping = true;
            Synchronizing = false;
            UnbindSceneManager();
        }

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

        public void Reset()
        {
            loadingOperation = null;
            menuOperation = null;
            Synchronizing = false;
        }

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
