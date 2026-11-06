using System;
using ArtDock.Core;

namespace ArtDock.Networking
{
    /// <summary> Command-line options for player profiles and automatic session startup </summary>
    public class LaunchOptions
    {
        public string Profile { get; }
        public string PlayerName { get; }
        public ushort Port { get; }
        public bool Host { get; }
        public string JoinAddress { get; }
        public ConnectionMode Mode { get; }
        public bool IsPrivate { get; }

        public LaunchOptions(GameSettings settings)
        {
            var args = Environment.GetCommandLineArgs();
            
            Profile = Argument(args, "--profile", "default");
            PlayerName = Argument(args, "--name", $"Player {Profile}").Trim();
            Host = Array.IndexOf(args, "--host") >= 0;
            IsPrivate = Array.IndexOf(args, "--private") >= 0;
            
            var lobby = Argument(args, GameConstants.SteamConnectArgument, string.Empty);
            JoinAddress = string.IsNullOrEmpty(lobby) ? Argument(args, "--join", string.Empty) : lobby;
            
            Mode = !string.IsNullOrEmpty(lobby) || Array.IndexOf(args, "--steam") >= 0 ||
                string.Equals(Argument(args, "--mode", string.Empty), GameConstants.SteamMode, StringComparison.OrdinalIgnoreCase)
                ? ConnectionMode.Steam : ConnectionMode.Lan;
            
            var portSrc = Argument(args, "--port", string.Empty);
            Port = ushort.TryParse(portSrc, out var port) && port != 0 ? port : settings.Port;
        }

        private static string Argument(string[] args, string key, string fallback)
        {
            var index = Array.IndexOf(args, key);
            if (index < 0 || index + 1 >= args.Length) return fallback;

            var value = args[index + 1];
            var isArgument = !value.StartsWith("--", StringComparison.Ordinal) && !value.StartsWith("+", StringComparison.Ordinal);
            
            return isArgument ? value : fallback;
        }
    }
}
