using System;
using System.Collections.Generic;
using ArtDock.Core;

namespace ArtDock.Networking
{
    public class PlatformService : IDisposable
    {
        private readonly Dictionary<ConnectionMode, IConnectionPlatform> platforms = new();
        
        public event Action<string> JoinRequested;
        public event Action<ConnectionMode, string> ConnectionLost;

        public PlatformService(IEnumerable<IConnectionPlatform> platforms)
        {
            foreach (var platform in platforms)
            {
                this.platforms.Add(platform.Mode, platform);
                platform.JoinRequested += OnJoinRequested;
                platform.ConnectionLost += OnConnectionLost;
            }
        }

        public void Dispose()
        {
            foreach (var platform in platforms.Values)
            {
                platform.JoinRequested -= OnJoinRequested;
                platform.ConnectionLost -= OnConnectionLost;
            }
        }

        public IConnectionPlatform For(ConnectionMode mode)
        {
            return platforms.TryGetValue(mode, out var platform) ? platform
                : throw new InvalidOperationException(string.Format(GameErrors.ConnectionModeUnavailable, mode));
        }

        public void Tick()
        {
            foreach (var platform in platforms.Values)
                platform.Tick();
        }

        private void OnJoinRequested(string address) => JoinRequested?.Invoke(address);
        private void OnConnectionLost(ConnectionMode mode, string reason) => ConnectionLost?.Invoke(mode, reason);
    }
}
