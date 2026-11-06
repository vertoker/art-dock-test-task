using System.Collections.Generic;
using System;
using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Network world phase: lobby, arena loading or gameplay </summary>
    public enum SessionPhase : byte
    {
        /// <summary> Players connect and wait for the host to start </summary>
        Lobby = 0,
        /// <summary> Clients load and synchronize the arena </summary>
        Loading = 1,
        /// <summary> The arena is ready and gameplay actions are allowed </summary>
        Playing = 2,
    }

    /// <summary> Synchronizes the game phase, player roster and loading readiness </summary>
    public class NetworkWorld : NetworkBehaviour
    {
        public readonly NetworkVariable<SessionPhase> Phase = new();
        public readonly NetworkVariable<byte> Capacity = new();
        public NetworkList<SessionPlayer> Players;
        
        private readonly NetworkVariable<uint> playersRevision = new();
        private readonly HashSet<ulong> received = new();
        private readonly Dictionary<ulong, double> lastReplies = new();

        private NetworkContext context;
        private NetworkManager manager;
        private int initialCapacity;
        private uint sequence;
        private double sentAt;
        private double nextProbe;

        public event Action Changed;

        /// <summary> Number of players who have finished loading the arena </summary>
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

        /// <summary> Checks that gameplay has started and the given client finished loading </summary>
        public bool IsReady(ulong client)
        {
            if (Phase.Value != SessionPhase.Playing) return false;

            for (var i = 0; i < Players.Count; i++)
                if (Players[i].ClientId == client) return Players[i].IsLoaded;

            return false;
        }
        internal void Initialize(int capacity) => initialCapacity = capacity;

        /// <summary> Binds replicated state and initializes the server roster and RTT probes </summary>
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

        /// <summary> Removes network callbacks and clears the current world reference </summary>
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

        /// <summary> Adds a newly connected client to the replicated player roster </summary>
        private void OnConnected(ulong client)
        {
            for (var i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId == client)
                    return;
            }
            Players.Add(new SessionPlayer(client, context.NameFor(client), Phase.Value == SessionPhase.Playing));
        }

        /// <summary> Removes a disconnected player and their pending RTT measurements </summary>
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

        /// <summary> Clears player load flags before switching the server world to loading </summary>
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

        /// <summary> Marks the given player as loaded in the server roster </summary>
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

        /// <summary> Requests a client reply to measure application RTT </summary>
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ProbeRpc(uint sequence) => ReplyRpc(sequence);

        /// <summary> Receives a probe reply and identifies its sender from the RPC </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReplyRpc(uint sequence, RpcParams rpc = default)
            => RecordRoundTrip(rpc.Receive.SenderClientId, sequence);

        /// <summary> Updates the player RTT only when its displayed value changes </summary>
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
        /// <summary> Replicates a complete roster revision on the server and notifies clients of changes </summary>
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

        /// <summary> Sends periodic application probes and expires stale RTT measurements </summary>
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
                
                if (client == NetworkManager.ServerClientId)
                    SetRoundTrip(client, 0);
                // Show an unavailable measurement when the latest reply is too old
                else if (!lastReplies.TryGetValue(client, out var last) || now - last > context.Settings.Diagnostics.PingTimeout)
                    SetRoundTrip(client, -1);
            }

            SendProbe(sequence);
        }

        /// <summary> Accepts one timely reply per client for the current probe </summary>
        private void RecordRoundTrip(ulong client, uint reply)
        {
            // Ignore replies to earlier probes and repeated replies from one client
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

    }
}
