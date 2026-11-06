using System;
using System.Collections.Generic;
using ArtDock.Core;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> A world button that the player can select and activate on the server </summary>
    public interface IWorldButton
    {
        int Id { get; }
        string Prompt { get; }
        void Use(NetworkPlayer player);
    }

    /// <summary> A region that returns players to their spawn positions </summary>
    public interface IWorldResetZone
    {
        bool Contains(Vector3 position);
    }

    /// <summary> Tracks the current world, players and items, connecting network objects to session services </summary>
    public class NetworkContext : MonoBehaviour
    {
        [SerializeField] private NetworkWorld worldPrefab;
        [SerializeField] private NetworkItem itemPrefab;

        private readonly Dictionary<ulong, string> names = new();
        private readonly Dictionary<ulong, int> spawnSlots = new();
        private readonly Dictionary<ulong, NetworkPlayer> players = new();
        private readonly Dictionary<ulong, NetworkItem> items = new();
        private readonly Dictionary<ulong, NetworkItem> heldItems = new();
        private readonly Dictionary<int, IWorldButton> buttons = new();
        private readonly List<IWorldResetZone> resetZones = new();

        internal ItemClaims Claims { get; } = new();
        public IReadOnlyDictionary<ulong, NetworkPlayer> Players => players;
        public IReadOnlyDictionary<ulong, NetworkItem> Items => items;
        public NetworkItem ItemPrefab => itemPrefab;

        public NetworkWorld WorldPrefab => worldPrefab;
        public GameSettings Settings { get; private set; }
        public SessionService Session { get; private set; }
        public NetworkWorld World { get; private set; }
        public IPlayerStatistics Statistics { get; set; }
        public IGameText Text { get; set; }
        public bool InputBlocked { get; set; }

        public event Action WorldChanged;

        /// <summary> Registers a spawned player by owner ID </summary>
        internal void RegisterPlayer(NetworkPlayer player) => players.Add(player.OwnerClientId, player);

        /// <summary> Registers a world button by its ID </summary>
        public void RegisterButton(IWorldButton button) => buttons.Add(button.Id, button);

        /// <summary> Removes the button only if the same instance is still registered </summary>
        public void UnregisterButton(IWorldButton button)
        {
            if (buttons.TryGetValue(button.Id, out var current) && current == button)
                buttons.Remove(button.Id);
        }

        /// <summary> Finds a button by ID and forwards the player's server action </summary>
        internal void UseButton(int id, NetworkPlayer player)
        {
            if (buttons.TryGetValue(id, out var button))
                button.Use(player);
        }

        /// <summary> Registers a player reset region </summary>
        public void RegisterResetZone(IWorldResetZone zone) => resetZones.Add(zone);

        /// <summary> Removes a player reset region </summary>
        public void UnregisterResetZone(IWorldResetZone zone) => resetZones.Remove(zone);

        /// <summary> Checks whether a position lies in any reset region </summary>
        internal bool InResetZone(Vector3 position)
        {
            foreach (var zone in resetZones)
                if (zone.Contains(position))
                    return true;

            return false;
        }

        /// <summary> Unregisters the player without removing an instance that replaced it </summary>
        internal void UnregisterPlayer(NetworkPlayer player)
        {
            if (players.TryGetValue(player.OwnerClientId, out var current) && current == player)
                players.Remove(player.OwnerClientId);
        }

        /// <summary> Registers an item and its server claim state </summary>
        internal void RegisterItem(NetworkItem item)
        {
            items.Add(item.NetworkObjectId, item);
            if (item.IsServer) Claims.Register(item.NetworkObjectId);

            UpdateItemHolder(item, NetworkItem.NoHolder, item.State.Value.Holder);
        }

        /// <summary> Removes an item from the world, holder lookup and server claims </summary>
        internal void UnregisterItem(NetworkItem item)
        {
            UpdateItemHolder(item, item.State.Value.Holder, NetworkItem.NoHolder);
            items.Remove(item.NetworkObjectId);

            if (item.IsServer)
                Claims.Remove(item.NetworkObjectId);
        }

        /// <summary> Updates the holder lookup when an item's network state changes </summary>
        internal void UpdateItemHolder(NetworkItem item, ulong previous, ulong current)
        {
            if (heldItems.TryGetValue(previous, out var held) && held == item)
                heldItems.Remove(previous);

            if (current != NetworkItem.NoHolder)
                heldItems[current] = item;
        }

        /// <summary> Returns the item held by the given player </summary>
        public NetworkItem HeldItemFor(ulong client) => heldItems.GetValueOrDefault(client);

        internal void Initialize(GameSettings settings, SessionService session)
        {
            Settings = settings;
            Session = session;
        }

        /// <summary> Sets the current network world and notifies subscribers </summary>
        public void RegisterWorld(NetworkWorld world)
        {
            World = world;
            WorldChanged?.Invoke();
        }

        /// <summary> Clears the despawned world reference and notifies subscribers </summary>
        public void UnregisterWorld(NetworkWorld world)
        {
            if (World != world) return;

            World = null;
            WorldChanged?.Invoke();
        }

        /// <summary> Stores a player name and assigns an available spawn slot </summary>
        public void SetName(ulong client, string playerName)
        {
            names[client] = playerName;
            if (spawnSlots.ContainsKey(client)) return;

            var slot = 0;
            while (spawnSlots.ContainsValue(slot))
                slot++;

            spawnSlots.Add(client, slot);
        }

        /// <summary> Returns the spawn position assigned to the client </summary>
        public Vector3 SpawnPositionFor(ulong client) =>
            PlayerValidation.SpawnPosition((ulong)spawnSlots[client], Settings);

        /// <summary> Returns the stored name or a label with the client ID </summary>
        public string NameFor(ulong client) => names.TryGetValue(client, out var name) ? name : $"Player {client}";

        /// <summary> Releases the disconnected player's item and removes their session data </summary>
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