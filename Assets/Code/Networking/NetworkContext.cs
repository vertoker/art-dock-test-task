using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArtDock.Networking
{
    public class NetworkContext : MonoBehaviour
    {
        [SerializeField] private NetworkWorld worldPrefab;
        private readonly Dictionary<ulong, string> names = new();
        private readonly Dictionary<ulong, int> spawnSlots = new();

        public NetworkWorld WorldPrefab => worldPrefab;
        public GameSettings Settings { get; private set; }
        public SessionService Session { get; private set; }
        public NetworkWorld World { get; private set; }
        public event Action WorldChanged;

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
