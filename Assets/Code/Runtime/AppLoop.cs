using System;
using ArtDock.Core;
using ArtDock.Networking;
using ArtDock.Networking.Platforms;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace ArtDock.Runtime
{
    /// <summary> Starts the application and updates the session, LAN discovery and pending statistics </summary>
    public class AppLoop : IStartable, ITickable, IDisposable
    {
        private readonly SessionService session;
        private readonly LaunchOptions options;
        private readonly GameSettings settings;
        private readonly NetworkContext context;
        private readonly LanDiscovery discovery;
        private readonly IPlayerStatistics statistics;
        
        private AsyncOperation menuLoad;
        private bool launchHandled;
        private float nextFlush;

        public AppLoop(SessionService session, LaunchOptions options, GameSettings settings, NetworkContext context,
            LanDiscovery discovery, IPlayerStatistics statistics)
        {
            this.session = session;
            this.options = options;
            this.settings = settings;
            this.context = context;
            this.discovery = discovery;
            this.statistics = statistics;
            
            context.Statistics = statistics;
            session.Changed += OnSessionChanged;
        }

        public void Start()
        {
            Application.runInBackground = settings.Launch.RunInBackground;
            Application.targetFrameRate = settings.Launch.TargetFrameRate;
            ConfigureDisplay();

            menuLoad = SceneManager.LoadSceneAsync(GameConstants.MenuScene, LoadSceneMode.Single);
        }

        private void ConfigureDisplay()
        {
            if (Application.isEditor || Application.isBatchMode || !settings.Launch.FullscreenOnStart) return;

            var arguments = Environment.GetCommandLineArgs();
            // Explicit command-line window options take priority over the fullscreen setting
            if (Array.IndexOf(arguments, "-screen-width") >= 0 || Array.IndexOf(arguments, "-screen-height") >= 0 ||
                Array.IndexOf(arguments, "-screen-fullscreen") >= 0) return;

            var resolution = Screen.currentResolution;
            Screen.SetResolution(resolution.width, resolution.height, FullScreenMode.FullScreenWindow);
        }

        public void Tick()
        {
            session.Tick();
            TickDiscovery();
            FlushStatistics();
            HandleLaunch();
        }

        private void HandleLaunch()
        {
            // Run automatic connection once after the menu loads, unless Steam already requested a join
            if (launchHandled || menuLoad is not { isDone: true }) return;
            launchHandled = true;

            if (session.State != SessionState.Offline || session.HasPendingJoin) return;
            session.Mode = options.Mode;
            
            if (options.Host)
                _ = session.Host(GameConstants.GameName, settings.MaxPlayers, options.IsPrivate);
            else if (!string.IsNullOrWhiteSpace(options.JoinAddress))
                _ = session.Join(options.JoinAddress);
        }

        private void TickDiscovery()
        {
            // Stop discovery when switching platforms or leaving the connection menu
            if (discovery.Searching && (session.Mode != ConnectionMode.Lan || session.State != SessionState.Offline))
                discovery.StopSearch();

            // Advertise only an open LAN session that is ready to accept another player
            var advertise = session.Mode == ConnectionMode.Lan && session.IsHost && !session.IsPrivate &&
                session.State == SessionState.Connected && !session.Loading && session.PlayerCount < session.Capacity;
            discovery.Tick(advertise, session.Title, options.Port, session.PlayerCount, session.Capacity);
        }

        private void FlushStatistics()
        {
            if (Time.unscaledTime < nextFlush) return;

            nextFlush = Time.unscaledTime + settings.Statistics.FlushInterval;
            statistics.Flush();
        }

        private void OnSessionChanged()
        {
            if (session.State is SessionState.Stopping or SessionState.Offline) statistics.Flush();
        }

        public void Dispose()
        {
            session.Changed -= OnSessionChanged;
            statistics.Flush();
            context.Statistics = null;
        }
    }
}
