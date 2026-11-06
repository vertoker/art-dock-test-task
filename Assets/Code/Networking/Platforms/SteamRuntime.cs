using System;
using ArtDock.Core;
using Steamworks;
using Steamworks.Data;

namespace ArtDock.Networking.Platforms
{
    /// <summary> Initializes the Steam SDK and handles its callbacks and overlay state </summary>
    internal class SteamRuntime : IDisposable
    {
        private readonly GameSettings settings;
        private bool disposed;

        public event Action<string> JoinRequested;
        public string Error { get; private set; } = string.Empty;
        public bool IsValid => SteamClient.IsValid;
        public bool OverlayOpen { get; private set; }
        public string PlayerId => IsValid ? SteamClient.SteamId.ToString() : string.Empty;
        
        /// <summary> Returns a bounded display name without splitting a UTF-16 surrogate pair </summary>
        public string PlayerName
        {
            get
            {
                var name = IsValid ? SteamClient.Name : GameConstants.SteamPlayer;
                if (string.IsNullOrWhiteSpace(name)) name = GameConstants.SteamPlayer;

                var length = Math.Min(name.Length, settings.MaxNameLength);
                if (length < name.Length && length > 0 && char.IsHighSurrogate(name[length - 1])) length--;

                return name[..length];
            }
        }

        public SteamRuntime(GameSettings settings) => this.settings = settings;

        /// <summary> Starts the SDK and subscribes to Steam events </summary>
        public bool EnsureInitialized()
        {
            if (disposed) return false;

            try
            {
                if (!SteamClient.IsValid)
                {
                    OverlayOpen = false;
                    SteamClient.Init(settings.Steam.AppId, false);
                }
                if (!SteamClient.IsValid) throw new InvalidOperationException(GameErrors.SteamInitializationFailed);

                // Initialize relay access and set connection timeouts
                SteamNetworkingUtils.InitRelayNetworkAccess();
                SteamNetworkingUtils.ConnectionTimeout = (int)(settings.ConnectionTimeout * 1000);
                SteamNetworkingUtils.Timeout = (int)(settings.ConnectionTimeout * 1000);

                // Remove previous subscriptions before initializing again
                Unsubscribe();
                SteamFriends.OnGameLobbyJoinRequested += OnLobbyJoin;
                SteamFriends.OnGameRichPresenceJoinRequested += OnPresenceJoin;
                SteamFriends.OnGameOverlayActivated += OnOverlayActivated;
                
                Error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                Error = string.Format(GameErrors.SteamUnavailable, exception.Message);
                return false;
            }
        }

        /// <summary> Pumps Steam callbacks and records SDK failures for the session </summary>
        public bool RunCallbacks()
        {
            if (disposed || !IsValid) return false;

            try
            {
                SteamClient.RunCallbacks();
                return true;
            }
            catch (Exception exception)
            {
                Error = string.Format(GameErrors.SteamUnavailable, exception.Message);
                return false;
            }
        }

        /// <summary> Forwards a lobby invitation as a session join request </summary>
        private void OnLobbyJoin(Lobby lobby, SteamId friend)
        {
            if (lobby.Id.Value != 0)
                JoinRequested?.Invoke(lobby.Id.ToString());
        }

        /// <summary> Parses a lobby join request from the Steam Rich Presence command </summary>
        private void OnPresenceJoin(Friend friend, string command)
        {
            const string prefix = GameConstants.SteamConnectArgument + " ";
            if (string.IsNullOrEmpty(command) || !command.StartsWith(prefix, StringComparison.Ordinal)) return;

            // Accept only a valid lobby ID from the invitation command
            if (SteamLobby.TryParseId(command[prefix.Length..], out var id))
                JoinRequested?.Invoke(id.ToString());
        }

        /// <summary> Removes SDK event subscriptions before reinitialization or disposal </summary>
        private void Unsubscribe()
        {
            SteamFriends.OnGameLobbyJoinRequested -= OnLobbyJoin;
            SteamFriends.OnGameRichPresenceJoinRequested -= OnPresenceJoin;
            SteamFriends.OnGameOverlayActivated -= OnOverlayActivated;
        }

        private void OnOverlayActivated(bool active) => OverlayOpen = active;

        /// <summary> Unsubscribes SDK events and shuts down the Steam client once </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Unsubscribe();

            if (SteamClient.IsValid)
                SteamClient.Shutdown();
        }
    }
}
