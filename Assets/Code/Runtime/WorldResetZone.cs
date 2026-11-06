using ArtDock.Core;
using ArtDock.Networking;
using UnityEngine;

namespace ArtDock.Runtime
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WorldResetZone : MonoBehaviour, IWorldResetZone
    {
        private NetworkContext context;
        private BoxCollider volume;

        private void OnEnable()
        {
            volume = GetComponent<BoxCollider>();
            context = FindFirstObjectByType<NetworkContext>();
            if (context) context.RegisterResetZone(this);
        }

        private void OnDisable()
        {
            if (context) context.UnregisterResetZone(this);
        }

        public bool Contains(Vector3 position)
        {
            var point = transform.InverseTransformPoint(position) - volume.center;
            var half = volume.size * 0.5f;
            return Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y && Mathf.Abs(point.z) <= half.z;
        }

        private void OnTriggerEnter(Collider other) => ResetItem(other);
        private void OnTriggerStay(Collider other) => ResetItem(other);

        private void ResetItem(Collider other)
        {
            var item = other.GetComponentInParent<NetworkItem>();
            if (item && item.IsSpawned && item.IsServer && Contains(item.transform.position)) item.NetworkObject.Despawn();
        }
    }
}
