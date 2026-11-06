using System;
using System.Threading;
using System.Threading.Tasks;
using ArtDock.Core;
using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    public enum SessionState : byte
    {
        Offline = 0,
        Preparing = 1,
        Connecting = 2,
        Connected = 3,
        Stopping = 4,
    }
    
    public class SessionService : IDisposable
    {
        private readonly NetworkManager manager;
        private readonly PlatformService platforms;
        private readonly GameSettings settings;
        private readonly SessionApproval approval;
        private readonly NetworkContext context;
        private readonly SessionSceneFlow scenes;
        
        private IConnectionPlatform platform;
        private SessionAttempt attempt;
        private bool disposed;
        private int sessionCapacity;

        public event Action Changed;
        public ConnectionMode Mode { get; set; } = ConnectionMode.Lan;
        public SessionState State { get; private set; }
        public string Message { get; private set; } = "Ready";
        public string Title { get; private set; } = GameConstants.GameName;
        
        public int Capacity
        {
            get => World ? World.Capacity.Value : sessionCapacity;
            private set => sessionCapacity = value;
        }
        public NetworkWorld World => context.World;
        public bool Loading => scenes.Loading;
        public bool CanStartGame => State == SessionState.Connected && scenes.CanStart;

        public bool IsHost => manager && manager.IsHost;
        public bool Busy => State is SessionState.Preparing or SessionState.Connecting or SessionState.Stopping;
        public int PlayerCount => manager && manager.IsServer
            ? manager.ConnectedClientsIds.Count : State == SessionState.Connected ? 1 : 0;

        public SessionService(NetworkManager manager, NetworkContext context, PlatformService platforms, GameSettings settings)
        {
            this.manager = manager;
            this.platforms = platforms;
            this.settings = settings;
            this.context = context;
            context.Initialize(settings, this);
            
            approval = new SessionApproval(context, settings);
            scenes = new SessionSceneFlow(manager, context, settings);
            scenes.Changed += OnSceneChanged;
            scenes.Failed += Stop;
            Capacity = settings.MaxPlayers;

            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.EnableSceneManagement = true;
            manager.NetworkConfig.TickRate = (uint)settings.TickRate;
            manager.NetworkConfig.ClientConnectionBufferTimeout = Mathf.CeilToInt(settings.ConnectionTimeout);
            manager.NetworkConfig.LoadSceneTimeOut = Mathf.CeilToInt(settings.SceneLoadTimeout);
            
            manager.ConnectionApprovalCallback += approval.Approve;
            manager.OnClientConnectedCallback += OnConnected;
            manager.OnClientDisconnectCallback += OnDisconnected;
            manager.OnTransportFailure += OnTransportFailure;
            manager.OnServerStopped += OnNetworkStopped;
            manager.OnClientStopped += OnNetworkStopped;

            platforms.JoinRequested += OnJoinRequested;
            platforms.ConnectionLost += OnConnectionLost;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            
            scenes.Changed -= OnSceneChanged;
            scenes.Failed -= Stop;
            scenes.Dispose();
            attempt?.Cancel();
            attempt?.Dispose();
            
            if (manager)
            {
                manager.ConnectionApprovalCallback -= approval.Approve;
                manager.OnClientConnectedCallback -= OnConnected;
                manager.OnClientDisconnectCallback -= OnDisconnected;
                manager.OnTransportFailure -= OnTransportFailure;
                manager.OnServerStopped -= OnNetworkStopped;
                manager.OnClientStopped -= OnNetworkStopped;
                if (manager.IsListening) manager.Shutdown(true);
            }
            
            platforms.JoinRequested -= OnJoinRequested;
            platforms.ConnectionLost -= OnConnectionLost;
            platform?.Leave();
            approval.Clear();
        }

        public Task<bool> Host(string title, int capacity, bool isPrivate) => Start(true, title, capacity, isPrivate);
        public Task<bool> Join(string address) => Start(false, address, settings.MaxPlayers, false);
        public void StartGame()
        {
            if (CanStartGame) scenes.StartGame();
        }

        private void OnSceneChanged() => Changed?.Invoke();

        private async Task<bool> Start(bool host, string address, int capacity, bool isPrivate)
        {
            if (disposed || State != SessionState.Offline || manager.ShutdownInProgress || manager.IsListening)
                return false;

            var current = BeginAttempt(host, address, capacity);
            var token = current.Token;
            Changed?.Invoke();

            try
            {
                var ready = await PreparePlatform(current, host, address, isPrivate);
                return ValidatePreparation(current, token, ready) && StartNetwork(current, host);
            }
            catch (Exception exception)
            {
                HandleStartFailure(current, exception);
                return false;
            }
        }

        private SessionAttempt BeginAttempt(bool host, string address, int capacity)
        {
            State = SessionState.Preparing;
            Message = GameConstants.Connecting;
            Capacity = Mathf.Clamp(capacity, 2, settings.MaxPlayers);
            Title = string.IsNullOrWhiteSpace(address) || !host ? GameConstants.GameName : address.Trim();
            
            if (Title.Length > settings.MaxNameLength) Title = Title[..settings.MaxNameLength];
            return attempt = new SessionAttempt(settings.ConnectionTimeout);
        }

        private Task<bool> PreparePlatform(SessionAttempt current, bool host, string address, bool isPrivate)
        {
            platform = platforms.For(Mode);
            return current.Prepare(platform, host, Title, Capacity, isPrivate, address);
        }

        private bool ValidatePreparation(SessionAttempt current, CancellationToken token, bool ready)
        {
            if (!IsCurrent(current)) return false;
            
            token.ThrowIfCancellationRequested();
            if (ready) return true;
            
            Stop(platform.Error);
            return false;
        }

        private bool StartNetwork(SessionAttempt current, bool host)
        {
            if (!approval.TryCreatePayload(platform, out var payload, out var error))
            {
                Stop(error);
                return false;
            }
            
            approval.Begin(Capacity);
            manager.NetworkConfig.ConnectionData = payload;
            State = SessionState.Connecting;
            
            current.BeginConnection();
            var started = host ? manager.StartHost() : manager.StartClient();
            
            if (!started)
            {
                Stop(GameErrors.TransportStartFailed);
                return false;
            }
            if (host) scenes.SpawnWorld(Capacity);
            
            Changed?.Invoke();
            return true;
        }

        private void HandleStartFailure(SessionAttempt current, Exception exception)
        {
            if (!IsCurrent(current)) return;
            
            if (exception is OperationCanceledException)
            {
                Stop(GameErrors.ConnectionTimedOut);
                return;
            }
            
            Stop(exception.Message);
            Debug.LogException(exception);
        }

        private bool IsCurrent(SessionAttempt current)
            => !disposed && attempt == current && State != SessionState.Stopping;

        private void OnConnected(ulong client)
        {
            if (client == manager.LocalClientId && State == SessionState.Connecting)
            {
                attempt?.Cancel();
                State = SessionState.Connected;
                Message = IsHost ? GameConstants.Hosting : GameConstants.Connected;
            }
            Changed?.Invoke();
        }

        private void OnDisconnected(ulong client)
        {
            if (manager.IsServer)
            {
                approval.Remove(client);
            }
            if (State != SessionState.Stopping && State != SessionState.Offline &&
                (client == manager.LocalClientId || !manager.IsServer && client == NetworkManager.ServerClientId))
            {
                var reason = manager.DisconnectReason;
                if (string.IsNullOrWhiteSpace(reason) || reason.StartsWith(GameConstants.TransportDisconnectPrefix, StringComparison.Ordinal))
                    reason = State == SessionState.Connecting ? GameErrors.ConnectionTimedOutOrRejected : GameErrors.HostConnectionLost;
                Stop(reason);
            }
            Changed?.Invoke();
        }

        private void OnNetworkStopped(bool wasHost)
        {
            if (State != SessionState.Offline && State != SessionState.Stopping)
                Stop(GameErrors.SessionEnded);
        }

        private void OnTransportFailure() => Stop(GameErrors.TransportFailed);
        private void OnJoinRequested(string address)
        {
            if (!disposed && State == SessionState.Offline)
                Join(address);
        }

        private void OnConnectionLost(ConnectionMode mode, string reason)
        {
            if (platform != null && platform.Mode == mode && State != SessionState.Offline)
                Stop(reason);
        }

        public void Stop(string reason = "")
        {
            if (disposed || State == SessionState.Stopping || State == SessionState.Offline)
                return;
            
            State = SessionState.Stopping;
            scenes.BeginShutdown();
            approval.StopAccepting();
            attempt?.Cancel();
            Message = reason ?? string.Empty;
            
            if (manager.IsListening || manager.IsClient || manager.IsServer)
                manager.Shutdown();
            Changed?.Invoke();
        }

        public void Tick()
        {
            if (disposed) return;
            platforms.Tick();
            if (State == SessionState.Preparing && attempt.PreparationTimedOut ||
                State == SessionState.Connecting && (scenes.Synchronizing ? scenes.SynchronizationTimedOut : attempt.ConnectionTimedOut))
                Stop(GameErrors.ConnectionTimedOut);

            if (State == SessionState.Stopping) TryFinishStop();
            else if (State == SessionState.Connected)
            {
                platform?.UpdatePresence(World && World.Phase.Value == SessionPhase.Playing,
                    PlayerCount, Capacity, !Loading && PlayerCount < Capacity);
            }
        }

        private void TryFinishStop()
        {
            if (manager.ShutdownInProgress || manager.IsListening ||
                attempt is { PreparationCompleted: false } || !scenes.ReturnToMenu()) return;
            
            platform?.Leave();
            platform = null;
            attempt?.Dispose();
            attempt = null;
            approval.Clear();
            scenes.Reset();
            
            manager.NetworkConfig.ConnectionData = Array.Empty<byte>();
            State = SessionState.Offline;
            Changed?.Invoke();
        }
    }
}
