using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ArtDock.Core;
using UnityEngine;

namespace ArtDock.Networking.Platforms
{
    /// <summary> Advertises open LAN sessions over UDP and tracks discovered hosts </summary>
    public class LanDiscovery : IDisposable
    {
        /// <summary> Discovered host details and the time of its latest announcement </summary>
        public class Host
        {
            public string Address { get; internal set; }
            public string Title { get; internal set; }
            public int Players { get; internal set; }
            public int Capacity { get; internal set; }
            internal float SeenAt;
        }

        /// <summary> Announcement payload containing the game marker, protocol and session details </summary>
        [Serializable]
        private class Beacon
        {
            public string magic;
            public string version;
            public string title;
            public int port;
            public int players;
            public int capacity;
        }

        private const string Marker = GameConstants.GameName + ".lan-session";
        private static readonly UTF8Encoding Encoding = new(false, true);
        
        private readonly GameSettings settings;
        private readonly IGameText text;
        private readonly Dictionary<string, Host> hosts = new();
        private readonly List<string> expired = new();
        private readonly byte[] buffer;
        
        private Socket receiver;
        private UdpClient sender;
        private float nextBeacon;

        public event Action Changed;
        public IReadOnlyCollection<Host> Hosts => hosts.Values;
        public bool Searching => receiver != null;
        public string Status { get; private set; }
        public string BroadcastError { get; private set; } = string.Empty;

        public LanDiscovery(GameSettings settings, IGameText text)
        {
            this.settings = settings;
            this.text = text;
            Status = text[TextKeys.DiscoverySearchHint];
            buffer = new byte[settings.Discovery.MaxPayloadBytes + 1];
        }

        public void StartSearch()
        {
            StopSearch();

            try
            {
                // Let local instances share the discovery port without blocking the main thread
                receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                receiver.ExclusiveAddressUse = false;
                receiver.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                receiver.ReceiveBufferSize = 65536;
                receiver.Bind(new IPEndPoint(IPAddress.Any, settings.Discovery.Port));
                receiver.Blocking = false;
                
                Status = text[TextKeys.SearchingLan];
            }
            catch (SocketException exception)
            {
                receiver?.Dispose();
                receiver = null;
                Status = string.Format(GameErrors.DiscoveryUnavailable, exception.Message);
            }

            Changed?.Invoke();
        }

        public void StopSearch()
        {
            receiver?.Dispose();
            receiver = null;
            hosts.Clear();
            
            Status = text[TextKeys.DiscoverySearchHint];
            Changed?.Invoke();
        }

        public void Tick(bool advertise, string title, ushort port, int players, int capacity)
        {
            if (advertise && Time.unscaledTime >= nextBeacon)
            {
                nextBeacon = Time.unscaledTime + settings.Discovery.BeaconInterval;
                Broadcast(title, port, players, capacity);
            }
            else if (!advertise && sender != null)
            {
                sender.Dispose();
                sender = null;
                BroadcastError = string.Empty;
                nextBeacon = 0;
            }

            if (!Searching) return;

            ExpireHosts();
            ReceiveHosts();
            if (Searching) SetStatus(text[hosts.Count == 0 ? TextKeys.DiscoveryNoHosts : TextKeys.DiscoverySelectHost]);
        }

        private void Broadcast(string title, ushort port, int players, int capacity)
        {
            try
            {
                sender ??= new UdpClient { EnableBroadcast = true };
                var beacon = new Beacon
                {
                    magic = Marker,
                    version = settings.ProtocolVersion,
                    title = title,
                    port = port,
                    players = players,
                    capacity = capacity,
                };
                
                var bytes = Encoding.GetBytes(JsonUtility.ToJson(beacon));
                if (bytes.Length > settings.Discovery.MaxPayloadBytes) return;

                sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, settings.Discovery.Port));
                BroadcastError = string.Empty;
            }
            catch (SocketException)
            {
                sender?.Dispose();
                sender = null;
                nextBeacon = Time.unscaledTime + settings.Discovery.RetryInterval;
                BroadcastError = GameErrors.DiscoveryBroadcastBlocked;
            }
        }

        private void ReceiveHosts()
        {
            for (var i = 0; i < settings.Discovery.PacketsPerTick; i++)
            {
                try
                {
                    if (receiver.Available == 0) break;

                    ReceiveHost();
                }
                catch (SocketException exception)
                {
                    if (!HandleReceiveError(exception)) break;
                }
                catch (ArgumentException) { }
            }
        }

        private void ReceiveHost()
        {
            EndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
            var length = receiver.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endpoint);
            if (length == 0 || length > settings.Discovery.MaxPayloadBytes) return;

            var beacon = JsonUtility.FromJson<Beacon>(Encoding.GetString(buffer, 0, length));
            if (Valid(beacon)) Remember(beacon, ((IPEndPoint)endpoint).Address);
        }

        private bool HandleReceiveError(SocketException exception)
        {
            if (exception.SocketErrorCode == SocketError.WouldBlock) return false;
            if (exception.SocketErrorCode == SocketError.MessageSize) return true;

            receiver.Dispose();
            receiver = null;
            SetStatus(string.Format(GameErrors.DiscoveryUnavailable, exception.Message));
            return false;
        }

        private bool Valid(Beacon beacon)
        {
            if (beacon == null || beacon.magic != Marker || beacon.version != settings.ProtocolVersion) return false;

            if (beacon.port is < 1 or > 65535 || beacon.capacity < 2 || beacon.capacity > settings.MaxPlayers ||
                beacon.players < 1 || beacon.players > beacon.capacity) return false;

            if (string.IsNullOrWhiteSpace(beacon.title) || beacon.title.Length > settings.MaxNameLength) return false;
            foreach (var character in beacon.title)
                if (char.IsControl(character)) return false;

            return true;
        }

        private void Remember(Beacon beacon, IPAddress ip)
        {
            var address = $"{ip}:{beacon.port}";
            if (!hosts.TryGetValue(address, out var host))
            {
                if (hosts.Count >= settings.Discovery.MaxHosts) return;

                host = new Host { Address = address };
                hosts.Add(address, host);
            }

            var changed = host.Title != beacon.title || host.Players != beacon.players || host.Capacity != beacon.capacity;
            host.Title = beacon.title;
            host.Players = beacon.players;
            host.Capacity = beacon.capacity;
            host.SeenAt = Time.unscaledTime;
            if (changed) Changed?.Invoke();
        }

        private void ExpireHosts()
        {
            expired.Clear();
            foreach (var pair in hosts)
            {
                if (Time.unscaledTime - pair.Value.SeenAt >= settings.Discovery.HostLifetime)
                    expired.Add(pair.Key);
            }

            foreach (var address in expired) hosts.Remove(address);
            if (expired.Count > 0) Changed?.Invoke();
        }

        private void SetStatus(string status)
        {
            if (Status == status) return;

            Status = status;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            StopSearch();
            sender?.Dispose();
            sender = null;
        }
    }
}
