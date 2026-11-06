using System;
using ArtDock.Core;
using ArtDock.Networking;
using ArtDock.Networking.Platforms;
using UnityEngine.UIElements;

namespace ArtDock.Runtime
{
    /// <summary> Fills connection forms and updates panels from session state </summary>
    internal class SessionMenuView
    {
        /// <summary> The current connection menu page </summary>
        private enum MenuPage : byte
        {
            Home = 0,
            Host = 1,
            Join = 2,
        }

        private static readonly string[] ConnectionButtons = { "host", "join", "mode-steam", "mode-lan", "open-host", "open-join" };
        private readonly VisualElement root;
        private readonly SessionService session;
        private readonly LanDiscovery discovery;
        private readonly IGameText text;
        private readonly GameSettings settings;
        private MenuPage page;

        public event Action Changed;
        public bool JoiningSteam => !session.Connected && page == MenuPage.Join && session.Mode == ConnectionMode.Steam;

        public SessionMenuView(VisualElement root, SessionService session, LanDiscovery discovery, IGameText text,
            GameSettings settings, SessionMenuView previous)
        {
            this.root = root;
            this.session = session;
            this.discovery = discovery;
            this.text = text;
            this.settings = settings;
            page = previous?.page ?? MenuPage.Home;

            ApplyLabels();
        }

        public void OpenHost() => OpenPage(MenuPage.Host);
        public void OpenJoin() => OpenPage(MenuPage.Join);

        /// <summary> Opens a menu page and requests an interface refresh </summary>
        private void OpenPage(MenuPage value)
        {
            page = value;
            Changed?.Invoke();
        }

        /// <summary> Stops discovery or a connection attempt and returns to the home page </summary>
        public void Back()
        {
            discovery.StopSearch();
            if (session.Busy) session.Stop(GameConstants.Disconnected);
            OpenPage(MenuPage.Home);
        }

        /// <summary> Creates a session using the title, capacity and visibility entered in the form </summary>
        public void Host() => session.Host(root.Q<TextField>("title").value,
            root.Q<IntegerField>("capacity").value,
            session.Mode == ConnectionMode.Steam && root.Q<Toggle>("private").value);

        /// <summary> Changes the platform and default address when no operation is pending </summary>
        public bool SelectMode(ConnectionMode mode)
        {
            if (session.Busy) return false;

            session.Mode = mode;
            root.Q<TextField>("address").value = mode == ConnectionMode.Lan
                ? $"{GameConstants.LoopbackAddress}:{settings.Port}" : string.Empty;
            discovery.StopSearch();
            return true;
        }

        /// <summary> Updates panel visibility, messages and connection controls </summary>
        public void Refresh(NetworkWorld world, bool loading, bool playing, bool diagnostics)
        {
            RefreshVisibility(loading, playing, diagnostics);
            RefreshMessages(world, playing);
            RefreshControls(world, playing);
        }

        /// <summary> Selects menu, session and HUD panels from the game phase and cursor state </summary>
        private void RefreshVisibility(bool loading, bool playing, bool diagnostics)
        {
            Show("menu", !session.Connected);
            Show("author", !session.Connected && page == MenuPage.Home);
            Show("home", page == MenuPage.Home);
            Show("connection", page != MenuPage.Home);
            Show("host-form", page == MenuPage.Host);
            Show("join-form", page == MenuPage.Join);
            Show("back", page != MenuPage.Home);

            Show("session", session.Connected && !loading && (!playing || session.CursorFree));
            Show("loading", loading);
            Show("hud", playing && !session.CursorFree && !session.OverlayOpen);
            Show("diagnostics", diagnostics);
        }

        /// <summary> Updates connection messages, session headings and arena loading progress </summary>
        private void RefreshMessages(NetworkWorld world, bool playing)
        {
            root.Q<Label>("message").text = session.Message;
            Show("message", !string.IsNullOrEmpty(session.Message) && session.Message != text[TextKeys.ChooseMode]);
            root.Q<Label>("session-heading").text = text[playing ? TextKeys.Paused : TextKeys.Lobby];
            root.Q<Label>("lobby-id").text = string.IsNullOrEmpty(session.LobbyId)
                ? session.Title : text.Format(TextKeys.LobbyIdLabel, session.LobbyId);
            root.Q<Label>("loading-status").text = world && world.Phase.Value == SessionPhase.Loading
                ? text.Format(TextKeys.SceneProgress, GameConstants.ArenaScene, world.LoadedCount, world.Players.Count)
                : text[TextKeys.Synchronizing];
        }

        /// <summary> Configures menu commands for the selected platform and player role </summary>
        private void RefreshControls(NetworkWorld world, bool playing)
        {
            foreach (var name in ConnectionButtons)
                root.Q<Button>(name).SetEnabled(!session.Busy);

            root.Q("mode-steam").EnableInClassList("selected", session.Mode == ConnectionMode.Steam);
            root.Q("mode-lan").EnableInClassList("selected", session.Mode == ConnectionMode.Lan);
            root.Q<Label>("address-label").text = text[session.Mode == ConnectionMode.Steam ? TextKeys.SteamAddressLabel : TextKeys.HostAddressLabel];
            Show("lan", session.Mode == ConnectionMode.Lan);
            Show("private", session.Mode == ConnectionMode.Steam);
            Show("start", session.IsHost && world && world.Phase.Value == SessionPhase.Lobby);
            root.Q<Button>("start").SetEnabled(session.CanStartGame);

            Show("waiting", !session.IsHost && !playing);
            Show("resume", playing);
            Show("invite", session.CanInvite);
        }

        /// <summary> Fills labels and initial field values from game text and settings </summary>
        private void ApplyLabels()
        {
            SetText("game-title", TextKeys.GameTitle);
            SetText("open-host", TextKeys.CreateGame);
            SetText("open-join", TextKeys.JoinGame);
            SetText("quit", TextKeys.Quit);
            SetText("mode-steam", TextKeys.Steam);
            SetText("mode-lan", TextKeys.Lan);
            SetText("host", TextKeys.Create);
            SetText("join", TextKeys.Join);
            SetText("search", TextKeys.FindLocalGames);
            SetText("back", TextKeys.Back);
            SetText("start", TextKeys.StartGame);
            SetText("waiting", TextKeys.WaitingForHost);
            SetText("resume", TextKeys.Resume);
            SetText("leave", TextKeys.LeaveGame);
            SetText("invite", TextKeys.InviteFriends);
            SetText("author", TextKeys.Author);
            SetText("players-heading", TextKeys.PlayersHeading);
            SetText("loading-heading", TextKeys.LoadingHeading);
            SetText("controls", TextKeys.ControlsHint);
            SetText("network-heading", TextKeys.NetworkHeading);
            SetText("crosshair", TextKeys.Crosshair);

            var title = root.Q<TextField>("title");
            title.label = text[TextKeys.GameNameLabel];
            title.value = GameConstants.GameName;
            title.maxLength = settings.MaxNameLength;
            root.Q<IntegerField>("capacity").label = text[TextKeys.CapacityLabel];
            root.Q<IntegerField>("capacity").value = settings.MaxPlayers;
            root.Q<Toggle>("private").label = text[TextKeys.PrivateLobbyLabel];
            root.Q<TextField>("address").maxLength = 64;
            root.Q<TextField>("address").value = session.Mode == ConnectionMode.Lan
                ? $"{GameConstants.LoopbackAddress}:{settings.Port}" : string.Empty;
            root.Q<Label>("diagnostics-text").enableRichText = false;
        }

        private void SetText(string element, string key) => root.Q<TextElement>(element).text = text[key];
        private void Show(string name, bool visible) => root.Q(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
