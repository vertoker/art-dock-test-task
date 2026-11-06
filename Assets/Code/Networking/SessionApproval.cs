using System;
using System.Text;
using ArtDock.Core;
using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Networking
{
    internal class SessionApproval
    {
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

        public void Begin(int capacity)
        {
            registry = new ApprovalRegistry(capacity, settings.ProtocolVersion, settings.MaxIdentityLength);
            context.Clear();
            accepting = true;
        }

        public bool TryCreatePayload(IConnectionPlatform platform, out byte[] payload, out string error)
        {
            payload = null;
            error = "";
            
            var hello = new Handshake { version = settings.ProtocolVersion, id = platform.PlayerId, name = platform.PlayerName };
            if (!ValidName(hello.name) || string.IsNullOrWhiteSpace(hello.id) || hello.id.Length > settings.MaxIdentityLength)
            {
                error = "Player identity or name is invalid";
                return false;
            }
            
            payload = Encoding.GetBytes(JsonUtility.ToJson(hello));
            if (payload.Length > settings.MaxPayloadBytes)
            {
                error = "Connection data is too large";
                return false;
            }
            return true;
        }

        public void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Pending = false;
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.Reason = "Invalid connection data";
            
            if (!accepting || registry == null || request.Payload == null ||
                request.Payload.Length == 0 || request.Payload.Length > settings.MaxPayloadBytes) return;
            
            try
            {
                var hello = JsonUtility.FromJson<Handshake>(Encoding.GetString(request.Payload));
                if (hello == null || !ValidName(hello.name)) return;
                
                var result = registry.TryReserveSlot(request.ClientNetworkId, hello.id, hello.version);
                response.Reason = RejectionReason(result);
                response.Approved = result == ApprovalResult.Approved;
                response.CreatePlayerObject = response.Approved;
                if (response.Approved) context.SetName(request.ClientNetworkId, hello.name);
            }
            catch (ArgumentException)
            {
                response.Reason = "Invalid connection data";
            }
        }

        public void StopAccepting() => accepting = false;

        public void Remove(ulong client)
        {
            registry?.Remove(client);
            context.Remove(client);
        }

        public void Clear()
        {
            accepting = false;
            registry = null;
            context.Clear();
        }

        private bool ValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > settings.MaxNameLength) return false;
            foreach (var character in name)
                if (char.IsControl(character)) return false;
            Encoding.GetByteCount(name);
            return true;
        }

        private static string RejectionReason(ApprovalResult result)
        {
            switch (result)
            {
                case ApprovalResult.Approved: return "";
                case ApprovalResult.VersionMismatch: return "Game version does not match the host.";
                case ApprovalResult.InvalidIdentity: return "Invalid player identity.";
                case ApprovalResult.DuplicateIdentity: return "This player profile is already connected.";
                case ApprovalResult.SessionFull: return "Session is full.";
                default: return "Connection rejected.";
            }
        }
    }
}
