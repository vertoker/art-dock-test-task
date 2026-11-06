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

        private readonly VisualElement root;
        private readonly SessionService session;
        private readonly LanDiscovery discovery;
        private readonly IGameText text;
        private readonly GameSettings settings;

        private readonly TextField titleField;
        private readonly TextField addressField;
        private readonly IntegerField capacityField;
        private readonly Toggle privateLobbyToggle;
        private readonly Button steamModeButton;
        private readonly Button lanModeButton;
        private readonly Button startButton;
        private readonly Button[] connectionButtons;

        private readonly Label messageLabel;
        private readonly Label sessionHeading;
        private readonly Label lobbyIdLabel;
        private readonly Label loadingStatus;
        private readonly Label addressLabel;

        private readonly VisualElement menuPanel;
        private readonly VisualElement authorButton;
        private readonly VisualElement homePanel;
        private readonly VisualElement connectionPanel;
        private readonly VisualElement hostForm;
        private readonly VisualElement joinForm;
        private readonly VisualElement backButton;
        private readonly VisualElement sessionPanel;
        private readonly VisualElement loadingPanel;
        private readonly VisualElement hud;
        private readonly VisualElement diagnosticsPanel;
        private readonly VisualElement lanPanel;
        private readonly VisualElement waitingLabel;
        private readonly VisualElement resumeButton;
        private readonly VisualElement inviteButton;
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

            titleField = root.Q<TextField>("title");
            addressField = root.Q<TextField>("address");
            capacityField = root.Q<IntegerField>("capacity");
            privateLobbyToggle = root.Q<Toggle>("private");
            steamModeButton = root.Q<Button>("mode-steam");
            lanModeButton = root.Q<Button>("mode-lan");
            startButton = root.Q<Button>("start");
            connectionButtons = new[]
            {
                root.Q<Button>("host"), root.Q<Button>("join"), steamModeButton, lanModeButton,
                root.Q<Button>("open-host"), root.Q<Button>("open-join")
            };

            messageLabel = root.Q<Label>("message");
            sessionHeading = root.Q<Label>("session-heading");
            lobbyIdLabel = root.Q<Label>("lobby-id");
            loadingStatus = root.Q<Label>("loading-status");
            addressLabel = root.Q<Label>("address-label");

            menuPanel = root.Q("menu");
            authorButton = root.Q("author");
            homePanel = root.Q("home");
            connectionPanel = root.Q("connection");
            hostForm = root.Q("host-form");
            joinForm = root.Q("join-form");
            backButton = root.Q("back");
            sessionPanel = root.Q("session");
            loadingPanel = root.Q("loading");
            hud = root.Q("hud");
            diagnosticsPanel = root.Q("diagnostics");
            lanPanel = root.Q("lan");
            waitingLabel = root.Q("waiting");
            resumeButton = root.Q("resume");
            inviteButton = root.Q("invite");

            ApplyLabels();
        }

        public void OpenHost() => OpenPage(MenuPage.Host);
        public void OpenJoin() => OpenPage(MenuPage.Join);

        private void OpenPage(MenuPage value)
        {
            page = value;
            Changed?.Invoke();
        }

        public void Back()
        {
            discovery.StopSearch();
            if (session.Busy) session.Stop(GameConstants.Disconnected);
            OpenPage(MenuPage.Home);
        }

        public void Host() => session.Host(titleField.value, capacityField.value,
            session.Mode == ConnectionMode.Steam && privateLobbyToggle.value);

        public void Join() => _ = session.Join(addressField.value);

        public bool SelectMode(ConnectionMode mode)
        {
            if (session.Busy) return false;

            session.Mode = mode;
            addressField.value = mode == ConnectionMode.Lan
                ? $"{GameConstants.LoopbackAddress}:{settings.Port}" : string.Empty;
            discovery.StopSearch();
            return true;
        }

        public void Refresh(NetworkWorld world, bool loading, bool playing, bool diagnostics)
        {
            RefreshVisibility(loading, playing, diagnostics);
            RefreshMessages(world, playing);
            RefreshControls(world, playing);
        }

        private void RefreshVisibility(bool loading, bool playing, bool diagnostics)
        {
            Show(menuPanel, !session.Connected);
            Show(authorButton, !session.Connected && page == MenuPage.Home);
            Show(homePanel, page == MenuPage.Home);
            Show(connectionPanel, page != MenuPage.Home);
            Show(hostForm, page == MenuPage.Host);
            Show(joinForm, page == MenuPage.Join);
            Show(backButton, page != MenuPage.Home);

            Show(sessionPanel, session.Connected && !loading && (!playing || session.CursorFree));
            Show(loadingPanel, loading);
            Show(hud, playing && !session.CursorFree && !session.OverlayOpen);
            Show(diagnosticsPanel, diagnostics);
        }

        private void RefreshMessages(NetworkWorld world, bool playing)
        {
            messageLabel.text = session.Message;
            Show(messageLabel, !string.IsNullOrEmpty(session.Message) && session.Message != text[TextKeys.ChooseMode]);
            sessionHeading.text = text[playing ? TextKeys.Paused : TextKeys.Lobby];
            lobbyIdLabel.text = string.IsNullOrEmpty(session.LobbyId)
                ? session.Title : text.Format(TextKeys.LobbyIdLabel, session.LobbyId);
            loadingStatus.text = world && world.Phase.Value == SessionPhase.Loading
                ? text.Format(TextKeys.SceneProgress, GameConstants.ArenaScene, world.LoadedCount, world.Players.Count)
                : text[TextKeys.Synchronizing];
        }

        private void RefreshControls(NetworkWorld world, bool playing)
        {
            foreach (var button in connectionButtons)
                button.SetEnabled(!session.Busy);

            steamModeButton.EnableInClassList("selected", session.Mode == ConnectionMode.Steam);
            lanModeButton.EnableInClassList("selected", session.Mode == ConnectionMode.Lan);
            addressLabel.text = text[session.Mode == ConnectionMode.Steam ? TextKeys.SteamAddressLabel : TextKeys.HostAddressLabel];
            Show(lanPanel, session.Mode == ConnectionMode.Lan);
            Show(privateLobbyToggle, session.Mode == ConnectionMode.Steam);
            Show(startButton, session.IsHost && world && world.Phase.Value == SessionPhase.Lobby);
            startButton.SetEnabled(session.CanStartGame);

            Show(waitingLabel, !session.IsHost && !playing);
            Show(resumeButton, playing);
            Show(inviteButton, session.CanInvite);
        }

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

            titleField.label = text[TextKeys.GameNameLabel];
            titleField.value = GameConstants.GameName;
            titleField.maxLength = settings.MaxNameLength;
            capacityField.label = text[TextKeys.CapacityLabel];
            capacityField.value = settings.MaxPlayers;
            privateLobbyToggle.label = text[TextKeys.PrivateLobbyLabel];
            addressField.maxLength = 64;
            addressField.value = session.Mode == ConnectionMode.Lan
                ? $"{GameConstants.LoopbackAddress}:{settings.Port}" : string.Empty;
            root.Q<Label>("diagnostics-text").enableRichText = false;
        }

        private void SetText(string element, string key) => root.Q<TextElement>(element).text = text[key];
        private static void Show(VisualElement element, bool visible) => element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
