using System;
using System.Collections.Generic;
using ArtDock.Core;
using UnityEngine;

namespace ArtDock.Networking
{
    public class NetworkContext : MonoBehaviour
    {
        [SerializeField] private NetworkWorld worldPrefab;
        [SerializeField] private NetworkItem itemPrefab;
        private readonly Dictionary<ulong, string> names = new();
        private readonly Dictionary<ulong, int> spawnSlots = new();
        private readonly Dictionary<ulong, NetworkPlayer> players = new();
        private readonly Dictionary<ulong, NetworkItem> items = new();
        private readonly Dictionary<ulong, NetworkItem> heldItems = new();
        internal ItemClaims Claims { get; } = new();
        public IReadOnlyDictionary<ulong, NetworkPlayer> Players => players;
        public IReadOnlyDictionary<ulong, NetworkItem> Items => items;
        public NetworkItem ItemPrefab => itemPrefab;

        public NetworkWorld WorldPrefab => worldPrefab;
        public GameSettings Settings { get; private set; }
        public SessionService Session { get; private set; }
        public NetworkWorld World { get; private set; }
        public event Action WorldChanged;

        internal void RegisterPlayer(NetworkPlayer player) => players.Add(player.OwnerClientId, player);

        internal void UnregisterPlayer(NetworkPlayer player)
        {
            if (players.TryGetValue(player.OwnerClientId, out var current) && current == player)
                players.Remove(player.OwnerClientId);
        }

        internal void RegisterItem(NetworkItem item)
        {
            items.Add(item.NetworkObjectId, item);
            if (item.IsServer) Claims.Register(item.NetworkObjectId);

            UpdateItemHolder(item, NetworkItem.NoHolder, item.State.Value.Holder);
        }

        internal void UnregisterItem(NetworkItem item)
        {
            UpdateItemHolder(item, item.State.Value.Holder, NetworkItem.NoHolder);
            items.Remove(item.NetworkObjectId);
            if (item.IsServer) Claims.Remove(item.NetworkObjectId);
        }

        internal void UpdateItemHolder(NetworkItem item, ulong previous, ulong current)
        {
            if (heldItems.TryGetValue(previous, out var held) && held == item) heldItems.Remove(previous);

            if (current != NetworkItem.NoHolder) heldItems[current] = item;
        }

        public NetworkItem HeldItemFor(ulong client) => heldItems.TryGetValue(client, out var item) ? item : null;

        internal void Initialize(GameSettings settings, SessionService session)
        {
            Settings = settings;
            Session = session;
        }

        public void RegisterWorld(NetworkWorld world)
        {
            World = world;
            WorldChanged?.Invoke();
        }

        public void UnregisterWorld(NetworkWorld world)
        {
            if (World != world) return;

            World = null;
            WorldChanged?.Invoke();
        }

        public void SetName(ulong client, string playerName)
        {
            names[client] = playerName;
            if (spawnSlots.ContainsKey(client)) return;
            
            var slot = 0;
            while (spawnSlots.ContainsValue(slot))
                slot++;

            spawnSlots.Add(client, slot);
        }

        public Vector3 SpawnPositionFor(ulong client) => PlayerValidation.SpawnPosition((ulong)spawnSlots[client], Settings);
        public string NameFor(ulong client) => names.TryGetValue(client, out var name) ? name : $"Player {client}";
        public void Remove(ulong client)
        {
            HeldItemFor(client)?.ReleaseDisconnected(client);

            names.Remove(client);
            spawnSlots.Remove(client);
        }

        public void Clear()
        {
            names.Clear();
            spawnSlots.Clear();
        }
    }
}
