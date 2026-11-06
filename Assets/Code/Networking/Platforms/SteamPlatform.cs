using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using ArtDock.Core;
using Netcode.Transports.Facepunch;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking.Platforms
{
    /// <summary> Prepares Steam sessions, selects the transport and forwards invitation and disconnect events </summary>
    public class SteamPlatform : IConnectionPlatform, ILobbyPlatform, IOverlayPlatform, IFriendsPlatform
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

        /// <summary> Returns online Steam friends sorted by name with their available lobby addresses </summary>
        public SessionFriend[] GetFriends()
        {
            if (disposed || !runtime.IsValid) return Array.Empty<SessionFriend>();

            // List online friends and keep lobby addresses for friends playing this game
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

        /// <summary> Invites the given Steam friend when the active lobby allows invitations </summary>
        public bool InviteFriend(ulong id)
        {
            if (!CanInvite) return false;

            foreach (var friend in SteamFriends.GetFriends())
            {
                if (friend.Id.Value == id)
                    return lobby.InviteFriend(id);
            }

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

        /// <summary> Creates and configures a Steam lobby before selecting the host transport </summary>
        public async Task<bool> PrepareHost(string title, int capacity, bool isPrivate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Prepare()) return false;

            try
            {
                // Wait for lobby creation within the connection timeout
                var created = await operation.Run(() => SteamMatchmaking.CreateLobbyAsync(capacity), settings.ConnectionTimeout, cancellationToken);
                if (!created.HasValue) return FailOperation(GameErrors.SteamLobbyCreationFailed);

                // Configure lobby access and metadata before selecting the host transport
                if (!lobby.HostLobby(created.Value, title, capacity, isPrivate))
                    return Fail(GameErrors.SteamLobbyConfigurationFailed);
                
                SelectTransport(0);
                return true;
            }
            catch (Exception exception)
            {
                return Fail(string.Format(GameErrors.SteamUnavailable, exception.Message));
            }
        }

        /// <summary> Joins a compatible Steam lobby and selects its original host as the transport target </summary>
        public async Task<bool> PrepareJoin(string address, CancellationToken cancellationToken)
        {
            // Validate the lobby ID before making a Steam request
            cancellationToken.ThrowIfCancellationRequested();
            if (!SteamLobby.TryParseId(address, out var id)) return Fail(GameErrors.SteamLobbyIdRequired);
            if (!Prepare()) return false;

            try
            {
                // Handle cancellation and leave lobbies returned after the request deadline
                var joined = await operation.Run(() => SteamMatchmaking.JoinLobbyAsync(id), settings.ConnectionTimeout, cancellationToken);
                if (!joined.HasValue) return FailOperation(GameErrors.SteamLobbyUnavailable);

                // Check the game, protocol and original lobby owner
                if (!lobby.JoinLobby(joined.Value))
                    return Fail(GameErrors.SteamLobbyIncompatible);
                
                SelectTransport(lobby.Host);
                return true;
            }
            catch (Exception exception)
            {
                return Fail(string.Format(GameErrors.SteamUnavailable, exception.Message));
            }
        }

        /// <summary> Checks SDK availability and rejects overlapping native lobby requests </summary>
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

        /// <summary> Creates a fresh transport instance for the next Steam connection </summary>
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

        /// <summary> Keeps the Steam operation error or uses the fallback message </summary>
        private bool FailOperation(string fallback)
            => Fail(string.IsNullOrEmpty(operation.Error) ? fallback : operation.Error);

        /// <summary> Stores the failure reason and releases the pending Steam session </summary>
        private bool Fail(string error)
        {
            Error = error;
            Leave();
            return false;
        }

        /// <summary> Updates the Steam SDK and checks connection and original host availability </summary>
        public void Tick()
        {
            if (disposed) return;

            if (lobby.Active && !runtime.IsValid)
            {
                LoseConnection(GameErrors.SteamConnectionLost);
                return;
            }

            RetryInitialization();
            if (!UpdateCallbacks()) return;

            if (!lobby.HostAvailable) LoseConnection(GameErrors.HostConnectionLost);
        }

        /// <summary> Retries Steam initialization between sessions after a failed attempt </summary>
        private void RetryInitialization()
        {
            if (runtime.IsValid || manager.IsListening || manager.ShutdownInProgress || operation.Busy ||
                Time.unscaledTime < nextInitialization) return;

            nextInitialization = Time.unscaledTime + settings.Steam.RetryInterval;
            runtime.EnsureInitialized();
        }

        /// <summary> Pumps callbacks outside the active Steam transport and reports SDK connection loss </summary>
        private bool UpdateCallbacks()
        {
            if (!runtime.IsValid || manager.IsListening && manager.NetworkConfig.NetworkTransport == transport) return true;
            if (runtime.RunCallbacks()) return true;

            if (lobby.Active) LoseConnection(GameErrors.SteamConnectionLost);
            return false;
        }

        /// <summary> Leaves the lobby and notifies the session coordinator of connection loss </summary>
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

        /// <summary> Cancels lobby work, leaves the lobby and disables the current transport </summary>
        public void Leave()
        {
            operation.Cancel();
            lobby.Leave();
            if (transport) transport.enabled = false;

            nextInitialization = 0;
        }

        /// <summary> Leaves the session and releases Steam subscriptions and runtime resources </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Leave();
            
            runtime.JoinRequested -= OnJoinRequested;
            runtime.Dispose();
        }

    }

}
