using System;
using System.Collections.Generic;

namespace ArtDock.Core
{
    public enum ApprovalResult : byte
    {
        Approved = 0,
        VersionMismatch = 1,
        InvalidIdentity = 2,
        DuplicateIdentity = 3,
        SessionFull = 4,
    }

    public class ApprovalRegistry
    {
        private readonly int capacity;
        private readonly string version;
        private readonly int maxIdentityLength;
        
        private readonly Dictionary<ulong, string> players = new();

        public ApprovalRegistry(int capacity, string version, int maxIdentityLength = 64)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (string.IsNullOrEmpty(version)) throw new ArgumentException("version is required", nameof(version));
            if (maxIdentityLength < 1) throw new ArgumentOutOfRangeException(nameof(maxIdentityLength));
            
            this.capacity = capacity;
            this.version = version;
            this.maxIdentityLength = maxIdentityLength;
        }

        public ApprovalResult TryReserveSlot(ulong client, string playerId, string version)
        {
            if (version != this.version)
                return ApprovalResult.VersionMismatch;
            
            if (string.IsNullOrWhiteSpace(playerId) || playerId.Length > maxIdentityLength)
                return ApprovalResult.InvalidIdentity;
            
            if (players.ContainsKey(client) || players.ContainsValue(playerId))
                return ApprovalResult.DuplicateIdentity;
            
            if (players.Count >= capacity)
                return ApprovalResult.SessionFull;
            
            players.Add(client, playerId);
            return ApprovalResult.Approved;
        }

        public void Remove(ulong client)
        {
            players.Remove(client);
        }
    }
}
