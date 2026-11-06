using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System;
using ArtDock.Core;
using Unity.Collections;
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
        private readonly NetworkWorld.SessionSceneFlow scenes;
        
        private IConnectionPlatform platform;
        private SessionAttempt attempt;
        private bool disposed;
        private int sessionCapacity;
        private string pendingJoin;
        private ConnectionMode pendingJoinMode;

        public event Action Changed;
        public ConnectionMode Mode { get; set; } = ConnectionMode.Lan;
        public SessionState State { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public string Title { get; private set; } = GameConstants.GameName;
        public bool IsPrivate { get; private set; }
        public string LobbyId => platform?.LobbyId ?? string.Empty;
        public bool HasPendingJoin => pendingJoin != null;
        public bool OverlayOpen => platforms.OverlayOpen;
        public bool Connected => State == SessionState.Connected;
        public bool CursorFree
        {
            get => context.InputBlocked;
            set => context.InputBlocked = value;
        }
        public bool CanInvite => State == SessionState.Connected && !Loading && platform is ILobbyPlatform lobby && lobby.CanInvite;
        
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
            ? manager.ConnectedClientsIds.Count : World ? World.Players.Count : State == SessionState.Connected ? 1 : 0;

        public SessionService(NetworkManager manager, NetworkContext context, PlatformService platforms, GameSettings settings)
        {
            this.manager = manager;
            this.platforms = platforms;
            this.settings = settings;
            this.context = context;
            context.Initialize(settings, this);
            Message = context.Text?[TextKeys.ChooseMode] ?? string.Empty;
            
            approval = new SessionApproval(context, settings);
            scenes = new NetworkWorld.SessionSceneFlow(manager, context, settings);
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
            CloseLobby();
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

        public void InviteFriends()
        {
            if (CanInvite && platform is ILobbyPlatform lobby) lobby.InviteFriends();
        }

        public void Kick(ulong client)
        {
            if (State != SessionState.Connected || !IsHost || client == manager.LocalClientId ||
                !manager.ConnectedClients.ContainsKey(client)) return;

            manager.DisconnectClient(client, GameErrors.KickedByHost);
        }

        public bool SteamOverlayAvailable => (platforms.For(ConnectionMode.Steam) as IFriendsPlatform)?.OverlayAvailable ?? false;
        public SessionFriend[] SteamFriends => (platforms.For(ConnectionMode.Steam) as IFriendsPlatform)?.GetFriends() ?? Array.Empty<SessionFriend>();
        public bool InviteFriend(ulong id) => CanInvite && platform is IFriendsPlatform friends && friends.InviteFriend(id);

        private void CloseLobby()
        {
            if (platform is ILobbyPlatform lobby) lobby.CloseLobby();
        }

        private void OnSceneChanged() => Changed?.Invoke();

        private async Task<bool> Start(bool host, string address, int capacity, bool isPrivate)
        {
            if (disposed || State != SessionState.Offline || manager.ShutdownInProgress || manager.IsListening)
                return false;

            var current = BeginAttempt(host, address, capacity);
            IsPrivate = isPrivate;
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
        private void OnJoinRequested(ConnectionMode mode, string address)
        {
            if (disposed || string.IsNullOrWhiteSpace(address)) return;
            if (State == SessionState.Connected && platform?.Mode == mode && LobbyId == address) return;

            pendingJoin = address;
            pendingJoinMode = mode;
            if (State != SessionState.Offline) Stop(GameConstants.Disconnected);
        }

        private void JoinPendingLobby()
        {
            if (State != SessionState.Offline || pendingJoin == null || !scenes.MenuReady) return;

            var address = pendingJoin;
            Mode = pendingJoinMode;
            pendingJoin = null;
            _ = Join(address);
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

            CloseLobby();
            
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

            JoinPendingLobby();
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

        private class SessionApproval
        {
            [Serializable]
            private class Handshake
            {
                public string version;
                public string id;
                public string name;
            }

            private static readonly UTF8Encoding Encoding = new(false, true);
            private readonly NetworkContext context;
            private readonly GameSettings settings;
            private ApprovalRegistry registry;
            private bool accepting;

            public SessionApproval(NetworkContext context, GameSettings settings)
            {
                this.context = context;
                this.settings = settings;
            }

            public void Begin(int capacity)
            {
                registry = new ApprovalRegistry(capacity, settings.ProtocolVersion, settings.MaxIdentityLength);
                context.Clear();
                accepting = true;
            }

            public bool TryCreatePayload(IConnectionPlatform platform, out byte[] payload, out string error)
            {
                payload = null;
                error = "";
                
                var hello = new Handshake { version = settings.ProtocolVersion, id = platform.PlayerId, name = platform.PlayerName };
                if (!ValidName(hello.name) || string.IsNullOrWhiteSpace(hello.id) || hello.id.Length > settings.MaxIdentityLength)
                {
                    error = GameErrors.InvalidPlayerIdentityOrName;
                    return false;
                }
                
                payload = Encoding.GetBytes(JsonUtility.ToJson(hello));
                if (payload.Length > settings.MaxPayloadBytes)
                {
                    error = GameErrors.PayloadTooLarge;
                    return false;
                }
                return true;
            }

            public void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
            {
                response.Pending = false;
                response.Approved = false;
                response.CreatePlayerObject = false;
                response.Reason = GameErrors.InvalidPayload;
                
                if (!accepting || registry == null || request.Payload == null ||
                    request.Payload.Length == 0 || request.Payload.Length > settings.MaxPayloadBytes) return;
                
                if (context.World && context.World.Phase.Value == SessionPhase.Loading)
                {
                    response.Reason = GameConstants.SessionLoading;
                    return;
                }
                
                try
                {
                    var hello = JsonUtility.FromJson<Handshake>(Encoding.GetString(request.Payload));
                    if (hello == null || !ValidName(hello.name)) return;
                    
                    var result = registry.TryReserveSlot(request.ClientNetworkId, hello.id, hello.version);
                    response.Reason = RejectionReason(result);
                    response.Approved = result == ApprovalResult.Approved;
                    response.CreatePlayerObject = response.Approved;
                    if (response.Approved)
                    {
                        context.SetName(request.ClientNetworkId, hello.name);
                        response.Position = context.SpawnPositionFor(request.ClientNetworkId);
                        response.Rotation = Quaternion.identity;
                    }
                }
                catch (ArgumentException)
                {
                    response.Reason = GameErrors.InvalidPayload;
                }
            }

            public void StopAccepting() => accepting = false;

            public void Remove(ulong client)
            {
                registry?.Remove(client);
                context.Remove(client);
            }

            public void Clear()
            {
                accepting = false;
                registry = null;
                context.Clear();
            }

            private bool ValidName(string name)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > settings.MaxNameLength)
                    return false;
                
                foreach (var character in name)
                {
                    if (char.IsControl(character))
                        return false;
                }
                return Encoding.GetByteCount(name) <= FixedString128Bytes.UTF8MaxLengthInBytes;
            }

            private static string RejectionReason(ApprovalResult result)
            {
                return result switch
                {
                    ApprovalResult.Approved => string.Empty,
                    ApprovalResult.VersionMismatch => GameErrors.VersionMismatch,
                    ApprovalResult.InvalidIdentity => GameErrors.InvalidIdentity,
                    ApprovalResult.DuplicateIdentity => GameErrors.DuplicateIdentity,
                    ApprovalResult.SessionFull => GameConstants.SessionFull,
                    _ => GameErrors.ConnectionRejected
                };
            }
        }

        private class SessionAttempt : IDisposable
        {
            private readonly CancellationTokenSource cancellation = new();
            private readonly float timeout;
            private Task<bool> preparation;
            private float deadline;

            public CancellationToken Token => cancellation.Token;
            public bool PreparationTimedOut => cancellation.IsCancellationRequested;
            public bool ConnectionTimedOut => Time.realtimeSinceStartup > deadline;
            public bool PreparationCompleted => preparation == null || preparation.IsCompleted;

            public SessionAttempt(float timeout)
            {
                this.timeout = timeout;
                cancellation.CancelAfter(TimeSpan.FromSeconds(timeout));
            }

            public Task<bool> Prepare(IConnectionPlatform platform, bool host, string title, int capacity, bool isPrivate, string address)
            {
                preparation = host
                    ? platform.PrepareHost(title, capacity, isPrivate, Token)
                    : platform.PrepareJoin(address, Token);
                return preparation;
            }

            public void BeginConnection() => deadline = Time.realtimeSinceStartup + timeout;
            public void Cancel() => cancellation.Cancel();
            public void Dispose() => cancellation.Dispose();
        }
    }

}
