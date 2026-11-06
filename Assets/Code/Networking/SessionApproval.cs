using System;
using System.Text;
using ArtDock.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    /// <summary> Creates connection payloads and validates players before their network objects spawn </summary>
    internal class SessionApproval
    {
        /// <summary> Client-declared protocol version, player identity and display name </summary>
        [Serializable]
        private class Handshake
        {
            public string version;
            public string id;
            public string name;
        }

        private static readonly UTF8Encoding Encoding = new(false, true);
        private readonly NetworkContext context;
        private readonly GameSettings settings;
        private ApprovalRegistry registry;
        private bool accepting;

        public SessionApproval(NetworkContext context, GameSettings settings)
        {
            this.context = context;
            this.settings = settings;
        }

        /// <summary> Creates a fresh slot registry and enables connection approval </summary>
        public void Begin(int capacity)
        {
            registry = new ApprovalRegistry(capacity, settings.ProtocolVersion, settings.MaxIdentityLength);
            context.Clear();
            accepting = true;
        }

        /// <summary> Validates local identity fields and encodes a size-limited UTF-8 handshake </summary>
        public bool TryCreatePayload(IConnectionPlatform platform, out byte[] payload, out string error)
        {
            payload = null;
            error = "";
            
            // Validate the name and identity before serializing the handshake
            var hello = new Handshake { version = settings.ProtocolVersion, id = platform.PlayerId, name = platform.PlayerName };
            if (!ValidName(hello.name) || string.IsNullOrWhiteSpace(hello.id) || hello.id.Length > settings.MaxIdentityLength)
            {
                error = GameErrors.InvalidPlayerIdentityOrName;
                return false;
            }
            
            payload = Encoding.GetBytes(JsonUtility.ToJson(hello));
            if (payload.Length > settings.MaxPayloadBytes)
            {
                error = GameErrors.PayloadTooLarge;
                return false;
            }
            return true;
        }

        /// <summary> Validates an incoming handshake and fills the NGO approval response </summary>
        public void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Pending = false;
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.Reason = GameErrors.InvalidPayload;
            if (!CanApprove(request, response)) return;

            try
            {
                var hello = JsonUtility.FromJson<Handshake>(Encoding.GetString(request.Payload));
                if (hello == null || !ValidName(hello.name)) return;

                ApplyHandshake(hello, request.ClientNetworkId, response);
            }
            catch (ArgumentException)
            {
                response.Reason = GameErrors.InvalidPayload;
            }
        }

        /// <summary> Checks whether the session accepts connections and the payload fits its limits </summary>
        private bool CanApprove(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            if (!accepting || registry == null || request.Payload == null ||
                request.Payload.Length == 0 || request.Payload.Length > settings.MaxPayloadBytes) return false;

            if (!context.World || context.World.Phase.Value != SessionPhase.Loading) return true;

            response.Reason = GameConstants.SessionLoading;
            return false;
        }

        /// <summary> Reserves a slot and assigns the approved player's name and spawn position </summary>
        private void ApplyHandshake(Handshake hello, ulong client, NetworkManager.ConnectionApprovalResponse response)
        {
            var result = registry.TryReserveSlot(client, hello.id, hello.version);
            response.Reason = RejectionReason(result);
            response.Approved = result == ApprovalResult.Approved;
            response.CreatePlayerObject = response.Approved;
            if (!response.Approved) return;

            context.SetName(client, hello.name);
            response.Position = context.SpawnPositionFor(client);
            response.Rotation = Quaternion.identity;
        }

        /// <summary> Closes admission before transport shutdown begins </summary>
        public void StopAccepting() => accepting = false;

        /// <summary> Releases the disconnected client slot and associated context data </summary>
        public void Remove(ulong client)
        {
            registry?.Remove(client);
            context.Remove(client);
        }

        /// <summary> Clears approval and player data after the session ends </summary>
        public void Clear()
        {
            accepting = false;
            registry = null;
            context.Clear();
        }

        /// <summary> Checks display name length, control characters and the network UTF-8 size limit </summary>
        private bool ValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > settings.MaxNameLength)
                return false;
            
            foreach (var character in name)
            {
                if (char.IsControl(character))
                    return false;
            }
            return Encoding.GetByteCount(name) <= FixedString128Bytes.UTF8MaxLengthInBytes;
        }

        /// <summary> Maps an approval result to the connection rejection message </summary>
        private static string RejectionReason(ApprovalResult result)
        {
            return result switch
            {
                ApprovalResult.Approved => string.Empty,
                ApprovalResult.VersionMismatch => GameErrors.VersionMismatch,
                ApprovalResult.InvalidIdentity => GameErrors.InvalidIdentity,
                ApprovalResult.DuplicateIdentity => GameErrors.DuplicateIdentity,
                ApprovalResult.SessionFull => GameConstants.SessionFull,
                _ => GameErrors.ConnectionRejected
            };
        }
    }
}
