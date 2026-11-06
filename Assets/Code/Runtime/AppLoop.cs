using ArtDock.Core;
using ArtDock.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace ArtDock.Runtime
{
    public class AppLoop : IStartable, ITickable
    {
        private readonly SessionService session;
        private readonly LaunchOptions options;
        private readonly GameSettings settings;
        private AsyncOperation menuLoad;
        private bool launchHandled;

        public AppLoop(SessionService session, LaunchOptions options, GameSettings settings)
        {
            this.session = session;
            this.options = options;
            this.settings = settings;
        }

        public void Start()
        {
            menuLoad = SceneManager.LoadSceneAsync(GameConstants.MenuScene, LoadSceneMode.Single);
        }

        public void Tick()
        {
            session.Tick();
            if (launchHandled || menuLoad is not { isDone: true }) return;
            launchHandled = true;

            if (session.State != SessionState.Offline || session.HasPendingJoin) return;
            session.Mode = options.Mode;
            
            if (options.Host)
                session.Host(GameConstants.GameName, settings.MaxPlayers, options.IsPrivate);
            else if (!string.IsNullOrWhiteSpace(options.JoinAddress))
                session.Join(options.JoinAddress);
        }
    }
}
