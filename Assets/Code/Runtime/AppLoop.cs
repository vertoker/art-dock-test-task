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
            menuLoad = SceneManager.LoadSceneAsync("Menu", LoadSceneMode.Single);
        }

        public void Tick()
        {
            session.Tick();
            if (launchHandled || menuLoad == null || !menuLoad.isDone) return;
            launchHandled = true;
            
            if (options.Host)
                session.Host("ArtDock", settings.MaxPlayers, false);
            else if (!string.IsNullOrWhiteSpace(options.JoinAddress))
                session.Join(options.JoinAddress);
        }
    }
}
