using UnityEngine;

namespace ArtDock.Networking
{
    public sealed class ItemPresentation
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

        public void Tick()
        {
            if (!visual) return;

            var current = item.State.Value.Holder;
            var blend = 1 - Mathf.Exp(-context.Settings.Items.VisualSharpness * Time.deltaTime);

            if (current != NetworkItem.NoHolder && context.Players.TryGetValue(current, out var player))
            {
                var eye = player.transform.position + Vector3.up * context.Settings.Player.EyeHeight;
                var direction = player.IsOwner ? player.ViewCamera.transform.forward : player.ReplicatedAim.Value;
                if (holder != current) offset = visual.position - eye;

                var target = direction * physics.HoldDistance(eye, direction, player);
                offset = Vector3.Lerp(offset, target, blend);

                visual.position = physics.ConstrainPresentation(eye + offset, player);
                visual.rotation = Quaternion.Slerp(visual.rotation, item.transform.rotation, blend);
            }
            else
            {
                visual.position = physics.ConstrainPresentation(Vector3.Lerp(visual.position, item.transform.position, blend));
                visual.localRotation = Quaternion.Slerp(visual.localRotation, Quaternion.identity, blend);
            }

            holder = current;
        }
    }
}
