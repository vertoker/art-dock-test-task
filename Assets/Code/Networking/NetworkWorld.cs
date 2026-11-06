using System.Collections.Generic;
using System;
using ArtDock.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace ArtDock.Networking
{
    public enum SessionPhase : byte
    {
        Lobby = 0,
        Loading = 1,
        Playing = 2,
    }

    public class NetworkWorld : NetworkBehaviour
    {
        public readonly NetworkVariable<SessionPhase> Phase = new();
        public readonly NetworkVariable<byte> Capacity = new();
        public NetworkList<SessionPlayer> Players;
        private readonly NetworkVariable<uint> playersRevision = new();

        private NetworkContext context;
        private NetworkManager manager;
        private int initialCapacity;
        private readonly HashSet<ulong> received = new();
        private readonly Dictionary<ulong, double> lastReplies = new();
        private uint sequence;
        private double sentAt;
        private double nextProbe;

        public event Action Changed;

        public int LoadedCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < Players.Count; i++)
                    if (Players[i].IsLoaded) count++;
                return count;
            }
        }

        private void Awake() => Players = new NetworkList<SessionPlayer>();

        public bool IsReady(ulong client)
        {
            if (Phase.Value != SessionPhase.Playing) return false;

            for (var i = 0; i < Players.Count; i++)
                if (Players[i].ClientId == client) return Players[i].IsLoaded;

            return false;
        }
        internal void Initialize(int capacity) => initialCapacity = capacity;

        public override void OnNetworkSpawn()
        {
            manager = NetworkManager;
            context = manager.GetComponent<NetworkContext>();
            
            Phase.OnValueChanged += OnPhaseChanged;
            Capacity.OnValueChanged += OnCapacityChanged;
            Players.OnListChanged += OnPlayersChanged;
            playersRevision.OnValueChanged += OnPlayersRevisionChanged;
            
            if (IsServer)
            {
                received.Clear();
                lastReplies.Clear();
                sequence = 0;
                nextProbe = 0;
                Capacity.Value = (byte)initialCapacity;
                foreach (var client in manager.ConnectedClientsIds) OnConnected(client);
                manager.OnClientConnectedCallback += OnConnected;
                manager.OnClientDisconnectCallback += OnDisconnected;
            }
            context.RegisterWorld(this);
        }

        public override void OnNetworkDespawn()
        {
            if (manager)
            {
                manager.OnClientConnectedCallback -= OnConnected;
                manager.OnClientDisconnectCallback -= OnDisconnected;
            }
            
            Phase.OnValueChanged -= OnPhaseChanged;
            Capacity.OnValueChanged -= OnCapacityChanged;
            Players.OnListChanged -= OnPlayersChanged;
            playersRevision.OnValueChanged -= OnPlayersRevisionChanged;
            
            if (context) context.UnregisterWorld(this);
        }

        private void OnConnected(ulong client)
        {
            for (var i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId == client)
                    return;
            }
            Players.Add(new SessionPlayer(client, context.NameFor(client), Phase.Value == SessionPhase.Playing));
        }

        private void OnDisconnected(ulong client)
        {
            RemoveLatency(client);

            for (var i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId != client) continue;
                
                Players.RemoveAt(i);
                return;
            }
        }

        internal void BeginLoading()
        {
            if (!IsServer) return;
            
            for (var i = 0; i < Players.Count; i++)
            {
                var player = Players[i];
                player.IsLoaded = false;
                Players[i] = player;
            }
            Phase.Value = SessionPhase.Loading;
        }

        internal void SetPhase(SessionPhase phase)
        {
            if (IsServer)
                Phase.Value = phase;
        }

        internal void MarkLoaded(ulong client)
        {
            if (!IsServer) return;
            
            for (var i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId != client) continue;
                
                var player = Players[i];
                player.IsLoaded = true;
                Players[i] = player;
                return;
            }
        }

        private void Update()
        {
            if (IsSpawned && IsServer) UpdateRoundTrips();
        }

        internal void SendProbe(uint sequence) => ProbeRpc(sequence);

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ProbeRpc(uint sequence) => ReplyRpc(sequence);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReplyRpc(uint sequence, RpcParams rpc = default)
            => RecordRoundTrip(rpc.Receive.SenderClientId, sequence);

        internal void SetRoundTrip(ulong client, int milliseconds)
        {
            for (var i = 0; i < Players.Count; i++)
            {
                var player = Players[i];
                if (player.ClientId != client || player.RoundTripMs == milliseconds) continue;

                player.RoundTripMs = milliseconds;
                Players[i] = player;
                return;
            }
        }

        private void OnPhaseChanged(SessionPhase previous, SessionPhase current) => Changed?.Invoke();
        private void OnCapacityChanged(byte previous, byte current) => Changed?.Invoke();
        private void OnPlayersChanged(NetworkListEvent<SessionPlayer> change)
        {
            if (IsServer)
            {
                Players.SetDirty(true);
                playersRevision.Value = unchecked(playersRevision.Value + 1);
            }
            else Changed?.Invoke();
        }

        private void OnPlayersRevisionChanged(uint previous, uint current) => Changed?.Invoke();

        private void UpdateRoundTrips()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (now < nextProbe) return;

            nextProbe = now + context.Settings.Diagnostics.PingInterval;
            sentAt = now;
            sequence = unchecked(sequence + 1);
            received.Clear();

            for (var i = 0; i < Players.Count; i++)
            {
                var client = Players[i].ClientId;
                if (client == NetworkManager.ServerClientId) SetRoundTrip(client, 0);
                else if (!lastReplies.TryGetValue(client, out var last) || now - last > context.Settings.Diagnostics.PingTimeout)
                    SetRoundTrip(client, -1);
            }

            SendProbe(sequence);
        }

        private void RecordRoundTrip(ulong client, uint reply)
        {
            if (reply != sequence || client == NetworkManager.ServerClientId || !received.Add(client)) return;

            var elapsed = Time.realtimeSinceStartupAsDouble - sentAt;
            if (elapsed < 0 || elapsed > context.Settings.Diagnostics.PingTimeout) return;

            lastReplies[client] = Time.realtimeSinceStartupAsDouble;
            SetRoundTrip(client, Mathf.RoundToInt((float)(elapsed * 1000)));
        }

        private void RemoveLatency(ulong client)
        {
            lastReplies.Remove(client);
            received.Remove(client);
        }

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
                UnityEngine.Object.FindFirstObjectByType<ArenaLayout>()?.SpawnInitialItems(context);
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

    public struct SessionPlayer : INetworkSerializable, IEquatable<SessionPlayer>
    {
        public ulong ClientId;
        public bool IsLoaded;
        public int RoundTripMs;
        private FixedString128Bytes playerName;

        public string Name => playerName.ToString();

        public SessionPlayer(ulong clientId, string name, bool isLoaded)
        {
            ClientId = clientId;
            playerName = new FixedString128Bytes(name);
            IsLoaded = isLoaded;
            RoundTripMs = -1;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref playerName);
            serializer.SerializeValue(ref IsLoaded);
            serializer.SerializeValue(ref RoundTripMs);
        }

        public bool Equals(SessionPlayer other)
            => ClientId == other.ClientId && playerName.Equals(other.playerName) && IsLoaded == other.IsLoaded && RoundTripMs == other.RoundTripMs;
    }

}
