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
    /// <summary> Connects the interface document to the session and handles pause, overlay and gameplay panels </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SessionView : MonoBehaviour
    {
        private SessionService session;
        private IGameText text;
        private GameSettings settings;
        private LanDiscovery discovery;
        private NetworkManager manager;
        private NetworkContext context;
        private UIDocument document;

        private VisualElement root;
        private VisualElement playersPanel;
        private VisualElement diagnosticsPanel;
        private Label interactionLabel;
        private SessionMenuView menu;
        private SessionConnectionsView connections;
        private SessionDiagnosticsView diagnosticsView;
        private PlayerListView lobbyPlayers;
        private PlayerListView scoreboardPlayers;
        private readonly List<(Button Button, Action Callback)> callbacks = new();

        private bool bound;
        private bool started;
        private bool diagnostics;
        private bool wasPlaying;
        private bool overlayWasOpen;
        private bool cursorBeforeOverlay;
        private float refreshAt;

        private bool Playing => session.Connected && session.World && session.World.Phase.Value == SessionPhase.Playing &&
            !session.Loading && SceneManager.GetActiveScene().name == GameConstants.ArenaScene;

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

            playersPanel = root.Q("players-panel");
            diagnosticsPanel = root.Q("diagnostics");
            interactionLabel = root.Q<Label>("interaction");

            menu = new SessionMenuView(root, session, discovery, text, settings, menu);
            connections = new SessionConnectionsView(root, session, discovery, text, connections);
            diagnosticsView = new SessionDiagnosticsView(root, session, manager, text);
            lobbyPlayers = new PlayerListView(root.Q("players"), session, manager, text);
            scoreboardPlayers = new PlayerListView(root.Q("scoreboard-players"), session, manager, text);
            BindButtons();

            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            menu.Changed += Refresh;
            session.Changed += Refresh;
            discovery.Changed += connections.RefreshHosts;
            bound = true;
            Refresh();
        }

        private void BindButtons()
        {
            BindButton("invite", () => connections.ToggleFriends(menu.JoiningSteam));
            BindButton("mode-steam", () => SelectMode(ConnectionMode.Steam));
            BindButton("mode-lan", () => SelectMode(ConnectionMode.Lan));
            BindButton("open-host", menu.OpenHost);
            BindButton("open-join", menu.OpenJoin);

            BindButton("back", menu.Back);
            BindButton("host", menu.Host);
            BindButton("join", menu.Join);
            BindButton("search", connections.ToggleSearch);
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

        private void SelectMode(ConnectionMode mode)
        {
            if (!menu.SelectMode(mode)) return;

            connections.ResetFriends();
            Refresh();
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
            if (!bound)
            {
                Bind();
                return;
            }

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
            UpdateOverlay(playing);

            var keyboard = Keyboard.current;
            if (keyboard == null || !Application.isFocused || session.OverlayOpen)
            {
                Show(playersPanel, false);
                return;
            }

            HandlePause(keyboard, playing);
            if (keyboard.f3Key.wasPressedThisFrame) diagnostics = !diagnostics;

            var shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            Show(playersPanel, playing && !session.CursorFree && keyboard.tabKey.isPressed && !shift);
            Show(diagnosticsPanel, diagnostics);
        }

        private void UpdateOverlay(bool playing)
        {
            if (session.OverlayOpen == overlayWasOpen) return;

            if (session.OverlayOpen)
            {
                cursorBeforeOverlay = session.CursorFree;
                SetCursor(true);
            }
            else SetCursor(!playing || cursorBeforeOverlay);

            overlayWasOpen = session.OverlayOpen;
        }

        private void HandlePause(Keyboard keyboard, bool playing)
        {
            if (!playing || !keyboard.escapeKey.wasPressedThisFrame) return;

            SetCursor(!session.CursorFree);
            Refresh();
        }

        private void Refresh()
        {
            if (!bound) return;

            var world = session.World;
            var loading = session.Connected && (!world || session.Loading || world.Phase.Value == SessionPhase.Loading);
            menu.Refresh(world, loading, Playing, diagnostics);
            lobbyPlayers.Refresh(world);
            scoreboardPlayers.Refresh(world);
            connections.RefreshHosts();
            connections.RefreshFriends(menu.JoiningSteam);

            context.Players.TryGetValue(manager.LocalClientId, out var local);
            interactionLabel.text = local ? local.InteractionHint : string.Empty;
            Show(interactionLabel, local && !string.IsNullOrEmpty(local.InteractionHint));
            if (diagnostics) diagnosticsView.Refresh(local);
        }

        private static void Show(VisualElement element, bool visible) => element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary> Reserves Tab for the player list and Steam by suppressing UI Toolkit focus navigation </summary>
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
            menu.Changed -= Refresh;
            session.Changed -= Refresh;
            discovery.Changed -= connections.RefreshHosts;
            root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);

            foreach (var binding in callbacks) binding.Button.clicked -= binding.Callback;
            callbacks.Clear();
            connections.Dispose();
            lobbyPlayers.Dispose();
            scoreboardPlayers.Dispose();
            SetCursor(true);
        }
    }
}
