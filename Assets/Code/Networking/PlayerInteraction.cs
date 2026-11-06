using ArtDock.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArtDock.Networking
{
    public sealed class PlayerInteraction
    {
        private readonly NetworkPlayer player;
        public NetworkItem SelectedItem { get; private set; }
        public IWorldButton SelectedButton { get; private set; }
        public string Hint { get; private set; } = string.Empty;
        public PlayerInteraction(NetworkPlayer player) => this.player = player;

        public void Tick(Keyboard keyboard, Mouse mouse)
        {
            UpdateSelection();

            if (keyboard.nKey.wasPressedThisFrame) player.RequestSpawn();
            if (keyboard.cKey.wasPressedThisFrame) player.RequestColorChange();

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

            if (SelectedButton != null) player.RequestButton(SelectedButton.Id);
            else if (SelectedItem) SelectedItem.GrabRpc(SelectedItem.State.Value.Revision);
        }

        private void UpdateSelection()
        {
            var camera = player.ViewCamera.transform;
            var lookingAt = Physics.Raycast(camera.position, camera.forward, out var hit, player.Context.Settings.Items.InteractionRange,
                ~0, QueryTriggerInteraction.Ignore);
            SelectedItem = lookingAt ? hit.collider.GetComponentInParent<NetworkItem>() : null;
            SelectedButton = lookingAt ? hit.collider.GetComponentInParent<IWorldButton>() : null;
            Hint = GetHint();
        }

        private string GetHint()
        {
            var text = player.Context.Text;
            if (player.HeldItem) return text[TextKeys.DropThrow];
            if (SelectedButton != null) return SelectedButton.Prompt;
            if (!SelectedItem) return string.Empty;

            return text[SelectedItem.State.Value.Holder == NetworkItem.NoHolder ? TextKeys.PickUp : TextKeys.ItemInUse];
        }
    }
}
