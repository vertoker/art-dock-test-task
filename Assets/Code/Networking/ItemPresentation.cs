using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Smooths item presentation between its network position and the hold position </summary>
    public class ItemPresentation
    {
        private readonly NetworkItem item;
        private readonly Transform visual;
        private readonly ItemPhysics physics;
        private readonly NetworkContext context;
        
        private Vector3 offset;
        private ulong holder = NetworkItem.NoHolder;

        public ItemPresentation(NetworkItem item, Transform visual, ItemPhysics physics, NetworkContext context)
        {
            this.item = item;
            this.visual = visual;
            this.physics = physics;
            this.context = context;
        }

        /// <summary> Updates held or released presentation using the current network holder </summary>
        public void Tick()
        {
            if (!visual) return;

            var current = item.State.Value.Holder;
            var blend = 1 - Mathf.Exp(-context.Settings.Items.VisualSharpness * Time.deltaTime);

            if (current != NetworkItem.NoHolder && context.Players.TryGetValue(current, out var player))
                FollowHolder(player, current, blend);
            else ReturnToBody(blend);

            holder = current;
        }

        /// <summary> Follows the hold position with local or replicated aim while clamping visual displacement </summary>
        private void FollowHolder(NetworkPlayer player, ulong current, float blend)
        {
            var eye = player.transform.position + Vector3.up * context.Settings.Player.EyeHeight;
            var direction = player.IsOwner ? player.ViewCamera.transform.forward : player.ReplicatedAim.Value;
            if (holder != current) offset = visual.position - eye;

            var target = direction * physics.HoldDistance(eye, direction, player);
            offset = Vector3.Lerp(offset, target, blend);

            visual.position = physics.ConstrainPresentation(eye + offset, player);
            visual.rotation = Quaternion.Slerp(visual.rotation, item.transform.rotation, blend);
        }

        /// <summary> Blends a released item's presentation back toward its network transform </summary>
        private void ReturnToBody(float blend)
        {
            visual.position = physics.ConstrainPresentation(Vector3.Lerp(visual.position, item.transform.position, blend));
            visual.localRotation = Quaternion.Slerp(visual.localRotation, Quaternion.identity, blend);
        }
    }
}
