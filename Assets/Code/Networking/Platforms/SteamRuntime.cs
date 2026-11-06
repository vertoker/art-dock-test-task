using System;
using ArtDock.Core;
using Steamworks;
using Steamworks.Data;

namespace ArtDock.Networking.Platforms
{
    internal sealed class SteamRuntime : IDisposable
    {
        private readonly GameSettings settings;
        private bool disposed;

        public event Action<string> JoinRequested;
        public string Error { get; private set; } = string.Empty;
        public bool IsValid => SteamClient.IsValid;
        public string PlayerId => IsValid ? SteamClient.SteamId.ToString() : string.Empty;
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

        public bool EnsureInitialized()
        {
            if (disposed) return false;

            try
            {
                if (!SteamClient.IsValid) SteamClient.Init(settings.Steam.AppId, false);
                if (!SteamClient.IsValid) throw new InvalidOperationException(GameErrors.SteamInitializationFailed);

                SteamNetworkingUtils.InitRelayNetworkAccess();
                SteamNetworkingUtils.ConnectionTimeout = (int)(settings.ConnectionTimeout * 1000);
                SteamNetworkingUtils.Timeout = (int)(settings.ConnectionTimeout * 1000);

                Unsubscribe();
                SteamFriends.OnGameLobbyJoinRequested += OnLobbyJoin;
                SteamFriends.OnGameRichPresenceJoinRequested += OnPresenceJoin;
                Error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                Error = string.Format(GameErrors.SteamUnavailable, exception.Message);
                return false;
            }
        }

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

        private void OnLobbyJoin(Lobby lobby, SteamId friend)
        {
            if (lobby.Id.Value != 0) JoinRequested?.Invoke(lobby.Id.ToString());
        }

        private void OnPresenceJoin(Friend friend, string command)
        {
            var prefix = GameConstants.SteamConnectArgument + " ";
            if (string.IsNullOrEmpty(command) || !command.StartsWith(prefix, StringComparison.Ordinal)) return;

            if (SteamLobby.TryParseId(command[prefix.Length..], out var id)) JoinRequested?.Invoke(id.ToString());
        }

        private void Unsubscribe()
        {
            SteamFriends.OnGameLobbyJoinRequested -= OnLobbyJoin;
            SteamFriends.OnGameRichPresenceJoinRequested -= OnPresenceJoin;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Unsubscribe();

            if (SteamClient.IsValid) SteamClient.Shutdown();
        }
    }
}
