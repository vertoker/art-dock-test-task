using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Creates items on the server after checking available space and world limits </summary>
    public static class ItemSpawner
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        public static bool TrySpawnForPlayer(NetworkPlayer player)
        {
            if (!player.CanAct) return false;

            var context = player.Context;
            var eye = player.TrustedPosition + Vector3.up * context.Settings.Player.EyeHeight;
            var direction = player.AimDirection;
            var distance = context.Settings.Items.HoldDistance;

            var hitCount = Physics.SphereCastNonAlloc(eye, context.Settings.Items.HoldRadius, direction, Hits,
                distance, ~0, QueryTriggerInteraction.Ignore);
            if (hitCount == Hits.Length) return false;

            for (var i = 0; i < hitCount; i++)
                if (Hits[i].collider.GetComponentInParent<NetworkPlayer>() != player) return false;

            return TrySpawn(context, eye + direction * distance);
        }

        public static bool TrySpawn(NetworkContext context, Vector3 position)
        {
            var manager = context.GetComponent<NetworkManager>();
            
            if (!manager.IsServer || !context.World || context.World.Phase.Value != SessionPhase.Playing) return false;
            if (!context.ItemPrefab || context.Items.Count >= context.Settings.Items.MaxItems) return false;
            if (PlayerValidation.OutsideBounds(position, context.Settings)) return false;
            if (Physics.CheckBox(position, context.Settings.Items.HalfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                return false;

            var item = Object.Instantiate(context.ItemPrefab, position, Quaternion.identity);
            item.NetworkObject.DontDestroyWithOwner = true;
            item.NetworkObject.Spawn();

            Physics.SyncTransforms();
            return true;
        }
    }
}
