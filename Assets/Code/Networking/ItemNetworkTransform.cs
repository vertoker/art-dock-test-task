using Unity.Netcode.Components;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Synchronizes items from the server even when network ownership belongs to the holder </summary>
    public class ItemNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => true;

        protected override void Awake()
        {
            base.Awake();
            OnClientRequestChange = RejectClientState;
        }

        /// <summary> Replaces client state requests with the current server Rigidbody state </summary>
        private (Vector3, Quaternion, Vector3) RejectClientState(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var body = GetComponent<Rigidbody>();
            return (body ? body.position : transform.position, body ? body.rotation : transform.rotation, transform.localScale);
        }
    }
}
