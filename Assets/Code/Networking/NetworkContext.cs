using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArtDock.Networking
{
    public class NetworkContext : MonoBehaviour
    {
        [SerializeField] private NetworkWorld worldPrefab;
        private readonly Dictionary<ulong, string> names = new();

        public NetworkWorld WorldPrefab => worldPrefab;
        public NetworkWorld World { get; private set; }
        public event Action WorldChanged;

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

        public void SetName(ulong client, string playerName) => names[client] = playerName;
        public string NameFor(ulong client) => names.TryGetValue(client, out var name) ? name : $"Player {client}";
        public void Remove(ulong client) => names.Remove(client);
        public void Clear() => names.Clear();
    }
}
