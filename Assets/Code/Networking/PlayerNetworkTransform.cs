using Unity.Netcode.Components;

namespace ArtDock.Networking
{
    public class PlayerNetworkTransform : NetworkTransform
    {
        public bool HasInvalidState { get; private set; }
        protected override bool OnIsServerAuthoritative() => false;
        
        protected override void OnNetworkTransformStateUpdated(ref NetworkTransformState previous, ref NetworkTransformState current)
        {
            var position = current.GetPosition();
            var rotation = current.GetRotation();
            var finite = float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z) &&
                         float.IsFinite(rotation.x) && float.IsFinite(rotation.y) && float.IsFinite(rotation.z) && float.IsFinite(rotation.w);
            if (!finite) HasInvalidState = true;
            else if (current.IsTeleportingNextFrame) HasInvalidState = false;
        }
    }
}
