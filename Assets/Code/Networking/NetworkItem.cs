using System.Collections.Generic;
using System;
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
            if (throwing && physics.InReach(player))
            {
                physics.Throw(direction.normalized);
                player.ConfirmThrowRpc();
            }
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

    public struct ItemState : INetworkSerializable, IEquatable<ItemState>
    {
        public ulong Holder;
        public uint Revision;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Holder);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(ItemState other) => Holder == other.Holder && Revision == other.Revision;
    }

    public sealed class ItemPhysics
    {
        private readonly Rigidbody body;
        private readonly BoxCollider collider;
        private readonly GameSettings.ItemConfiguration settings;
        private readonly HashSet<Collider> ignored = new();
        private readonly List<Collider> restore = new();
        private NetworkPlayer collisionHolder;
        private Collider[] holderColliders = Array.Empty<Collider>();

        public ItemPhysics(Rigidbody body, BoxCollider collider, GameSettings.ItemConfiguration settings, bool server)
        {
            this.body = body;
            this.collider = collider;
            this.settings = settings;

            body.isKinematic = !server;
            body.interpolation = server ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }

        public void Grab()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.angularDamping = 8;
        }

        public void Release()
        {
            body.useGravity = true;
            body.angularDamping = 0.05f;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        public void Throw(Vector3 direction) => body.AddForce(direction * settings.ThrowImpulse, ForceMode.VelocityChange);

        public bool InReach(NetworkPlayer player) => Vector3.Distance(Eye(player), body.position) <= settings.InteractionRange;

        public bool VisibleFrom(NetworkPlayer player)
        {
            var eye = Eye(player);
            var delta = body.position - eye;

            foreach (var hit in Physics.RaycastAll(eye, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider != collider && hit.collider.GetComponentInParent<NetworkPlayer>() != player) return false;

            return true;
        }

        public bool Follow(NetworkPlayer player)
        {
            var eye = Eye(player);
            if (Vector3.Distance(eye, body.position) > settings.MaxHolderDistance) return false;

            var target = eye + player.AimDirection * HoldDistance(eye, player.AimDirection, player);
            var blend = 1 - Mathf.Exp(-settings.HoldSharpness * Time.fixedDeltaTime);

            body.linearVelocity = Vector3.ClampMagnitude((target - body.position) * blend / Time.fixedDeltaTime, settings.MaxHoldSpeed);
            body.angularVelocity = Vector3.zero;

            return true;
        }

        public float HoldDistance(Vector3 eye, Vector3 direction, NetworkPlayer player)
        {
            var distance = settings.HoldDistance;

            foreach (var hit in Physics.SphereCastAll(eye, settings.HoldRadius, direction, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == collider || hit.collider.GetComponentInParent<NetworkPlayer>() == player) continue;

                distance = Mathf.Min(distance, Mathf.Max(0, hit.distance - settings.ContactGap));
            }

            return distance;
        }

        public Vector3 ConstrainPresentation(Vector3 desired, NetworkPlayer player = null)
        {
            var origin = body.transform.position;
            var offset = Vector3.ClampMagnitude(desired - origin, settings.MaxVisualOffset);
            var distance = offset.magnitude;
            if (distance < 0.001f) return origin;

            var direction = offset / distance;
            var position = origin + direction * ClampPresentationDistance(origin, direction, distance, player);

            return PresentationOverlaps(position, player) ? origin : position;
        }

        private float ClampPresentationDistance(Vector3 origin, Vector3 direction, float distance, NetworkPlayer player)
        {
            var halfExtents = Vector3.Scale(collider.size, body.transform.lossyScale) * 0.5f;
            var rotation = body.transform.rotation;

            foreach (var hit in Physics.BoxCastAll(origin, halfExtents, direction, rotation, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (IgnorePresentationCollider(hit.collider, player)) continue;

                distance = Mathf.Min(distance, Mathf.Max(0, hit.distance - settings.ContactGap));
            }

            return distance;
        }

        private bool PresentationOverlaps(Vector3 position, NetworkPlayer player)
        {
            var halfExtents = Vector3.Scale(collider.size, body.transform.lossyScale) * 0.5f;
            var rotation = body.transform.rotation;

            foreach (var obstacle in Physics.OverlapBox(position, halfExtents, rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (IgnorePresentationCollider(obstacle, player)) continue;

                if (Physics.ComputePenetration(collider, position, rotation, obstacle, obstacle.transform.position,
                        obstacle.transform.rotation, out _, out var depth) && depth > 0.001f) return true;
            }

            return false;
        }

        private bool IgnorePresentationCollider(Collider other, NetworkPlayer player) =>
            other == collider || player && other.GetComponentInParent<NetworkPlayer>() == player;

        public void UpdateCollisions(NetworkPlayer holder)
        {
            UpdateHolderColliders(holder);
            IgnoreHolderCollisions();
            RestoreSeparatedCollisions();
        }

        private void UpdateHolderColliders(NetworkPlayer holder)
        {
            if (collisionHolder == holder) return;

            collisionHolder = holder;
            holderColliders = holder ? holder.GetComponentsInChildren<Collider>() : Array.Empty<Collider>();
        }

        private void IgnoreHolderCollisions()
        {
            foreach (var playerCollider in holderColliders)
                if (playerCollider && playerCollider.enabled && ignored.Add(playerCollider))
                    Physics.IgnoreCollision(collider, playerCollider, true);
        }

        private void RestoreSeparatedCollisions()
        {
            restore.Clear();

            foreach (var playerCollider in ignored)
            {
                if (!playerCollider)
                {
                    restore.Add(playerCollider);
                    continue;
                }

                if (Array.IndexOf(holderColliders, playerCollider) >= 0 && playerCollider.enabled) continue;
                if (collider.bounds.Intersects(playerCollider.bounds)) continue;

                Physics.IgnoreCollision(collider, playerCollider, false);
                restore.Add(playerCollider);
            }

            foreach (var playerCollider in restore) ignored.Remove(playerCollider);
        }

        public void RestoreCollisions()
        {
            foreach (var playerCollider in ignored)
                if (playerCollider && collider) Physics.IgnoreCollision(collider, playerCollider, false);

            ignored.Clear();
        }

        private static Vector3 Eye(NetworkPlayer player) => player.TrustedPosition + Vector3.up * player.Context.Settings.Player.EyeHeight;
    }

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
