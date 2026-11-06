using UnityEngine;
using UnityEngine.InputSystem;

namespace ArtDock.Networking
{
    public sealed class PlayerInteraction
    {
        private readonly NetworkPlayer player;
        public PlayerInteraction(NetworkPlayer player) => this.player = player;

        public void Tick(Keyboard keyboard, Mouse mouse)
        {
            if (keyboard.nKey.wasPressedThisFrame) player.RequestSpawn();

            var held = player.HeldItem;
            if (held)
            {
                var revision = held.State.Value.Revision;
                if (keyboard.deleteKey.wasPressedThisFrame) held.DeleteRpc(revision);
                else if (mouse.rightButton.wasPressedThisFrame) held.ReleaseRpc(revision, true, player.ViewCamera.transform.forward);
                else if (mouse.leftButton.wasPressedThisFrame) held.ReleaseRpc(revision, false, Vector3.zero);

                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame) return;

            var camera = player.ViewCamera.transform;
            if (!Physics.Raycast(camera.position, camera.forward, out var hit, player.Context.Settings.Items.InteractionRange,
                    ~0, QueryTriggerInteraction.Ignore)) return;

            var item = hit.collider.GetComponentInParent<NetworkItem>();
            if (item) item.GrabRpc(item.State.Value.Revision);
        }
    }
}
