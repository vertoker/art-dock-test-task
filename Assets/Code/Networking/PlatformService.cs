using System;
using System.Collections.Generic;
using ArtDock.Core;

namespace ArtDock.Networking
{
    public class PlatformService : IDisposable
    {
        private readonly Dictionary<ConnectionMode, IConnectionPlatform> platforms = new();
        private readonly Dictionary<ConnectionMode, Action<string>> joinHandlers = new();
        
        public event Action<ConnectionMode, string> JoinRequested;
        public event Action<ConnectionMode, string> ConnectionLost;

        public PlatformService(IEnumerable<IConnectionPlatform> platforms)
        {
            foreach (var platform in platforms)
            {
                this.platforms.Add(platform.Mode, platform);
                Action<string> join = address => JoinRequested?.Invoke(platform.Mode, address);
                joinHandlers.Add(platform.Mode, join);
                platform.JoinRequested += join;
                platform.ConnectionLost += OnConnectionLost;
            }
        }

        public void Dispose()
        {
            foreach (var platform in platforms.Values)
            {
                platform.JoinRequested -= joinHandlers[platform.Mode];
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

        private void OnConnectionLost(ConnectionMode mode, string reason) => ConnectionLost?.Invoke(mode, reason);
    }
}
