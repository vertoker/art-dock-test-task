using ArtDock.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArtDock.Networking
{
    /// <summary> Selects the item or button under the crosshair and sends local actions to the server </summary>
    public class PlayerInteraction
    {
        private readonly NetworkPlayer player;
        public NetworkItem SelectedItem { get; private set; }
        public IWorldButton SelectedButton { get; private set; }
        public string Hint { get; private set; } = string.Empty;
        public PlayerInteraction(NetworkPlayer player) => this.player = player;

        /// <summary> Updates the selected target and handles item actions </summary>
        public void Tick(Keyboard keyboard, Mouse mouse)
        {
            UpdateSelection();
            HandleShortcuts(keyboard);

            var held = player.HeldItem;
            if (held) HandleHeldItem(held, keyboard, mouse);
            else UseSelection(mouse);
        }

        /// <summary> Handles keyboard shortcuts for item creation and color changes </summary>
        private void HandleShortcuts(Keyboard keyboard)
        {
            if (keyboard.nKey.wasPressedThisFrame) player.RequestSpawn();
            if (keyboard.cKey.wasPressedThisFrame) player.RequestColorChange();
        }

        /// <summary> Sends delete, throw or drop commands with the held item's current revision </summary>
        private void HandleHeldItem(NetworkItem held, Keyboard keyboard, Mouse mouse)
        {
            var revision = held.State.Value.Revision;
            if (keyboard.deleteKey.wasPressedThisFrame) held.DeleteRpc(revision);
            else if (mouse.rightButton.wasPressedThisFrame) held.ReleaseRpc(revision, true, player.ViewCamera.transform.forward);
            else if (mouse.leftButton.wasPressedThisFrame) held.ReleaseRpc(revision, false, Vector3.zero);
        }

        /// <summary> Activates the selected button or grabs the selected item on a left click </summary>
        private void UseSelection(Mouse mouse)
        {
            if (!mouse.leftButton.wasPressedThisFrame) return;

            if (SelectedButton != null) player.RequestButton(SelectedButton.Id);
            else if (SelectedItem) SelectedItem.GrabRpc(SelectedItem.State.Value.Revision);
        }

        /// <summary> Finds the item or world button under the local camera crosshair </summary>
        private void UpdateSelection()
        {
            var camera = player.ViewCamera.transform;
            var lookingAt = Physics.Raycast(camera.position,
                camera.forward, out var hit, player.Context.Settings.Items.InteractionRange, ~0, QueryTriggerInteraction.Ignore);
            
            SelectedItem = lookingAt ? hit.collider.GetComponentInParent<NetworkItem>() : null;
            SelectedButton = lookingAt ? hit.collider.GetComponentInParent<IWorldButton>() : null;
            Hint = GetHint();
        }

        /// <summary> Chooses a hint from the held item and the current crosshair target </summary>
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
