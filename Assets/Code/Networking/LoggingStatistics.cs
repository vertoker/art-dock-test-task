using ArtDock.Core;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Batches confirmed throws and writes their statistics to the application log </summary>
    public sealed class LoggingStatistics : IPlayerStatistics
    {
        private readonly GameSettings settings;
        private readonly IGameText text;
        private int pending;
        private int total;

        public LoggingStatistics(GameSettings settings, IGameText text)
        {
            this.settings = settings;
            this.text = text;
        }

        public void ConfirmThrow()
        {
            pending++;
            total++;
        }

        public void Flush()
        {
            if (pending == 0) return;

            var achievement = total >= settings.Statistics.AchievementThreshold
                ? text[TextKeys.AchievementReached] : text[TextKeys.AchievementPending];
            
            Debug.Log(text.Format(TextKeys.StatisticsBatch, total, pending, settings.Statistics.AchievementThreshold, achievement));
            pending = 0;
        }
    }
}
