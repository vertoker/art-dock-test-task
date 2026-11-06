using ArtDock.Core;
using ArtDock.Networking;
using System.Collections.Generic;
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
        private DropdownField mode;
        private Toggle privateLobby;
        private TextField lobbyId;
        private Button invite;
        private string lanAddress;
        private string steamAddress = string.Empty;
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
            mode = root.Q<DropdownField>("connection-mode");
            privateLobby = root.Q<Toggle>("private-lobby");
            lobbyId = root.Q<TextField>("lobby-id");
            invite = root.Q<Button>("invite");
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
            lanAddress = $"{GameConstants.LoopbackAddress}:{settings.Port}";
            mode.choices = new List<string> { GameConstants.LanMode, GameConstants.SteamMode };
            ApplyMode();
            capacity.highValue = settings.MaxPlayers;
            capacity.value = settings.MaxPlayers;
            
            host.clicked += OnHost;
            join.clicked += OnJoin;
            leave.clicked += OnLeave;
            startGame.clicked += OnStartGame;
            invite.clicked += OnInvite;
            mode.RegisterValueChangedCallback(OnModeChanged);
            session.Changed += Refresh;
            
            bound = true;
            Refresh();
        }

        private void OnHost() => _ = session.Host(title.value, capacity.value, session.Mode == ConnectionMode.Steam && privateLobby.value);
        private void OnJoin() => _ = session.Join(address.value);
        private void OnLeave() => session.Stop(GameConstants.Disconnected);
        private void OnStartGame() => session.StartGame();
        private void OnInvite() => session.InviteFriends();

        private void OnModeChanged(ChangeEvent<string> change)
        {
            if (session.Mode == ConnectionMode.Steam) steamAddress = address.value;
            else lanAddress = address.value;

            session.Mode = mode.index == 1 ? ConnectionMode.Steam : ConnectionMode.Lan;
            ApplyMode();
            Refresh();
        }

        private void ApplyMode()
        {
            var steam = session.Mode == ConnectionMode.Steam;
            mode.SetValueWithoutNotify(steam ? GameConstants.SteamMode : GameConstants.LanMode);
            address.label = steam ? GameConstants.SteamLobbyId : GameConstants.HostAddress;
            address.SetValueWithoutNotify(steam ? steamAddress : lanAddress);
            privateLobby.style.display = steam ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Refresh()
        {
            var offline = session.State == SessionState.Offline;
            if (mode.index != (int)session.Mode) ApplyMode();

            mode.SetEnabled(offline);
            privateLobby.SetEnabled(offline);
            lobbyId.style.display = session.Mode == ConnectionMode.Steam ? DisplayStyle.Flex : DisplayStyle.None;
            lobbyId.SetValueWithoutNotify(session.LobbyId);
            invite.style.display = session.Mode == ConnectionMode.Steam ? DisplayStyle.Flex : DisplayStyle.None;
            invite.SetEnabled(session.CanInvite);
            
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
            invite.clicked -= OnInvite;
            mode.UnregisterValueChangedCallback(OnModeChanged);
        }
    }
}
