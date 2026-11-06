using System;

namespace ArtDock.Core
{
    /// <summary> Limits movement with a distance budget that refills over time </summary>
    public class MovementBudget
    {
        private readonly float tolerance;
        private readonly float speed;
        private readonly float distanceCapacity;
        private float availableDistance;

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
            distanceCapacity = tolerance + speed * bufferedSeconds;
            
            Reset();
        }

        /// <summary> Refills the distance budget for valid samples, then spends it if affordable.
        /// Unaffordable moves keep the refill </summary>
        public bool TryConsume(float distance, float elapsedSeconds)
        {
            if (!float.IsFinite(distance) || distance < 0 || !float.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
                return false;
            
            availableDistance = Math.Min(distanceCapacity, availableDistance + speed * elapsedSeconds);
            if (distance > availableDistance) return false;
            
            availableDistance -= distance;
            return true;
        }

        public void Reset() => availableDistance = tolerance;
    }
}
