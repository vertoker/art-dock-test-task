using System;
using System.Collections.Generic;
using ArtDock.Core;
using ArtDock.Networking;
using Unity.Netcode;
using UnityEngine.UIElements;

namespace ArtDock.Runtime
{
    public sealed class PlayerListView : IDisposable
    {
        private sealed class Row : IDisposable
        {
            public readonly VisualElement Root = new();
            public readonly Label Name = new() { enableRichText = false };
            public readonly Label State = new();
            public readonly Label Latency = new();
            public readonly Button Kick = new();
            private readonly ulong client;
            private readonly Action<ulong> kick;

            public Row(ulong client, Action<ulong> kick, IGameText text)
            {
                this.client = client;
                this.kick = kick;
                Root.AddToClassList("row");
                Name.AddToClassList("player-name");
                Latency.AddToClassList("ping");
                Latency.tooltip = text[TextKeys.ApplicationRoundTrip];
                Kick.text = text[TextKeys.Kick];
                Kick.focusable = false;
                Root.Add(Name);
                Root.Add(State);
                Root.Add(Latency);
                Root.Add(Kick);
                Kick.clicked += OnKick;
            }

            private void OnKick() => kick(client);
            public void Dispose() => Kick.clicked -= OnKick;
        }

        private readonly VisualElement root;
        private readonly SessionService session;
        private readonly NetworkManager manager;
        private readonly IGameText text;
        private readonly Dictionary<ulong, Row> rows = new();
        private readonly HashSet<ulong> active = new();
        private readonly List<ulong> removed = new();

        public PlayerListView(VisualElement root, SessionService session, NetworkManager manager, IGameText text)
        {
            this.root = root;
            this.session = session;
            this.manager = manager;
            this.text = text;
        }

        public void Refresh(NetworkWorld world)
        {
            active.Clear();
            if (world)
            {
                foreach (var player in world.Players)
                {
                    active.Add(player.ClientId);
                    RefreshRow(player, world);
                }
            }

            removed.Clear();
            foreach (var pair in rows)
                if (!active.Contains(pair.Key)) removed.Add(pair.Key);

            foreach (var client in removed)
            {
                rows[client].Root.RemoveFromHierarchy();
                rows[client].Dispose();
                rows.Remove(client);
            }
        }

        private void RefreshRow(SessionPlayer player, NetworkWorld world)
        {
            if (!rows.TryGetValue(player.ClientId, out var row))
            {
                row = new Row(player.ClientId, session.Kick, text);
                rows.Add(player.ClientId, row);
                root.Add(row.Root);
            }

            row.Name.text = player.Name;
            row.State.text = world.Phase.Value == SessionPhase.Loading
                ? text[player.IsLoaded ? TextKeys.Loaded : TextKeys.Loading]
                : player.ClientId == NetworkManager.ServerClientId ? text[TextKeys.Host] : string.Empty;
            row.Latency.text = player.RoundTripMs >= 0 ? text.Format(TextKeys.Ping, player.RoundTripMs) : text[TextKeys.Unavailable];

            var canKick = session.State == SessionState.Connected && session.IsHost && player.ClientId != manager.LocalClientId;
            row.Kick.style.display = canKick ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Dispose()
        {
            foreach (var row in rows.Values)
            {
                row.Dispose();
                row.Root.RemoveFromHierarchy();
            }

            rows.Clear();
        }
    }
}
