using System;

namespace ArtDock.Networking
{
    public class LaunchOptions
    {
        public string Profile { get; }
        public string PlayerName { get; }
        public ushort Port { get; }
        public bool Host { get; }
        public string JoinAddress { get; }

        public LaunchOptions(GameSettings settings)
        {
            var args = Environment.GetCommandLineArgs();
            
            Profile = Argument(args, "--profile", "default");
            PlayerName = Argument(args, "--name", $"Player {Profile}").Trim();
            Host = Array.IndexOf(args, "--host") >= 0;
            JoinAddress = Argument(args, "--join", string.Empty);

            var portSrc = Argument(args, "--port", string.Empty);
            Port = ushort.TryParse(portSrc, out var port) && port != 0 ? port : settings.Port;
        }

        private static string Argument(string[] args, string key, string fallback)
        {
            var index = Array.IndexOf(args, key);
            var isArgument = !args[index + 1].StartsWith("--", StringComparison.Ordinal);
            
            return index >= 0 && index + 1 < args.Length && isArgument ? args[index + 1] : fallback;
        }
    }
}
