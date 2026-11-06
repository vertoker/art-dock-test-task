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
        
        private IConnectionPlatform platform;
        private SessionAttempt attempt;
        private bool disposed;

        public event Action Changed;
        public ConnectionMode Mode { get; set; } = ConnectionMode.Lan;
        public SessionState State { get; private set; }
        public string Message { get; private set; } = "Ready";
        public string Title { get; private set; } = "ArtDock";
        public int Capacity { get; private set; }

        public bool IsHost => manager && manager.IsHost;
        public bool Busy => State is SessionState.Preparing or SessionState.Connecting or SessionState.Stopping;
        public int PlayerCount => manager && manager.IsServer
            ? manager.ConnectedClientsIds.Count : State == SessionState.Connected ? 1 : 0;

        public SessionService(NetworkManager manager, NetworkContext context, PlatformService platforms, GameSettings settings)
        {
            this.manager = manager;
            this.platforms = platforms;
            this.settings = settings;
            approval = new SessionApproval(context, settings);
            Capacity = settings.MaxPlayers;

            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.EnableSceneManagement = true;
            manager.NetworkConfig.TickRate = (uint)settings.TickRate;
            manager.NetworkConfig.ClientConnectionBufferTimeout = Mathf.CeilToInt(settings.ConnectionTimeout);
            
            manager.ConnectionApprovalCallback += approval.Approve;
            manager.OnClientConnectedCallback += OnConnected;
            manager.OnClientDisconnectCallback += OnDisconnected;
            manager.OnTransportFailure += OnTransportFailure;
            manager.OnServerStopped += OnNetworkStopped;
            manager.OnClientStopped += OnNetworkStopped;

            platforms.JoinRequested += OnJoinRequested;
            platforms.ConnectionLost += OnConnectionLost;
        }

        public Task<bool> Host(string title, int capacity, bool isPrivate) => Start(true, title, capacity, isPrivate);
        public Task<bool> Join(string address) => Start(false, address, settings.MaxPlayers, false);

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
            Message = "Connecting...";
            Capacity = Mathf.Clamp(capacity, 2, settings.MaxPlayers);
            Title = string.IsNullOrWhiteSpace(address) || !host ? "TestGame" : address.Trim();
            
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
                Stop("Unable to start the network transport");
                return false;
            }
            
            Changed?.Invoke();
            return true;
        }

        private void HandleStartFailure(SessionAttempt current, Exception exception)
        {
            if (!IsCurrent(current)) return;
            if (exception is OperationCanceledException)
            {
                Stop("Connection timed out");
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
                Message = IsHost ? "Hosting" : "Connected";
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
                if (string.IsNullOrWhiteSpace(reason) || reason.StartsWith("[Disconnect Event]", StringComparison.Ordinal))
                    reason = State == SessionState.Connecting ? "Connection timed out or was rejected" : "Connection to the host was lost";
                Stop(reason);
            }
            Changed?.Invoke();
        }

        private void OnNetworkStopped(bool wasHost)
        {
            if (State != SessionState.Offline && State != SessionState.Stopping)
                Stop("Network session ended");
        }

        private void OnTransportFailure() => Stop("Network transport failed");
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
            approval.StopAccepting();
            attempt?.Cancel();
            Message = reason ?? "";
            
            if (manager.IsListening || manager.IsClient || manager.IsServer)
                manager.Shutdown();
            Changed?.Invoke();
        }

        public void Tick()
        {
            if (disposed) return;
            platforms.Tick();
            if (State == SessionState.Preparing && attempt.PreparationTimedOut ||
                State == SessionState.Connecting && attempt.ConnectionTimedOut)
                Stop("Connection timed out");

            if (State == SessionState.Stopping && !manager.ShutdownInProgress && !manager.IsListening &&
                (attempt == null || attempt.PreparationCompleted))
            {
                platform?.Leave();
                platform = null;
                attempt?.Dispose();
                attempt = null;
                approval.Clear();
                
                manager.NetworkConfig.ConnectionData = Array.Empty<byte>();
                State = SessionState.Offline;
                Changed?.Invoke();
            }
            else if (State == SessionState.Connected)
                platform?.UpdatePresence(false, PlayerCount, Capacity, PlayerCount < Capacity);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
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
    }
}
