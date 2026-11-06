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
    /// <summary> Prepares IPv4 connections through UnityTransport and stores the local player profile </summary>
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

        /// <summary> Selects the LAN transport and listens on the configured port </summary>
        public Task<bool> PrepareHost(string title, int capacity, bool isPrivate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Error = string.Empty;
            
            // Clients use the host address while the host listens on all local interfaces
            manager.NetworkConfig.NetworkTransport = transport;
            transport.SetConnectionData(GameConstants.LoopbackAddress, options.Port, GameConstants.ListenAddress);
            return Task.FromResult(true);
        }

        /// <summary> Validates the IPv4 address and port before configuring the client transport </summary>
        public Task<bool> PrepareJoin(string address, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Error = string.Empty;

            if (!TryParseAddress(address, out var ip, out var port))
            {
                Error = GameErrors.InvalidAddress;
                return Task.FromResult(false);
            }

            manager.NetworkConfig.NetworkTransport = transport;
            transport.SetConnectionData(ip.ToString(), port);
            return Task.FromResult(true);
        }

        /// <summary> Parses IPv4 with an optional port, falling back to the launch port </summary>
        private bool TryParseAddress(string address, out IPAddress ip, out ushort port)
        {
            var parts = (address ?? string.Empty).Trim().Split(':');
            ip = null;
            port = options.Port;
            if (parts.Length > 2) return false;

            if (!IPAddress.TryParse(parts[0], out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            if (parts.Length == 1) return true;

            return ushort.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out port) && port != 0;
        }

        public void Tick() { }
        public void UpdatePresence(bool inGame, int players, int capacity, bool acceptingPlayers) { }
        public void Leave() { }
        public void Dispose() { }
    }
}
