using System;
using System.Collections.Generic;
using ArtDock.Core;
using ArtDock.Networking;
using ArtDock.Networking.Platforms;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;
using Cursor = UnityEngine.Cursor;

namespace ArtDock.Runtime
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class SessionView : MonoBehaviour
    {
        private enum MenuPage : byte
        {
            Home,
            Host,
            Join
        }

        private SessionService session;
        private IGameText text;
        private GameSettings settings;
        private LanDiscovery discovery;
        private NetworkManager manager;
        private NetworkContext context;
        private UIDocument document;
        private VisualElement root;
        private PlayerListView lobbyPlayers;
        private PlayerListView scoreboardPlayers;
        private readonly List<(Button Button, Action Callback)> callbacks = new();
        private readonly List<(Button Button, Action Callback)> hostCallbacks = new();
        private readonly List<(Button Button, Action Callback)> friendCallbacks = new();
        private static readonly string[] ConnectionButtons = { "host", "join", "mode-steam", "mode-lan", "open-host", "open-join" };
        private MenuPage page;
        private bool bound;
        private bool started;
        private bool diagnostics;
        private bool showFriends;
        private bool wasPlaying;
        private bool overlayWasOpen;
        private bool cursorBeforeOverlay;
        private float friendsRefreshAt;
        private float refreshAt;

        [Inject]
        public void Construct(SessionService session, LanDiscovery discovery, NetworkManager manager,
            NetworkContext context, IGameText text, GameSettings settings)
        {
            this.session = session;
            this.discovery = discovery;
            this.manager = manager;
            this.context = context;
            this.text = text;
            this.settings = settings;

            if (started && isActiveAndEnabled) Bind();
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

            document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            if (root?.panel == null || root.Q("screen") == null) return;

            ApplyLabels();
            lobbyPlayers = new PlayerListView(root.Q("players"), session, manager, text);
            scoreboardPlayers = new PlayerListView(root.Q("scoreboard-players"), session, manager, text);
            BindButtons();
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            session.Changed += Refresh;
            discovery.Changed += RefreshHosts;
            bound = true;
            Refresh();
        }

        private void BindButtons()
        {
            BindButton("invite", ToggleFriends);
            BindButton("mode-steam", () => SelectMode(ConnectionMode.Steam));
            BindButton("mode-lan", () => SelectMode(ConnectionMode.Lan));
            BindButton("open-host", () => OpenPage(MenuPage.Host));
            BindButton("open-join", () => OpenPage(MenuPage.Join));
            BindButton("back", OnBack);
            BindButton("host", OnHost);
            BindButton("join", () => _ = session.Join(root.Q<TextField>("address").value));
            BindButton("search", ToggleSearch);
            BindButton("start", session.StartGame);
            BindButton("leave", () => session.Stop(GameConstants.Disconnected));
            BindButton("resume", () => SetCursor(false));
            BindButton("quit", Quit);
            BindButton("author", () => Application.OpenURL(settings.Launch.AuthorUrl));
        }

        private void BindButton(string name, Action callback)
        {
            var button = root.Q<Button>(name);
            button.focusable = false;
            button.tabIndex = -1;
            button.clicked += callback;
            callbacks.Add((button, callback));
        }

        private static void ClearCallbacks(List<(Button Button, Action Callback)> bindings)
        {
            foreach (var binding in bindings) binding.Button.clicked -= binding.Callback;
            bindings.Clear();
        }

        private static void BindDynamicButton(Button button, Action callback, List<(Button Button, Action Callback)> bindings)
        {
            button.clicked += callback;
            bindings.Add((button, callback));
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
        private bool Playing => session.Connected && session.World && session.World.Phase.Value == SessionPhase.Playing &&
            !session.Loading && SceneManager.GetActiveScene().name == GameConstants.ArenaScene;

        private void OpenPage(MenuPage value)
        {
            page = value;
            Refresh();
        }

        private void OnBack()
        {
            discovery.StopSearch();
            if (session.Busy) session.Stop(GameConstants.Disconnected);
            OpenPage(MenuPage.Home);
        }

        private void OnHost() => _ = session.Host(root.Q<TextField>("title").value,
            root.Q<IntegerField>("capacity").value, session.Mode == ConnectionMode.Steam && root.Q<Toggle>("private").value);

        private void SelectMode(ConnectionMode mode)
        {
            if (session.Busy) return;

            session.Mode = mode;
            root.Q<TextField>("address").value = mode == ConnectionMode.Lan
                ? $"{GameConstants.LoopbackAddress}:{settings.Port}" : string.Empty;
            discovery.StopSearch();
            friendsRefreshAt = 0;
            Refresh();
        }

        private void ToggleSearch()
        {
            if (discovery.Searching) discovery.StopSearch();
            else discovery.StartSearch();
        }

        private void ToggleFriends()
        {
            showFriends = !showFriends;
            friendsRefreshAt = 0;
            if (showFriends) session.InviteFriends();
            RefreshFriends();
        }

        private void SetCursor(bool free)
        {
            session.CursorFree = free;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Update()
        {
            if (bound && root != document.rootVisualElement) Unbind();
            if (!bound) Bind();
            if (!bound) return;

            var playing = Playing;
            if (playing && !wasPlaying) SetCursor(session.OverlayOpen);
            if (!playing && wasPlaying) SetCursor(true);
            wasPlaying = playing;
            HandleKeyboard(playing);

            if (Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + settings.Diagnostics.RefreshInterval;
            Refresh();
        }

        private void HandleKeyboard(bool playing)
        {
            if (session.OverlayOpen != overlayWasOpen)
            {
                if (session.OverlayOpen)
                {
                    cursorBeforeOverlay = session.CursorFree;
                    SetCursor(true);
                }
                else SetCursor(playing ? cursorBeforeOverlay : true);
                overlayWasOpen = session.OverlayOpen;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null || !Application.isFocused || session.OverlayOpen)
            {
                Show("players-panel", false);
                return;
            }

            if (playing && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetCursor(!session.CursorFree);
                Refresh();
            }
            if (keyboard.f3Key.wasPressedThisFrame) diagnostics = !diagnostics;

            var shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            Show("players-panel", playing && !session.CursorFree && keyboard.tabKey.isPressed && !shift);
            Show("diagnostics", diagnostics);
        }

        private void Refresh()
        {
            if (!bound) return;

            var world = session.World;
            var loading = session.Connected && (!world || session.Loading || world.Phase.Value == SessionPhase.Loading);
            RefreshPanels(world, loading);
            lobbyPlayers.Refresh(world);
            scoreboardPlayers.Refresh(world);
            RefreshHosts();
            RefreshFriends();

            context.Players.TryGetValue(manager.LocalClientId, out var local);
            root.Q<Label>("interaction").text = local ? local.InteractionHint : string.Empty;
            Show("interaction", local && !string.IsNullOrEmpty(local.InteractionHint));
            if (diagnostics) RefreshDiagnostics(local);
        }

        private void RefreshPanels(NetworkWorld world, bool loading)
        {
            var playing = Playing;
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

            root.Q<Label>("message").text = session.Message;
            Show("message", !string.IsNullOrEmpty(session.Message) && session.Message != text[TextKeys.ChooseMode]);
            root.Q<Label>("session-heading").text = text[playing ? TextKeys.Paused : TextKeys.Lobby];
            root.Q<Label>("lobby-id").text = string.IsNullOrEmpty(session.LobbyId)
                ? session.Title : text.Format(TextKeys.LobbyIdLabel, session.LobbyId);
            root.Q<Label>("loading-status").text = world && world.Phase.Value == SessionPhase.Loading
                ? text.Format(TextKeys.SceneProgress, GameConstants.ArenaScene, world.LoadedCount, world.Players.Count)
                : text[TextKeys.Synchronizing];

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

        private void RefreshHosts()
        {
            if (!bound) return;

            var list = root.Q("hosts");
            ClearCallbacks(hostCallbacks);
            list.Clear();
            root.Q<Label>("lan-status").text = discovery.Status;
            foreach (var host in discovery.Hosts)
            {
                var address = host.Address;
                var button = new Button
                {
                    text = text.Format(TextKeys.HostRow, host.Title, host.Players, host.Capacity, address),
                    enableRichText = false,
                    focusable = false,
                };
                BindDynamicButton(button, () => _ = session.Join(address), hostCallbacks);
                button.SetEnabled(!session.Busy && host.Players < host.Capacity);
                list.Add(button);
            }
        }

        private void RefreshFriends()
        {
            var joining = !session.Connected && page == MenuPage.Join && session.Mode == ConnectionMode.Steam;
            var inviting = session.CanInvite && showFriends;
            Show("steam-friends", joining);
            Show("invite-friends", inviting);
            if (!joining && !inviting || Time.unscaledTime < friendsRefreshAt) return;

            friendsRefreshAt = Time.unscaledTime + 2;
            var list = root.Q(joining ? "steam-friends-list" : "invite-friends-list");
            ClearCallbacks(friendCallbacks);
            list.Clear();
            SetText("steam-friends-heading", TextKeys.FriendsHeading);
            SetText("invite-friends-heading", session.SteamOverlayAvailable ? TextKeys.FriendsHeading : TextKeys.SteamOverlayHelp);
            foreach (var friend in session.SteamFriends)
            {
                if (joining && (string.IsNullOrEmpty(friend.Lobby) || friend.Lobby == "0")) continue;

                var button = new Button
                {
                    text = text.Format(joining ? TextKeys.JoinFriend : TextKeys.InviteFriend, friend.Name),
                    enableRichText = false,
                    focusable = false,
                };
                if (joining) BindDynamicButton(button, () => _ = session.Join(friend.Lobby), friendCallbacks);
                else BindDynamicButton(button, () => InviteFriend(friend, button), friendCallbacks);
                button.SetEnabled(!session.Busy);
                list.Add(button);
            }

            if (list.childCount == 0) list.Add(new Label(text[joining ? TextKeys.NoFriendLobbies : TextKeys.NoOnlineFriends]));
        }

        private void InviteFriend(SessionFriend friend, Button button)
        {
            var sent = session.InviteFriend(friend.Id);
            button.text = text.Format(sent ? TextKeys.InvitationSent : TextKeys.InvitationFailed, friend.Name);
            button.SetEnabled(!sent);
        }

        private void RefreshDiagnostics(NetworkPlayer local)
        {
            var item = local ? local.HeldItem : null;
            if (!item && local) item = local.SelectedItem;
            var unavailable = text[TextKeys.Unavailable];
            var holder = !item ? unavailable : item.State.Value.Holder == NetworkItem.NoHolder
                ? text[TextKeys.Unheld] : item.State.Value.Holder.ToString();
            var rtt = unavailable;
            if (session.World)
                foreach (var player in session.World.Players)
                    if (player.ClientId == manager.LocalClientId && player.RoundTripMs >= 0)
                        rtt = text.Format(TextKeys.Ping, player.RoundTripMs);

            var summary = text.Format(TextKeys.Diagnostics, session.Mode, manager.IsServer, manager.IsClient,
                manager.NetworkConfig.TickRate, rtt, manager.IsListening ? manager.SpawnManager.SpawnedObjects.Count : 0,
                unavailable, item ? item.NetworkObjectId.ToString() : text[TextKeys.NoItem],
                item ? item.OwnerClientId.ToString() : unavailable, holder,
                session.World ? session.World.Phase.Value.ToString() : session.State.ToString(),
                item ? item.State.Value.Revision.ToString() : unavailable);
            root.Q<Label>("diagnostics-text").text = summary;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Tab && evt.character != '\t') return;

            root.focusController?.IgnoreEvent(evt);
            evt.StopImmediatePropagation();
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt.direction is not (NavigationMoveEvent.Direction.Next or NavigationMoveEvent.Direction.Previous)) return;

            root.focusController?.IgnoreEvent(evt);
            evt.StopImmediatePropagation();
        }

        private void OnDisable() => Unbind();

        private void Unbind()
        {
            if (!bound) return;

            bound = false;
            session.Changed -= Refresh;
            discovery.Changed -= RefreshHosts;
            root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            ClearCallbacks(callbacks);
            ClearCallbacks(hostCallbacks);
            ClearCallbacks(friendCallbacks);
            lobbyPlayers.Dispose();
            scoreboardPlayers.Dispose();
            SetCursor(true);
        }
    }
}
