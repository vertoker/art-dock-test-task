using System.Collections.Generic;
using System;
using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Network world phase: lobby, arena loading or gameplay </summary>
    public enum SessionPhase : byte
    {
        Lobby = 0,
        Loading = 1,
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
                
                if (client == NetworkManager.ServerClientId)
                    SetRoundTrip(client, 0);
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

    }
}
