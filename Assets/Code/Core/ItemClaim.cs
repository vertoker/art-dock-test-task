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
            if (Holder.HasValue || Revision != expectedRevision) return false;
            
            Holder = client;
            Revision = unchecked(Revision + 1);
            return true;
        }

        public bool TryRelease(ulong client, uint expectedRevision)
        {
            if (Holder != client || Revision != expectedRevision) return false;
            
            Holder = null;
            Revision = unchecked(Revision + 1);
            return true;
        }

        public bool Disconnect(ulong client)
        {
            return Holder == client && TryRelease(client, Revision);
        }
    }

    /// <summary> Tracks item claims and prevents a player from holding multiple items </summary>
    public class ItemClaims
    {
        private readonly Dictionary<ulong, ItemClaim> claimsByItem = new();
        private readonly Dictionary<ulong, ulong> heldItemByClient = new();

        public void Register(ulong item) => claimsByItem.Add(item, new ItemClaim());
        public ItemClaim For(ulong item) => claimsByItem[item];

        public bool TryGrab(ulong item, ulong client, uint revision)
        {
            if (heldItemByClient.ContainsKey(client) || !claimsByItem.TryGetValue(item, out var claim) || !claim.TryGrab(client, revision))
                return false;

            heldItemByClient.Add(client, item);
            return true;
        }

        public bool TryRelease(ulong item, ulong client, uint revision)
        {
            if (!claimsByItem.TryGetValue(item, out var claim) || !claim.TryRelease(client, revision)) return false;

            heldItemByClient.Remove(client);
            return true;
        }

        public void Remove(ulong item)
        {
            if (!claimsByItem.TryGetValue(item, out var claim)) return;

            if (claim.Holder.HasValue) heldItemByClient.Remove(claim.Holder.Value);
            
            claimsByItem.Remove(item);
        }

        public void Clear()
        {
            heldItemByClient.Clear();
            claimsByItem.Clear();
        }
    }
}
