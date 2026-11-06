using System;
using System.Threading;
using System.Threading.Tasks;
using ArtDock.Core;
using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    public partial class SessionService
    {
        /// <summary> Prepares the selected platform and starts NGO for the current connection attempt </summary>
        private async Task<bool> Start(bool host, string address, int capacity, bool isPrivate)
        {
            // Start a new attempt only after the previous session has fully stopped
            if (disposed || State != SessionState.Offline || manager.ShutdownInProgress || manager.IsListening)
                return false;

            var current = BeginAttempt(host, address, capacity);
            IsPrivate = isPrivate;
            var token = current.Token;
            Changed?.Invoke();

            try
            {
                var ready = await PreparePlatform(current, host, address, isPrivate);
                return ValidatePreparation(current, token, ready) && StartNetwork(current, host);
            }
            catch (Exception exception)
            {
                HandleStartFailure(current, exception);
                return false;
            }
        }

        /// <summary> Sets preparing state and bounds the new session title and capacity </summary>
        private SessionAttempt BeginAttempt(bool host, string address, int capacity)
        {
            State = SessionState.Preparing;
            Message = GameConstants.Connecting;
            Capacity = Mathf.Clamp(capacity, 2, settings.MaxPlayers);
            Title = string.IsNullOrWhiteSpace(address) || !host ? GameConstants.GameName : address.Trim();
            
            if (Title.Length > settings.MaxNameLength) Title = Title[..settings.MaxNameLength];
            return attempt = new SessionAttempt(settings.ConnectionTimeout);
        }

        /// <summary> Selects the platform and starts cancellable host or client preparation </summary>
        private Task<bool> PreparePlatform(SessionAttempt current, bool host, string address, bool isPrivate)
        {
            platform = platforms.For(Mode);
            return current.Prepare(platform, host, Title, Capacity, isPrivate, address);
        }

        /// <summary> Rejects stale or canceled preparation results before starting the transport </summary>
        private bool ValidatePreparation(SessionAttempt current, CancellationToken token, bool ready)
        {
            if (!IsCurrent(current)) return false;
            
            token.ThrowIfCancellationRequested();
            if (ready) return true;
            
            Stop(platform.Error);
            return false;
        }

        /// <summary> Builds the handshake, starts the transport and creates the host world </summary>
        private bool StartNetwork(SessionAttempt current, bool host)
        {
            if (!approval.TryCreatePayload(platform, out var payload, out var error))
            {
                Stop(error);
                return false;
            }
            
            approval.Begin(Capacity);
            manager.NetworkConfig.ConnectionData = payload;
            State = SessionState.Connecting;
            
            current.BeginConnection();
            var started = host ? manager.StartHost() : manager.StartClient();
            
            if (!started)
            {
                Stop(GameErrors.TransportStartFailed);
                return false;
            }
            if (host) scenes.SpawnWorld(Capacity);
            
            Changed?.Invoke();
            return true;
        }

        /// <summary> Ends only the current attempt and reports its timeout or exception </summary>
        private void HandleStartFailure(SessionAttempt current, Exception exception)
        {
            if (!IsCurrent(current)) return;
            
            if (exception is OperationCanceledException)
            {
                Stop(GameErrors.ConnectionTimedOut);
                return;
            }
            
            Stop(exception.Message);
            Debug.LogException(exception);
        }

        private bool IsCurrent(SessionAttempt current)
            => !disposed && attempt == current && State != SessionState.Stopping;

        /// <summary> Completes the local connection attempt and notifies interface subscribers </summary>
        private void OnConnected(ulong client)
        {
            if (client == manager.LocalClientId && State == SessionState.Connecting)
            {
                attempt?.Cancel();
                State = SessionState.Connected;
                Message = IsHost ? GameConstants.Hosting : GameConstants.Connected;
            }
            Changed?.Invoke();
        }

        /// <summary> Releases departed players and stops the session when the local client loses its connection </summary>
        private void OnDisconnected(ulong client)
        {
            if (manager.IsServer)
            {
                approval.Remove(client);
            }
            
            // Losing the local client or host ends the local session
            if (State != SessionState.Stopping && State != SessionState.Offline &&
                (client == manager.LocalClientId || !manager.IsServer && client == NetworkManager.ServerClientId))
            {
                var reason = manager.DisconnectReason;
                
                // Replace transport status messages with a readable disconnect reason
                if (string.IsNullOrWhiteSpace(reason) || reason.StartsWith(GameConstants.TransportDisconnectPrefix, StringComparison.Ordinal))
                    reason = State == SessionState.Connecting ? GameErrors.ConnectionTimedOutOrRejected : GameErrors.HostConnectionLost;
                
                Stop(reason);
            }
            Changed?.Invoke();
        }

        private void OnNetworkStopped(bool wasHost)
        {
            if (State != SessionState.Offline && State != SessionState.Stopping)
                Stop(GameErrors.SessionEnded);
        }

        private void OnTransportFailure() => Stop(GameErrors.TransportFailed);

        private void OnConnectionLost(ConnectionMode mode, string reason)
        {
            if (platform != null && platform.Mode == mode && State != SessionState.Offline)
                Stop(reason);
        }
    }
}
