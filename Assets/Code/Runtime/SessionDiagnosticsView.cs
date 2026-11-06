using ArtDock.Core;
using ArtDock.Networking;
using Unity.Netcode;
using UnityEngine.UIElements;

namespace ArtDock.Runtime
{
    /// <summary> Shows the client role, application RTT and selected item network state </summary>
    internal class SessionDiagnosticsView
    {
        private readonly Label label;
        private readonly SessionService session;
        private readonly NetworkManager manager;
        private readonly IGameText text;

        public SessionDiagnosticsView(VisualElement root, SessionService session, NetworkManager manager, IGameText text)
        {
            label = root.Q<Label>("diagnostics-text");
            this.session = session;
            this.manager = manager;
            this.text = text;
        }

        public void Refresh(NetworkPlayer local)
        {
            var item = local ? local.HeldItem : null;
            if (!item && local) item = local.SelectedItem;
            var unavailable = text[TextKeys.Unavailable];
            var holder = !item ? unavailable : item.State.Value.Holder == NetworkItem.NoHolder
                ? text[TextKeys.Unheld] : item.State.Value.Holder.ToString();

            // Transport counters are unavailable here, RTT comes from an application probe
            label.text = text.Format(TextKeys.Diagnostics, session.Mode, manager.IsServer, manager.IsClient,
                manager.NetworkConfig.TickRate, RoundTrip(), manager.IsListening ? manager.SpawnManager.SpawnedObjects.Count : 0,
                unavailable, item ? item.NetworkObjectId.ToString() : text[TextKeys.NoItem],
                item ? item.OwnerClientId.ToString() : unavailable, holder,
                session.World ? session.World.Phase.Value.ToString() : session.State.ToString(),
                item ? item.State.Value.Revision.ToString() : unavailable);
        }

        private string RoundTrip()
        {
            if (session.World)
                foreach (var player in session.World.Players)
                    if (player.ClientId == manager.LocalClientId && player.RoundTripMs >= 0)
                        return text.Format(TextKeys.Ping, player.RoundTripMs);

            return text[TextKeys.Unavailable];
        }
    }
}
