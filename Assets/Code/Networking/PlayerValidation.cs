using ArtDock.Core;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Validates player movement and aim on the server, retaining the last trusted position </summary>
    public class PlayerValidation
    {
        private readonly MovementBudget movement;
        private readonly GameSettings.PlayerConfiguration settings;
        private float nextAim;

        public Vector3 TrustedPosition { get; private set; }
        public Vector3 AimDirection { get; private set; } = Vector3.forward;
        public bool PositionValid { get; private set; } = true;
        public PlayerValidation(GameSettings settings, Vector3 position)
        {
            this.settings = settings.Player;
            movement = new MovementBudget(this.settings.PositionTolerance, this.settings.AllowedPositionSpeed,
                this.settings.BufferedMovementSeconds);
            Reset(position);
        }

        /// <summary> Accepts a finite position only when its displacement fits the movement budget </summary>
        public bool TryMove(Vector3 position, float elapsed)
        {
            var distance = Vector3.Distance(TrustedPosition, position);
            PositionValid = Finite(position) && movement.TryConsume(distance, elapsed);
            if (PositionValid) TrustedPosition = position;
            return PositionValid;
        }

        /// <summary> Accepts a finite unit aim direction at the allowed update rate </summary>
        public bool TryAim(Vector3 direction, float now)
        {
            if (!float.IsFinite(now) || now < nextAim || !Finite(direction) ||
                direction.sqrMagnitude < 0.9f || direction.sqrMagnitude > 1.1f) return false;
            nextAim = now + settings.AimReceiveInterval;
            AimDirection = direction.normalized;
            return true;
        }

        /// <summary> Sets a trusted position and resets the movement budget after respawn </summary>
        public void Reset(Vector3 position)
        {
            TrustedPosition = position;
            PositionValid = true;
            movement.Reset();
        }

        /// <summary> Marks the latest transform state as invalid until correction </summary>
        internal void RejectPosition() => PositionValid = false;

        /// <summary> Places a player in a centered spawn slot using the configured spacing </summary>
        public static Vector3 SpawnPosition(ulong clientId, GameSettings settings)
        {
            var slot = (int)(clientId % (ulong)settings.MaxPlayers);
            var offset = (slot - (settings.MaxPlayers - 1) * 0.5f) * settings.Player.SpawnSpacing;
            return settings.Player.SpawnOrigin + Vector3.right * offset;
        }

        /// <summary> Rejects non-finite positions and positions outside the playable bounds </summary>
        public static bool OutsideBounds(Vector3 position, GameSettings settings)
        {
            var min = settings.Player.MinimumPosition;
            var max = settings.Player.MaximumPosition;
            return !Finite(position) || position.x < min.x || position.y < min.y || position.z < min.z ||
                   position.x > max.x || position.y > max.y || position.z > max.z;
        }

        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
