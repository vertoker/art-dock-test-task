using System.Globalization;
using ArtDock.Core;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace ArtDock.Networking.Platforms
{
    internal sealed class SteamLobby
    {
        private const string GameKey = "game";
        private const string ProtocolKey = "protocol";
        private const string HostKey = "host";
        private const string NameKey = "name";
        private const string PlayersKey = "players";
        private const string CapacityKey = "capacity";
        private const string PhaseKey = "phase";
        private const string ConnectKey = "connect";
        private const string StatusKey = "status";
        private const string LobbyPhase = "lobby";
        private const string PlayingPhase = "game";
        
        private const string Marker = GameConstants.GameName + ".session";
        private readonly GameSettings settings;
        private Lobby? lobby;
        private bool ownsLobby;
        private ulong host;
        private float nextPresence;
        private bool? lastInGame;
        private bool lastAccepting;
        private int lastPlayers;
        private int lastCapacity;

        public string Id => lobby?.Id.ToString() ?? string.Empty;
        public bool Active => lobby.HasValue;
        public bool CanInvite => Active && SteamClient.IsValid && SteamUtils.IsOverlayEnabled;
        public ulong Host => host;
        public bool HostAvailable => !Active || ownsLobby || SteamClient.IsValid && lobby.Value.Owner.Id.Value == host &&
            TryParseId(lobby.Value.GetData(HostKey), out var current) && current == host;

        public SteamLobby(GameSettings settings) => this.settings = settings;

        public bool HostLobby(Lobby created, string title, int capacity, bool isPrivate)
        {
            lobby = created;
            ownsLobby = true;
            host = SteamClient.SteamId.Value;
            nextPresence = 0;
            lastInGame = null;

            var configured = created.SetData(GameKey, Marker) && created.SetData(ProtocolKey, settings.ProtocolVersion) &&
                created.SetData(HostKey, host.ToString(CultureInfo.InvariantCulture)) && created.SetData(NameKey, title) &&
                created.SetData(CapacityKey, capacity.ToString(CultureInfo.InvariantCulture));
            var visibility = isPrivate ? created.SetPrivate() : created.SetFriendsOnly();

            return configured && visibility && created.SetJoinable(false);
        }

        public bool JoinLobby(Lobby joined)
        {
            lobby = joined;
            ownsLobby = false;
            nextPresence = 0;
            lastInGame = null;

            if (joined.GetData(GameKey) != Marker || joined.GetData(ProtocolKey) != settings.ProtocolVersion ||
                !TryParseId(joined.GetData(HostKey), out host) || joined.Owner.Id.Value != host || host == SteamClient.SteamId.Value)
                return false;

            return true;
        }

        public void UpdatePresence(bool inGame, int players, int capacity, bool accepting)
        {
            if (!Active || !SteamClient.IsValid) return;
            if (Time.unscaledTime < nextPresence && lastInGame == inGame && lastAccepting == accepting &&
                lastPlayers == players && lastCapacity == capacity) return;

            nextPresence = Time.unscaledTime + settings.Steam.PresenceInterval;
            lastInGame = inGame;
            lastAccepting = accepting;
            lastPlayers = players;
            lastCapacity = capacity;

            if (ownsLobby)
            {
                var current = lobby.Value;
                current.SetData(PlayersKey, players.ToString(CultureInfo.InvariantCulture));
                current.SetData(CapacityKey, capacity.ToString(CultureInfo.InvariantCulture));
                current.SetData(PhaseKey, inGame ? PlayingPhase : LobbyPhase);
                current.SetJoinable(accepting);
            }

            SteamFriends.SetRichPresence(StatusKey, inGame ? string.Format(GameConstants.SteamGameStatus, players, capacity) : GameConstants.SteamLobbyStatus);
            SteamFriends.SetRichPresence(ConnectKey, accepting ? GameConstants.SteamConnectArgument + " " + Id : null);
        }

        public void InviteFriends()
        {
            if (CanInvite) SteamFriends.OpenGameInviteOverlay(lobby.Value.Id);
        }

        public void Close()
        {
            if (!Active || !SteamClient.IsValid) return;

            if (ownsLobby)
            {
                var current = lobby.Value;
                current.SetJoinable(false);
                current.SetData(HostKey, string.Empty);
            }

            SteamFriends.ClearRichPresence();
        }

        public void Leave()
        {
            Close();
            if (Active && SteamClient.IsValid) lobby.Value.Leave();

            lobby = null;
            ownsLobby = false;
            host = 0;
            lastInGame = null;
        }

        public static bool TryParseId(string value, out ulong id) =>
            ulong.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0;
    }
}
