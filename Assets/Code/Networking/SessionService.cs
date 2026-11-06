using System.Threading.Tasks;
using System;
using ArtDock.Core;
using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Connection state from platform preparation through complete session shutdown </summary>
    public enum SessionState : byte
    {
        Offline = 0,
        Preparing = 1,
        Connecting = 2,
        Connected = 3,
        Stopping = 4,
    }
    
    /// <summary> Starts and stops sessions, connecting the platform, approval checks and scene flow </summary>
    public partial class SessionService : IDisposable
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
        public int PlayerCount
        {
            get
            {
                if (manager && manager.IsServer) return manager.ConnectedClientsIds.Count;
                if (World) return World.Players.Count;
                return State == SessionState.Connected ? 1 : 0;
            }
        }

        public SessionService(NetworkManager manager, NetworkContext context, PlatformService platforms, GameSettings settings)
        {
            this.manager = manager;
            this.platforms = platforms;
            this.settings = settings;
            this.context = context;
            context.Initialize(settings, this);
            Message = context.Text?[TextKeys.ChooseMode] ?? string.Empty;
            
            approval = new SessionApproval(context, settings);
            scenes = new SessionSceneFlow(manager, context, settings);
            scenes.Changed += OnSceneChanged;
            scenes.Failed += Stop;
            Capacity = settings.MaxPlayers;

            ConfigureNetwork();
            
            BindEvents();
        }

        private void ConfigureNetwork()
        {
            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.EnableSceneManagement = true;
            manager.NetworkConfig.TickRate = (uint)settings.TickRate;
            manager.NetworkConfig.ClientConnectionBufferTimeout = Mathf.CeilToInt(settings.ConnectionTimeout);
            manager.NetworkConfig.LoadSceneTimeOut = Mathf.CeilToInt(settings.SceneLoadTimeout);
        }

        private void BindEvents()
        {
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
            
            if (ConnectionTimedOut()) Stop(GameErrors.ConnectionTimedOut);

            if (State == SessionState.Stopping) TryFinishStop();
            else if (State == SessionState.Connected)
            {
                platform?.UpdatePresence(World && World.Phase.Value == SessionPhase.Playing,
                    PlayerCount, Capacity, !Loading && PlayerCount < Capacity);
            }

            JoinPendingLobby();
        }

        private bool ConnectionTimedOut()
        {
            if (State == SessionState.Preparing) return attempt.PreparationTimedOut;
            if (State != SessionState.Connecting) return false;
            if (scenes.Synchronizing) return scenes.SynchronizationTimedOut;
            return attempt.ConnectionTimedOut;
        }

        /// <summary> Waits for transport and preparation cleanup before leaving the platform and resetting state </summary>
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
