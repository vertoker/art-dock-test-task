using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider), typeof(ItemNetworkTransform))]
    public class NetworkItem : NetworkBehaviour
    {
        public const ulong NoHolder = ulong.MaxValue;
        [SerializeField] private Transform presentation;
        public readonly NetworkVariable<ItemState> State = new(new ItemState { Holder = NoHolder });
        private NetworkContext context;
        private ItemPhysics physics;
        private ItemPresentation visual;

        public override void OnNetworkSpawn()
        {
            context = NetworkManager.GetComponent<NetworkContext>();
            physics = new ItemPhysics(GetComponent<Rigidbody>(), GetComponent<BoxCollider>(), context.Settings.Items, IsServer);
            visual = new ItemPresentation(this, presentation, physics, context);
            NetworkObject.DontDestroyWithOwner = true;

            context.RegisterItem(this);
            State.OnValueChanged += OnStateChanged;
        }

        private void OnStateChanged(ItemState previous, ItemState current) => context.UpdateItemHolder(this, previous.Holder, current.Holder);
        private void LateUpdate()
        {
            if (IsSpawned) visual.Tick();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;

            context.Players.TryGetValue(State.Value.Holder, out var holder);
            physics.UpdateCollisions(holder);

            if (!IsServer) return;

            if (PlayerValidation.OutsideBounds(transform.position, context.Settings))
            {
                NetworkObject.Despawn();
                return;
            }

            if (State.Value.Holder == NoHolder) return;

            if (!holder || !holder.IsSpawned || !holder.CanAct || !physics.Follow(holder))
                ReleaseDisconnected(State.Value.Holder);
        }

        private bool TryPlayer(ulong sender, out NetworkPlayer player) => context.Players.TryGetValue(sender, out player) &&
            player && player.TryConsumeItemAction();

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void GrabRpc(uint expectedRevision, RpcParams rpc = default)
        {
            var sender = rpc.Receive.SenderClientId;
            if (!TryPlayer(sender, out var player) || !physics.InReach(player) || !physics.VisibleFrom(player) ||
                !context.Claims.TryGrab(NetworkObjectId, sender, expectedRevision)) return;

            physics.Grab();
            NetworkObject.ChangeOwnership(sender);
            Publish();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReleaseRpc(uint expectedRevision, bool throwing, Vector3 direction, RpcParams rpc = default)
        {
            var sender = rpc.Receive.SenderClientId;
            if (State.Value.Holder != sender || State.Value.Revision != expectedRevision || !TryPlayer(sender, out var player)) return;

            if (throwing && (!Finite(direction) || direction.sqrMagnitude < 0.9f || direction.sqrMagnitude > 1.1f)) return;
            if (!context.Claims.TryRelease(NetworkObjectId, sender, expectedRevision)) return;

            ReleasePhysics();
            if (throwing && physics.InReach(player)) physics.Throw(direction.normalized);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DeleteRpc(uint expectedRevision, RpcParams rpc = default)
        {
            var sender = rpc.Receive.SenderClientId;
            if (State.Value.Holder != sender || State.Value.Revision != expectedRevision || !TryPlayer(sender, out var player) ||
                !physics.InReach(player) || !context.Claims.TryRelease(NetworkObjectId, sender, expectedRevision)) return;

            NetworkObject.Despawn();
        }

        public void ReleaseDisconnected(ulong client)
        {
            if (IsSpawned && IsServer && State.Value.Holder == client &&
                context.Claims.TryRelease(NetworkObjectId, client, State.Value.Revision)) ReleasePhysics();
        }

        private void ReleasePhysics()
        {
            NetworkObject.RemoveOwnership();
            physics.Release();
            Publish();
        }

        private void Publish()
        {
            var claim = context.Claims.For(NetworkObjectId);
            State.Value = new ItemState { Holder = claim.Holder ?? NoHolder, Revision = claim.Revision };
        }

        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        public override void OnNetworkDespawn()
        {
            State.OnValueChanged -= OnStateChanged;
            physics.RestoreCollisions();
            context.UnregisterItem(this);

            context = null;
        }
    }
}
