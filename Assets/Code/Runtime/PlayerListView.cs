using ArtDock.Networking;
using UnityEngine.UIElements;

namespace ArtDock.Runtime
{
    public class PlayerListView
    {
        private readonly VisualElement root;

        public PlayerListView(VisualElement root) => this.root = root;

        public void Refresh(NetworkWorld world)
        {
            root.Clear();
            if (!world) return;
            
            foreach (var player in world.Players)
            {
                var loading = world.Phase.Value == SessionPhase.Loading;
                var state = player.IsLoaded ? "Loaded" : "Loading...";
                root.Add(new Label(loading ? $"{player.Name} - {state}" : player.Name));
            }
        }
    }
}
