namespace ArtDock.Core
{
    /// <summary> Error messages for connections, scene loading and platform operations </summary>
    public static class GameErrors
    {
        public const string MissingReference = "\"{0}\" is null";
        public const string SceneLoadFailed = "Unable to load " + GameConstants.ArenaScene + ": {0}";
        public const string ConnectionModeUnavailable = "Connection mode {0} is not available";
        
        public const string SceneLoadTimedOut = "Scene loading timed out";
        public const string ConnectionTimedOut = "Connection timed out";
        public const string SessionEnded = "Network session ended";
        public const string TransportFailed = "Network transport failed";
        public const string ConnectionRejected = "Connection rejected";
        public const string VersionRequired = "Version is required";
        public const string PayloadTooLarge = "Connection data is too large";
        public const string InvalidPayload = "Invalid connection data";
        public const string InvalidIdentity = "Invalid player identity";
        
        public const string TransportNotAssigned = "UnityTransport is not assigned to NetworkManager";
        public const string InvalidPlayerIdentityOrName = "Player identity or name is invalid";
        public const string VersionMismatch = "Game version does not match the host";
        public const string DuplicateIdentity = "This player profile is already connected";
        public const string WorldPrefabNotAssigned = "World prefab is not assigned";
        public const string HostSceneLoadFailed = "Host could not load " + GameConstants.ArenaScene;
        public const string TransportStartFailed = "Unable to start the network transport";
        public const string ConnectionTimedOutOrRejected = "Connection timed out or was rejected";
        public const string HostConnectionLost = "Connection to the host was lost";
        public const string InvalidAddress = "Enter an IPv4 address and port, for example "
                                             + GameConstants.LoopbackAddress + ":" + GameConstants.DefaultPort;

        public const string SteamUnavailable = "Steam is unavailable: {0}";
        public const string SteamInitializationFailed = "Start Steam and sign in before connecting";
        public const string SteamLobbyIdRequired = "Enter a valid Steam lobby ID";
        public const string SteamLobbyCreationFailed = "Unable to create a Steam lobby";
        public const string SteamLobbyUnavailable = "Unable to join the Steam lobby";
        public const string SteamLobbyIncompatible = "The Steam lobby belongs to another game or version, or its host has left";
        public const string SteamLobbyConfigurationFailed = "Unable to configure the Steam lobby";
        public const string SteamLobbyTimedOut = "Steam lobby request timed out";
        public const string SteamLobbyPending = "The previous Steam lobby request is still finishing";
        public const string SteamConnectionLost = "Connection to Steam was lost";
        public const string KickedByHost = "You were removed by the host";
        public const string DiscoveryUnavailable = "LAN search is unavailable: {0}. You can still join by IP.";
        public const string DiscoveryBroadcastBlocked = "LAN broadcast is unavailable. Players can still join by IP.";
    }
}
