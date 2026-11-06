using System;
using Unity.Netcode;

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
    }
}
