using System.Globalization;
using ArtDock.Core;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace ArtDock.Networking.Platforms
{
    /// <summary> Tracks the Steam lobby, its original host and the session data shown to friends </summary>
    internal class SteamLobby
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
        public bool CanInvite => Active && SteamClient.IsValid;
        public ulong Host => host;
        /// <summary> Requires the lobby owner and stored host metadata to match the original game host </summary>
        public bool HostAvailable => !Active || ownsLobby || SteamClient.IsValid && lobby.Value.Owner.Id.Value == host &&
            TryParseId(lobby.Value.GetData(HostKey), out var current) && current == host;

        public SteamLobby(GameSettings settings) => this.settings = settings;

        /// <summary> Stores host metadata and configures a lobby that stays closed until the session is ready </summary>
        public bool HostLobby(Lobby created, string title, int capacity, bool isPrivate)
        {
            lobby = created;
            ownsLobby = true;
            host = SteamClient.SteamId.Value;
            nextPresence = 0;
            lastInGame = null;

            // Store the game marker, protocol and original host in lobby metadata
            var configured = created.SetData(GameKey, Marker) && created.SetData(ProtocolKey, settings.ProtocolVersion) &&
                created.SetData(HostKey, host.ToString(CultureInfo.InvariantCulture)) && created.SetData(NameKey, title) &&
                created.SetData(CapacityKey, capacity.ToString(CultureInfo.InvariantCulture));
            
            var visibility = isPrivate ? created.SetPrivate() : created.SetFriendsOnly();

            return configured && visibility && created.SetJoinable(false);
        }

        /// <summary> Accepts only a compatible lobby still owned by its original host </summary>
        public bool JoinLobby(Lobby joined)
        {
            lobby = joined;
            ownsLobby = false;
            nextPresence = 0;
            lastInGame = null;

            // A new lobby owner does not replace the original game host
            return joined.GetData(GameKey) == Marker && joined.GetData(ProtocolKey) == settings.ProtocolVersion &&
                   TryParseId(joined.GetData(HostKey), out host) && joined.Owner.Id.Value == host && host != SteamClient.SteamId.Value;
        }

        /// <summary> Updates player counts, game phase and join availability in lobby metadata and Rich Presence </summary>
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
                // Only the original host updates lobby metadata
                var current = lobby.Value;
                current.SetData(PlayersKey, players.ToString(CultureInfo.InvariantCulture));
                current.SetData(CapacityKey, capacity.ToString(CultureInfo.InvariantCulture));
                current.SetData(PhaseKey, inGame ? PlayingPhase : LobbyPhase);
                current.SetJoinable(accepting);
            }

            SteamFriends.SetRichPresence(StatusKey, inGame ? string.Format(GameConstants.SteamGameStatus, players, capacity) : GameConstants.SteamLobbyStatus);
            SteamFriends.SetRichPresence(ConnectKey, accepting ? GameConstants.SteamConnectArgument + " " + Id : null);
        }

        /// <summary> Opens the Steam invitation overlay when it is available </summary>
        public void InviteFriends()
        {
            if (CanInvite && SteamUtils.IsOverlayEnabled) SteamFriends.OpenGameInviteOverlay(lobby.Value.Id);
        }

        public bool InviteFriend(ulong id) => CanInvite && lobby.Value.InviteFriend(id);

        /// <summary> Closes lobby admission and clears presence before the transport stops </summary>
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

        /// <summary> Closes access, leaves the native lobby and resets local lobby state </summary>
        public void Leave()
        {
            Close();
            if (Active && SteamClient.IsValid)
                lobby.Value.Leave();

            lobby = null;
            ownsLobby = false;
            host = 0;
            lastInGame = null;
        }

        /// <summary> Accepts only a nonzero numeric Steam lobby ID </summary>
        public static bool TryParseId(string value, out ulong id) =>
            ulong.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0;
    }
}
