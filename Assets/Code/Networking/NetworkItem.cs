using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Validates item commands on the server and synchronizes the holder and revision </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider), typeof(ItemNetworkTransform))]
    public class NetworkItem : NetworkBehaviour
    {
        public const ulong NoHolder = ulong.MaxValue;
        [SerializeField] private Transform presentation;
        
        public readonly NetworkVariable<ItemState> State = new(new ItemState { Holder = NoHolder });
        
        private NetworkContext context;
        private ItemPhysics physics;
        private ItemPresentation visual;

        /// <summary> Creates item physics and presentation, then registers the item in the current world </summary>
        public override void OnNetworkSpawn()
        {
            context = NetworkManager.GetComponent<NetworkContext>();
            physics = new ItemPhysics(GetComponent<Rigidbody>(), GetComponent<BoxCollider>(), context.Settings.Items, IsServer);
            visual = new ItemPresentation(this, presentation, physics, context);
            NetworkObject.DontDestroyWithOwner = true;

            context.RegisterItem(this);
            State.OnValueChanged += OnStateChanged;
        }

        /// <summary> Updates the held item lookup after a holder change </summary>
        private void OnStateChanged(ItemState previous, ItemState current) => context.UpdateItemHolder(this, previous.Holder, current.Holder);
        
        private void LateUpdate()
        {
            if (IsSpawned)
                visual.Tick();
        }

        /// <summary> Updates holder collisions and performs server holding and world bounds checks </summary>
        private void FixedUpdate()
        {
            if (!IsSpawned) return;

            // Update holder collisions on both the server and clients
            context.Players.TryGetValue(State.Value.Holder, out var holder);
            physics.UpdateCollisions(holder);

            if (!IsServer) return;

            // The server removes items outside world bounds
            if (PlayerValidation.OutsideBounds(transform.position, context.Settings))
            {
                NetworkObject.Despawn();
                return;
            }

            if (State.Value.Holder == NoHolder) return;

            // Release the item if its holder is unavailable or can no longer hold it
            if (!holder || !holder.IsSpawned || !holder.CanAct || !physics.Follow(holder))
                ReleaseDisconnected(State.Value.Holder);
        }

        /// <summary> Finds the command sender and checks the item action cooldown </summary>
        private bool TryPlayer(ulong sender, out NetworkPlayer player) => context.Players.TryGetValue(sender, out player) &&
            player && player.TryConsumeItemAction();

        /// <summary> Grabs a free item after the server validates the player and revision </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void GrabRpc(uint expectedRevision, RpcParams rpc = default)
        {
            var sender = rpc.Receive.SenderClientId;
            
            // Check reach, line of sight and the current claim revision
            if (!TryPlayer(sender, out var player) || !physics.InReach(player) || !physics.VisibleFrom(player) ||
                !context.Claims.TryGrab(NetworkObjectId, sender, expectedRevision)) return;

            physics.Grab();
            NetworkObject.ChangeOwnership(sender);
            Publish();
        }

        /// <summary> Drops or throws an item at the current holder's request </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReleaseRpc(uint expectedRevision, bool throwing, Vector3 direction, RpcParams rpc = default)
        {
            var sender = rpc.Receive.SenderClientId;
            
            // Only the current holder with the current revision can issue this command
            if (State.Value.Holder != sender || State.Value.Revision != expectedRevision || !TryPlayer(sender, out var player)) return;
            
            // The throw direction must be finite and approximately unit length
            if (throwing && (!Finite(direction) || direction.sqrMagnitude < 0.9f || direction.sqrMagnitude > 1.1f)) return;
            // Release the server claim before restoring item physics
            if (!context.Claims.TryRelease(NetworkObjectId, sender, expectedRevision)) return;

            ReleasePhysics();
            if (throwing && physics.InReach(player))
            {
                physics.Throw(direction.normalized);
                player.ConfirmThrowRpc();
            }
        }

        /// <summary> Removes an item after validating its holder's request </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DeleteRpc(uint expectedRevision, RpcParams rpc = default)
        {
            var sender = rpc.Receive.SenderClientId;
            // Check the holder, revision and reach before removing the item
            if (State.Value.Holder != sender || State.Value.Revision != expectedRevision || !TryPlayer(sender, out var player) ||
                !physics.InReach(player) || !context.Claims.TryRelease(NetworkObjectId, sender, expectedRevision)) return;

            NetworkObject.Despawn();
        }

        /// <summary> Releases the current claim when its holder disconnects or can no longer hold the item </summary>
        public void ReleaseDisconnected(ulong client)
        {
            // Release only the disconnected client's claim
            if (IsSpawned && IsServer && State.Value.Holder == client &&
                context.Claims.TryRelease(NetworkObjectId, client, State.Value.Revision)) ReleasePhysics();
        }

        /// <summary> Returns network ownership to the server and publishes the released claim state </summary>
        private void ReleasePhysics()
        {
            NetworkObject.RemoveOwnership();
            physics.Release();
            Publish();
        }

        /// <summary> Copies the server claim holder and revision into the network state </summary>
        private void Publish()
        {
            var claim = context.Claims.For(NetworkObjectId);
            State.Value = new ItemState { Holder = claim.Holder ?? NoHolder, Revision = claim.Revision };
        }

        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        /// <summary> Restores collisions and removes the item from the world context </summary>
        public override void OnNetworkDespawn()
        {
            State.OnValueChanged -= OnStateChanged;
            physics.RestoreCollisions();
            context.UnregisterItem(this);

            context = null;
        }
    }

}
