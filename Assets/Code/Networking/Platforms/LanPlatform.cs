using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ArtDock.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace ArtDock.Networking.Platforms
{
    public class LanPlatform : IConnectionPlatform
    {
        private readonly NetworkManager manager;
        private readonly UnityTransport transport;
        private readonly LaunchOptions options;

        public ConnectionMode Mode => ConnectionMode.Lan;
        public string PlayerId { get; }
        public string PlayerName => options.PlayerName;
        public string LobbyId => "";
        public string Error { get; private set; } = "";
        public event Action<string> JoinRequested { add { } remove { } }
        public event Action<ConnectionMode, string> ConnectionLost { add { } remove { } }

        public LanPlatform(NetworkManager manager, LaunchOptions options)
        {
            this.manager = manager;
            this.options = options;
            transport = manager.GetComponent<UnityTransport>();
            if (!transport)
                throw new InvalidOperationException(GameErrors.TransportNotAssigned);
            
            var key = $"{GameConstants.GameName}.PlayerId.{options.Profile}";
            PlayerId = PlayerPrefs.GetString(key, "");
            
            if (string.IsNullOrEmpty(PlayerId))
            {
                PlayerId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(key, PlayerId);
                PlayerPrefs.Save();
            }
        }

        public Task<bool> PrepareHost(string title, int capacity, bool isPrivate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Error = "";
            manager.NetworkConfig.NetworkTransport = transport;
            transport.SetConnectionData(GameConstants.LoopbackAddress, options.Port, GameConstants.ListenAddress);
            return Task.FromResult(true);
        }

        public Task<bool> PrepareJoin(string address, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Error = "";
            var parts = (address ?? "").Trim().Split(':');
            var port = options.Port;
            if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
                (parts.Length == 2 && (!ushort.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port == 0)))
            {
                Error = GameErrors.InvalidAddress;
                return Task.FromResult(false);
            }
            manager.NetworkConfig.NetworkTransport = transport;
            transport.SetConnectionData(ip.ToString(), port);
            return Task.FromResult(true);
        }

        public void Tick() { }
        public void UpdatePresence(bool inGame, int players, int capacity, bool acceptingPlayers) { }
        public void Leave() { }
        public void Dispose() { }
    }
}
