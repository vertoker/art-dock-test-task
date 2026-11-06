using System.Collections.Generic;

namespace ArtDock.Core
{
    /// <summary> Stores the item holder and revision so the server can reject stale commands </summary>
    public class ItemClaim
    {
        public ulong? Holder { get; private set; }
        public uint Revision { get; private set; }
        
        public bool TryGrab(ulong client, uint expectedRevision)
        {
            // Only an unheld item with the current revision can be grabbed
            if (Holder.HasValue || Revision != expectedRevision) return false;
            
            Holder = client;
            Revision = unchecked(Revision + 1);
            return true;
        }

        public bool TryRelease(ulong client, uint expectedRevision)
        {
            // Only the current holder with the current revision can release the item
            if (Holder != client || Revision != expectedRevision) return false;
            
            Holder = null;
            Revision = unchecked(Revision + 1);
            return true;
        }

        public bool Disconnect(ulong client)
        {
            // Release the item only if it belongs to the disconnected client
            return Holder == client && TryRelease(client, Revision);
        }
    }

    /// <summary> Tracks item claims and prevents a player from holding multiple items </summary>
    public class ItemClaims
    {
        /// <summary> Claim state for each registered item </summary>
        private readonly Dictionary<ulong, ItemClaim> claims = new();
        /// <summary> The item currently held by each player </summary>
        private readonly Dictionary<ulong, ulong> held = new();

        /// <summary> Registers an unheld item in the claim registry </summary>
        public void Register(ulong item) => claims.Add(item, new ItemClaim());
        /// <summary> Returns the server claim state for an item </summary>
        public ItemClaim For(ulong item) => claims[item];

        public bool TryGrab(ulong item, ulong client, uint revision)
        {
            // Check that the player has free hands and the item claim is current
            if (held.ContainsKey(client) || !claims.TryGetValue(item, out var claim) || !claim.TryGrab(client, revision))
                return false;

            held.Add(client, item);
            return true;
        }

        public bool TryRelease(ulong item, ulong client, uint revision)
        {
            // Release the claim before removing the item from the player's hands
            if (!claims.TryGetValue(item, out var claim) || !claim.TryRelease(client, revision)) return false;

            held.Remove(client);
            return true;
        }

        public void Remove(ulong item)
        {
            // Ignore items that have already been removed
            if (!claims.TryGetValue(item, out var claim)) return;

            // Free the holder's hands when their item is removed
            if (claim.Holder.HasValue) held.Remove(claim.Holder.Value);
            
            claims.Remove(item);
        }

        public void Clear()
        {
            held.Clear();
            claims.Clear();
        }
    }
}
