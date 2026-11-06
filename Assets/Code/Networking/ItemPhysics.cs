using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Handles server item physics, obstacle queries and holder collisions </summary>
    public class ItemPhysics
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static readonly Collider[] Overlaps = new Collider[32];

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

            var hitCount = Physics.RaycastNonAlloc(eye, delta.normalized, Hits,
                delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (hitCount == Hits.Length) return false;

            for (var i = 0; i < hitCount; i++)
            {
                var hit = Hits[i];
                if (hit.collider != collider && hit.collider.GetComponentInParent<NetworkPlayer>() != player) return false;
            }

            return true;
        }

        /// <summary> Moves the item toward the hold position and reports whether holding can continue </summary>
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

            var hitCount = Physics.SphereCastNonAlloc(eye, settings.HoldRadius, direction, Hits,
                distance, ~0, QueryTriggerInteraction.Ignore);
            if (hitCount == Hits.Length) return 0;

            for (var i = 0; i < hitCount; i++)
            {
                var hit = Hits[i];
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

            var hitCount = Physics.BoxCastNonAlloc(origin, halfExtents, direction, Hits,
                rotation, distance, ~0, QueryTriggerInteraction.Ignore);
            if (hitCount == Hits.Length) return 0;

            for (var i = 0; i < hitCount; i++)
            {
                var hit = Hits[i];
                if (IgnorePresentationCollider(hit.collider, player)) continue;

                distance = Mathf.Min(distance, Mathf.Max(0, hit.distance - settings.ContactGap));
            }

            return distance;
        }

        private bool PresentationOverlaps(Vector3 position, NetworkPlayer player)
        {
            var halfExtents = Vector3.Scale(collider.size, body.transform.lossyScale) * 0.5f;
            var rotation = body.transform.rotation;

            var overlapCount = Physics.OverlapBoxNonAlloc(position, halfExtents, Overlaps,
                rotation, ~0, QueryTriggerInteraction.Ignore);
            if (overlapCount == Overlaps.Length) return true;

            for (var i = 0; i < overlapCount; i++)
            {
                var obstacle = Overlaps[i];
                if (IgnorePresentationCollider(obstacle, player)) continue;

                if (Physics.ComputePenetration(collider, position, rotation, obstacle, obstacle.transform.position,
                        obstacle.transform.rotation, out _, out var depth) && depth > 0.001f) return true;
            }

            return false;
        }

        private bool IgnorePresentationCollider(Collider other, NetworkPlayer player) =>
            other == collider || player && other.GetComponentInParent<NetworkPlayer>() == player;

        /// <summary> Updates ignored holder collisions and restores them after separation </summary>
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

                // Keep ignoring the current holder and colliders that still intersect
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
}
