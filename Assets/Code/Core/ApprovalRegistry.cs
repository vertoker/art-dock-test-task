using System;
using System.Collections.Generic;

namespace ArtDock.Core
{
    /// <summary> Result of validating a player before connection </summary>
    public enum ApprovalResult : byte
    {
        /// <summary> The player passed validation and reserved a session slot </summary>
        Approved = 0,
        /// <summary> The client protocol differs from the host protocol </summary>
        VersionMismatch = 1,
        /// <summary> The player identity is empty or too long </summary>
        InvalidIdentity = 2,
        /// <summary> The client or player identity is already registered </summary>
        DuplicateIdentity = 3,
        /// <summary> The session has no free slots </summary>
        SessionFull = 4,
    }

    /// <summary> Checks protocol and player identities, reserving slots before network objects are created </summary>
    public class ApprovalRegistry
    {
        /// <summary> Maximum number of players in the session </summary>
        private readonly int capacity;
        /// <summary> Protocol version accepted by the host </summary>
        private readonly string version;
        /// <summary> Maximum player identity length </summary>
        private readonly int maxIdentityLength;
        
        /// <summary> Reserved slots mapped from network clients to player identities </summary>
        private readonly Dictionary<ulong, string> players = new();

        public ApprovalRegistry(int capacity, string version, int maxIdentityLength = 64)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (string.IsNullOrEmpty(version)) throw new ArgumentException(GameErrors.VersionRequired, nameof(version));
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
