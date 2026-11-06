using ArtDock.Core;
using ArtDock.Networking;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace ArtDock.Runtime
{
    [RequireComponent(typeof(UIDocument))]
    public class SessionView : MonoBehaviour
    {
        private SessionService session;
        private GameSettings settings;
        private TextField title;
        private TextField address;
        private SliderInt capacity;
        private Button host;
        private Button join;
        private Button leave;
        private Label status;
        private Button startGame;
        private VisualElement screen;
        private VisualElement connection;
        private VisualElement lobby;
        private PlayerListView players;
        private bool started;
        private bool bound;

        [Inject]
        public void Construct(SessionService session, GameSettings settings)
        {
            this.session = session;
            this.settings = settings;
        }

        private void Start()
        {
            started = true;
            Bind();
        }

        private void OnEnable()
        {
            if (started) Bind();
        }

        private void Bind()
        {
            if (bound || session == null) return;
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.Q<Label>("game-name").text = GameConstants.GameName;
            
            title = root.Q<TextField>("title");
            address = root.Q<TextField>("address");
            capacity = root.Q<SliderInt>("capacity");
            host = root.Q<Button>("host");
            join = root.Q<Button>("join");
            leave = root.Q<Button>("leave");
            status = root.Q<Label>("status");
            startGame = root.Q<Button>("start-game");
            screen = root.Q<VisualElement>("screen");
            connection = root.Q<VisualElement>("connection");
            lobby = root.Q<VisualElement>("lobby");
            players = new PlayerListView(root.Q<VisualElement>("players"));
            
            title.maxLength = settings.MaxNameLength;
            title.value = GameConstants.GameName;
            address.maxLength = 64;
            address.value = $"{GameConstants.LoopbackAddress}:{settings.Port}";
            capacity.highValue = settings.MaxPlayers;
            capacity.value = settings.MaxPlayers;
            
            host.clicked += OnHost;
            join.clicked += OnJoin;
            leave.clicked += OnLeave;
            startGame.clicked += OnStartGame;
            session.Changed += Refresh;
            
            bound = true;
            Refresh();
        }

        private void OnHost() => _ = session.Host(title.value, capacity.value, false);
        private void OnJoin() => _ = session.Join(address.value);
        private void OnLeave() => session.Stop(GameConstants.Disconnected);
        private void OnStartGame() => session.StartGame();

        private void Refresh()
        {
            var offline = session.State == SessionState.Offline;
            
            host.SetEnabled(offline);
            join.SetEnabled(offline);
            title.SetEnabled(offline);
            address.SetEnabled(offline);
            capacity.SetEnabled(offline);
            leave.SetEnabled(!offline && session.State != SessionState.Stopping);
            
            var world = session.World;
            var playing = world && world.Phase.Value == SessionPhase.Playing && !session.Loading;
            connection.style.display = offline ? DisplayStyle.Flex : DisplayStyle.None;
            lobby.style.display = world ? DisplayStyle.Flex : DisplayStyle.None;
            startGame.style.display = session.IsHost && world && world.Phase.Value == SessionPhase.Lobby
                ? DisplayStyle.Flex : DisplayStyle.None;
            
            startGame.SetEnabled(session.CanStartGame);
            screen.EnableInClassList("playing", playing);
            players.Refresh(world);
            
            if (session.Loading)
            {
                status.text = world && world.Phase.Value == SessionPhase.Loading
                    ? $"Loading {GameConstants.ArenaScene}: {world.LoadedCount}/{world.Players.Count}"
                    : "Synchronizing...";
                return;
            }
            if (playing)
            {
                status.text = "Playing";
                return;
            }
            
            status.text = session.IsHost ? $"{session.Message} · {session.PlayerCount}/{session.Capacity}" : session.Message;
        }

        private void OnDisable()
        {
            if (!bound) return;
            bound = false;
            
            session.Changed -= Refresh;
            host.clicked -= OnHost;
            join.clicked -= OnJoin;
            leave.clicked -= OnLeave;
            startGame.clicked -= OnStartGame;
        }
    }
}
