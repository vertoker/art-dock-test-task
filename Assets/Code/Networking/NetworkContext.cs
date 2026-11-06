using System.Collections.Generic;
using UnityEngine;

namespace ArtDock.Networking
{
    public class NetworkContext : MonoBehaviour
    {
        private readonly Dictionary<ulong, string> names = new();

        public void SetName(ulong client, string playerName) => names[client] = playerName;
        public string NameFor(ulong client) => names.TryGetValue(client, out var name) ? name : $"Player {client}";
        public void Remove(ulong client) => names.Remove(client);
        public void Clear() => names.Clear();
    }
}
