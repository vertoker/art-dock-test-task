using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Threading;
using System;
using ArtDock.Core;
using Netcode.Transports.Facepunch;
using Steamworks.Data;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking.Platforms
{
    public sealed class SteamPlatform : IConnectionPlatform, ILobbyPlatform, IOverlayPlatform, IFriendsPlatform
    {
        private readonly NetworkManager manager;
        private readonly GameSettings settings;
        private readonly SteamRuntime runtime;
        private readonly SteamLobby lobby;
        private readonly SteamLobbyOperation operation = new();
        private FacepunchTransport transport;
        private float nextInitialization;
        private bool disposed;

        public ConnectionMode Mode => ConnectionMode.Steam;
        public string PlayerId => runtime.PlayerId;
        public string PlayerName => runtime.PlayerName;
        public string LobbyId => lobby.Id;
        public string Error { get; private set; } = string.Empty;
        public bool CanInvite => !disposed && lobby.CanInvite;
        public bool OverlayOpen => !disposed && runtime.IsValid && runtime.OverlayOpen;
        public bool OverlayAvailable => !disposed && runtime.IsValid && SteamUtils.IsOverlayEnabled;
        public event Action<string> JoinRequested;
        public event Action<ConnectionMode, string> ConnectionLost;

        public SessionFriend[] GetFriends()
        {
            if (disposed || !runtime.IsValid) return Array.Empty<SessionFriend>();

            var friends = new List<SessionFriend>();
            foreach (var friend in SteamFriends.GetFriends())
            {
                if (!friend.IsOnline) continue;

                var address = friend.IsPlayingThisGame ? friend.GameInfo?.Lobby?.Id.ToString() ?? string.Empty : string.Empty;
                friends.Add(new SessionFriend(friend.Id.Value, friend.Name, address));
            }

            friends.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            return friends.ToArray();
        }

        public bool InviteFriend(ulong id)
        {
            if (!CanInvite) return false;

            foreach (var friend in SteamFriends.GetFriends())
                if (friend.Id.Value == id) return lobby.InviteFriend(id);

            return false;
        }

        public SteamPlatform(NetworkManager manager, GameSettings settings)
        {
            this.manager = manager;
            this.settings = settings;
            runtime = new SteamRuntime(settings);
            lobby = new SteamLobby(settings);
            runtime.JoinRequested += OnJoinRequested;
        }

        public async Task<bool> PrepareHost(string title, int capacity, bool isPrivate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Prepare()) return false;

            try
            {
                var created = await operation.Run(() => SteamMatchmaking.CreateLobbyAsync(capacity), settings.ConnectionTimeout, cancellationToken);
                if (!created.HasValue) return FailOperation(GameErrors.SteamLobbyCreationFailed);

                if (!lobby.HostLobby(created.Value, title, capacity, isPrivate)) return Fail(GameErrors.SteamLobbyConfigurationFailed);
                SelectTransport(0);
                return true;
            }
            catch (Exception exception)
            {
                return Fail(string.Format(GameErrors.SteamUnavailable, exception.Message));
            }
        }

        public async Task<bool> PrepareJoin(string address, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SteamLobby.TryParseId(address, out var id)) return Fail(GameErrors.SteamLobbyIdRequired);
            if (!Prepare()) return false;

            try
            {
                var joined = await operation.Run(() => SteamMatchmaking.JoinLobbyAsync(id), settings.ConnectionTimeout, cancellationToken);
                if (!joined.HasValue) return FailOperation(GameErrors.SteamLobbyUnavailable);

                if (!lobby.JoinLobby(joined.Value)) return Fail(GameErrors.SteamLobbyIncompatible);
                SelectTransport(lobby.Host);
                return true;
            }
            catch (Exception exception)
            {
                return Fail(string.Format(GameErrors.SteamUnavailable, exception.Message));
            }
        }

        private bool Prepare()
        {
            Error = string.Empty;
            if (disposed) return false;
            if (operation.Busy)
            {
                Error = GameErrors.SteamLobbyPending;
                return false;
            }

            if (runtime.EnsureInitialized()) return true;
            Error = runtime.Error;
            return false;
        }

        private void SelectTransport(ulong host)
        {
            if (transport)
            {
                transport.enabled = false;
                UnityEngine.Object.Destroy(transport);
            }

            transport = manager.gameObject.AddComponent<FacepunchTransport>();
            transport.targetSteamId = host;
            manager.NetworkConfig.NetworkTransport = transport;
        }

        private bool FailOperation(string fallback) => Fail(string.IsNullOrEmpty(operation.Error) ? fallback : operation.Error);

        private bool Fail(string error)
        {
            Error = error;
            Leave();
            return false;
        }

        public void Tick()
        {
            if (disposed) return;

            if (lobby.Active && !runtime.IsValid)
            {
                LoseConnection(GameErrors.SteamConnectionLost);
                return;
            }

            if (!runtime.IsValid && !manager.IsListening && !manager.ShutdownInProgress && !operation.Busy && Time.unscaledTime >= nextInitialization)
            {
                nextInitialization = Time.unscaledTime + settings.Steam.RetryInterval;
                runtime.EnsureInitialized();
            }

            if (runtime.IsValid && (!manager.IsListening || manager.NetworkConfig.NetworkTransport != transport) && !runtime.RunCallbacks())
            {
                if (lobby.Active) LoseConnection(GameErrors.SteamConnectionLost);
                return;
            }

            if (!lobby.HostAvailable) LoseConnection(GameErrors.HostConnectionLost);
        }

        private void LoseConnection(string error)
        {
            Leave();
            ConnectionLost?.Invoke(Mode, error);
        }

        private void OnJoinRequested(string address) => JoinRequested?.Invoke(address);

        public void UpdatePresence(bool inGame, int players, int capacity, bool acceptingPlayers) =>
            lobby.UpdatePresence(inGame, players, capacity, acceptingPlayers);

        public void InviteFriends() => lobby.InviteFriends();
        public void CloseLobby() => lobby.Close();

        public void Leave()
        {
            operation.Cancel();
            lobby.Leave();
            if (transport) transport.enabled = false;

            nextInitialization = 0;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Leave();
            runtime.JoinRequested -= OnJoinRequested;
            runtime.Dispose();
        }

        private sealed class SteamRuntime : IDisposable
        {
            private readonly GameSettings settings;
            private bool disposed;

            public event Action<string> JoinRequested;
            public string Error { get; private set; } = string.Empty;
            public bool IsValid => SteamClient.IsValid;
            public bool OverlayOpen { get; private set; }
            public string PlayerId => IsValid ? SteamClient.SteamId.ToString() : string.Empty;
            public string PlayerName
            {
                get
                {
                    var name = IsValid ? SteamClient.Name : GameConstants.SteamPlayer;
                    if (string.IsNullOrWhiteSpace(name)) name = GameConstants.SteamPlayer;

                    var length = Math.Min(name.Length, settings.MaxNameLength);
                    if (length < name.Length && length > 0 && char.IsHighSurrogate(name[length - 1])) length--;

                    return name[..length];
                }
            }

            public SteamRuntime(GameSettings settings) => this.settings = settings;

            public bool EnsureInitialized()
            {
                if (disposed) return false;

                try
                {
                    if (!SteamClient.IsValid)
                    {
                        OverlayOpen = false;
                        SteamClient.Init(settings.Steam.AppId, false);
                    }
                    if (!SteamClient.IsValid) throw new InvalidOperationException(GameErrors.SteamInitializationFailed);

                    SteamNetworkingUtils.InitRelayNetworkAccess();
                    SteamNetworkingUtils.ConnectionTimeout = (int)(settings.ConnectionTimeout * 1000);
                    SteamNetworkingUtils.Timeout = (int)(settings.ConnectionTimeout * 1000);

                    Unsubscribe();
                    SteamFriends.OnGameLobbyJoinRequested += OnLobbyJoin;
                    SteamFriends.OnGameRichPresenceJoinRequested += OnPresenceJoin;
                    SteamFriends.OnGameOverlayActivated += OnOverlayActivated;
                    Error = string.Empty;
                    return true;
                }
                catch (Exception exception)
                {
                    Error = string.Format(GameErrors.SteamUnavailable, exception.Message);
                    return false;
                }
            }

            public bool RunCallbacks()
            {
                if (disposed || !IsValid) return false;

                try
                {
                    SteamClient.RunCallbacks();
                    return true;
                }
                catch (Exception exception)
                {
                    Error = string.Format(GameErrors.SteamUnavailable, exception.Message);
                    return false;
                }
            }

            private void OnLobbyJoin(Lobby lobby, SteamId friend)
            {
                if (lobby.Id.Value != 0) JoinRequested?.Invoke(lobby.Id.ToString());
            }

            private void OnPresenceJoin(Friend friend, string command)
            {
                var prefix = GameConstants.SteamConnectArgument + " ";
                if (string.IsNullOrEmpty(command) || !command.StartsWith(prefix, StringComparison.Ordinal)) return;

                if (SteamLobby.TryParseId(command[prefix.Length..], out var id)) JoinRequested?.Invoke(id.ToString());
            }

            private void Unsubscribe()
            {
                SteamFriends.OnGameLobbyJoinRequested -= OnLobbyJoin;
                SteamFriends.OnGameRichPresenceJoinRequested -= OnPresenceJoin;
                SteamFriends.OnGameOverlayActivated -= OnOverlayActivated;
            }

            private void OnOverlayActivated(bool active) => OverlayOpen = active;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                Unsubscribe();

                if (SteamClient.IsValid) SteamClient.Shutdown();
            }
        }

        private sealed class SteamLobby
        {
            private const string GameKey = "game";
            private const string ProtocolKey = "protocol";
            private const string HostKey = "host";
            private const string NameKey = "name";
            private const string PlayersKey = "players";
            private const string CapacityKey = "capacity";
            private const string PhaseKey = "phase";
            private const string ConnectKey = "connect";
            private const string StatusKey = "status";
            private const string LobbyPhase = "lobby";
            private const string PlayingPhase = "game";
            
            private const string Marker = GameConstants.GameName + ".session";
            private readonly GameSettings settings;
            private Lobby? lobby;
            private bool ownsLobby;
            private ulong host;
            private float nextPresence;
            private bool? lastInGame;
            private bool lastAccepting;
            private int lastPlayers;
            private int lastCapacity;

            public string Id => lobby?.Id.ToString() ?? string.Empty;
            public bool Active => lobby.HasValue;
            public bool CanInvite => Active && SteamClient.IsValid;
            public ulong Host => host;
            public bool HostAvailable => !Active || ownsLobby || SteamClient.IsValid && lobby.Value.Owner.Id.Value == host &&
                TryParseId(lobby.Value.GetData(HostKey), out var current) && current == host;

            public SteamLobby(GameSettings settings) => this.settings = settings;

            public bool HostLobby(Lobby created, string title, int capacity, bool isPrivate)
            {
                lobby = created;
                ownsLobby = true;
                host = SteamClient.SteamId.Value;
                nextPresence = 0;
                lastInGame = null;

                var configured = created.SetData(GameKey, Marker) && created.SetData(ProtocolKey, settings.ProtocolVersion) &&
                    created.SetData(HostKey, host.ToString(CultureInfo.InvariantCulture)) && created.SetData(NameKey, title) &&
                    created.SetData(CapacityKey, capacity.ToString(CultureInfo.InvariantCulture));
                var visibility = isPrivate ? created.SetPrivate() : created.SetFriendsOnly();

                return configured && visibility && created.SetJoinable(false);
            }

            public bool JoinLobby(Lobby joined)
            {
                lobby = joined;
                ownsLobby = false;
                nextPresence = 0;
                lastInGame = null;

                if (joined.GetData(GameKey) != Marker || joined.GetData(ProtocolKey) != settings.ProtocolVersion ||
                    !TryParseId(joined.GetData(HostKey), out host) || joined.Owner.Id.Value != host || host == SteamClient.SteamId.Value)
                    return false;

                return true;
            }

            public void UpdatePresence(bool inGame, int players, int capacity, bool accepting)
            {
                if (!Active || !SteamClient.IsValid) return;
                if (Time.unscaledTime < nextPresence && lastInGame == inGame && lastAccepting == accepting &&
                    lastPlayers == players && lastCapacity == capacity) return;

                nextPresence = Time.unscaledTime + settings.Steam.PresenceInterval;
                lastInGame = inGame;
                lastAccepting = accepting;
                lastPlayers = players;
                lastCapacity = capacity;

                if (ownsLobby)
                {
                    var current = lobby.Value;
                    current.SetData(PlayersKey, players.ToString(CultureInfo.InvariantCulture));
                    current.SetData(CapacityKey, capacity.ToString(CultureInfo.InvariantCulture));
                    current.SetData(PhaseKey, inGame ? PlayingPhase : LobbyPhase);
                    current.SetJoinable(accepting);
                }

                SteamFriends.SetRichPresence(StatusKey, inGame ? string.Format(GameConstants.SteamGameStatus, players, capacity) : GameConstants.SteamLobbyStatus);
                SteamFriends.SetRichPresence(ConnectKey, accepting ? GameConstants.SteamConnectArgument + " " + Id : null);
            }

            public void InviteFriends()
            {
                if (CanInvite && SteamUtils.IsOverlayEnabled) SteamFriends.OpenGameInviteOverlay(lobby.Value.Id);
            }

            public bool InviteFriend(ulong id) => CanInvite && lobby.Value.InviteFriend(id);

            public void Close()
            {
                if (!Active || !SteamClient.IsValid) return;

                if (ownsLobby)
                {
                    var current = lobby.Value;
                    current.SetJoinable(false);
                    current.SetData(HostKey, string.Empty);
                }

                SteamFriends.ClearRichPresence();
            }

            public void Leave()
            {
                Close();
                if (Active && SteamClient.IsValid) lobby.Value.Leave();

                lobby = null;
                ownsLobby = false;
                host = 0;
                lastInGame = null;
            }

            public static bool TryParseId(string value, out ulong id) =>
                ulong.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0;
        }

        private sealed class SteamLobbyOperation
        {
            private Task cleanup = Task.CompletedTask;
            private CancellationTokenSource cancellation;
            private int generation;
            private bool running;

            public bool Busy => running || !cleanup.IsCompleted;
            public string Error { get; private set; } = string.Empty;

            public async Task<Lobby?> Run(Func<Task<Lobby?>> begin, float timeout, CancellationToken token)
            {
                Error = string.Empty;
                if (Busy)
                {
                    Error = GameErrors.SteamLobbyPending;
                    return null;
                }

                token.ThrowIfCancellationRequested();
                running = true;
                var version = ++generation;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                cancellation = deadline;
                deadline.CancelAfter(TimeSpan.FromSeconds(timeout));

                try
                {
                    var pending = begin();
                    var canceled = Task.Delay(Timeout.Infinite, deadline.Token);
                    var completed = await Task.WhenAny(pending, canceled);

                    if (completed != pending || deadline.IsCancellationRequested || version != generation)
                    {
                        cleanup = CleanupLate(pending);
                        if (!token.IsCancellationRequested && version == generation) Error = GameErrors.SteamLobbyTimedOut;
                        return null;
                    }

                    return await pending;
                }
                finally
                {
                    cancellation = null;
                    deadline.Cancel();
                    running = false;
                }
            }

            public void Cancel()
            {
                generation++;
                cancellation?.Cancel();
            }

            private static async Task CleanupLate(Task<Lobby?> pending)
            {
                try
                {
                    var lobby = await pending;
                    if (lobby.HasValue && SteamClient.IsValid) lobby.Value.Leave();
                }
                catch (Exception)
                {
                }
            }
        }
    }

}
