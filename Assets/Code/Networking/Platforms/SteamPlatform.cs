using System;
using System.Threading;
using System.Threading.Tasks;
using ArtDock.Core;
using Netcode.Transports.Facepunch;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking.Platforms
{
    public sealed class SteamPlatform : IConnectionPlatform, ILobbyPlatform
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
        public event Action<string> JoinRequested;
        public event Action<ConnectionMode, string> ConnectionLost;

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
    }
}
