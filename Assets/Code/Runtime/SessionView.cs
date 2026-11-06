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
            
            title = root.Q<TextField>("title");
            address = root.Q<TextField>("address");
            capacity = root.Q<SliderInt>("capacity");
            host = root.Q<Button>("host");
            join = root.Q<Button>("join");
            leave = root.Q<Button>("leave");
            status = root.Q<Label>("status");
            
            title.maxLength = settings.MaxNameLength;
            address.maxLength = 64;
            address.value = $"127.0.0.1:{settings.Port}";
            capacity.highValue = settings.MaxPlayers;
            capacity.value = settings.MaxPlayers;
            
            host.clicked += OnHost;
            join.clicked += OnJoin;
            leave.clicked += OnLeave;
            session.Changed += Refresh;
            
            bound = true;
            Refresh();
        }

        private void OnHost() => _ = session.Host(title.value, capacity.value, false);
        private void OnJoin() => _ = session.Join(address.value);
        private void OnLeave() => session.Stop("Disconnected.");

        private void Refresh()
        {
            var offline = session.State == SessionState.Offline;
            
            host.SetEnabled(offline);
            join.SetEnabled(offline);
            title.SetEnabled(offline);
            address.SetEnabled(offline);
            capacity.SetEnabled(offline);
            leave.SetEnabled(!offline && session.State != SessionState.Stopping);
            
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
        }
    }
}
