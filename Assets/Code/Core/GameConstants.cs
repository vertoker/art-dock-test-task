namespace ArtDock.Core
{
    public static class GameConstants
    {
        public const string GameName = "TestGame";
        
        public const string BootScene = "Boot";
        public const string MenuScene = "Menu";
        public const string ArenaScene = "Arena";
        
        public const string LoopbackAddress = "127.0.0.1";
        public const string DefaultPort = "7777";
        public const string ListenAddress = "0.0.0.0";
        public const string TransportDisconnectPrefix = "[Disconnect Event]";
        
        public const string Connecting = "Connecting...";
        public const string Hosting = "Hosting";
        public const string Connected = "Connected";
        
        public const string SessionLoading = "Session is loading";
        public const string Disconnected = "Disconnected";
        public const string SessionFull = "Session is full";
    }
}
