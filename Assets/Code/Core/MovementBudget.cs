using System;

namespace ArtDock.Core
{
    public sealed class MovementBudget
    {
        private readonly float tolerance;
        private readonly float speed;
        private readonly float capacity;
        private float available;

        public MovementBudget(float tolerance, float speed, float bufferedSeconds)
        {
            if (!float.IsFinite(tolerance) || tolerance < 0)
                throw new ArgumentOutOfRangeException(nameof(tolerance));
            if (!float.IsFinite(speed) || speed < 0)
                throw new ArgumentOutOfRangeException(nameof(speed));
            if (!float.IsFinite(bufferedSeconds) || bufferedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(bufferedSeconds));
            
            this.tolerance = tolerance;
            this.speed = speed;
            capacity = tolerance + speed * bufferedSeconds;
            Reset();
        }

        public bool TryConsume(float distance, float elapsedSeconds)
        {
            if (!float.IsFinite(distance) || distance < 0 || !float.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
                return false;
            
            available = Math.Min(capacity, available + speed * elapsedSeconds);
            if (distance > available) return false;
            
            available -= distance;
            return true;
        }

        public void Reset() => available = tolerance;
    }
}
