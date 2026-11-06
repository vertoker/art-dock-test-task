using System.Collections.Generic;

namespace ArtDock.Core
{
    public sealed class ItemClaims
    {
        private readonly Dictionary<ulong, ItemClaim> claims = new();
        private readonly Dictionary<ulong, ulong> held = new();

        public void Register(ulong item) => claims.Add(item, new ItemClaim());
        public ItemClaim For(ulong item) => claims[item];

        public bool TryGrab(ulong item, ulong client, uint revision)
        {
            if (held.ContainsKey(client) || !claims.TryGetValue(item, out var claim) || !claim.TryGrab(client, revision))
                return false;

            held.Add(client, item);
            return true;
        }

        public bool TryRelease(ulong item, ulong client, uint revision)
        {
            if (!claims.TryGetValue(item, out var claim) || !claim.TryRelease(client, revision)) return false;

            held.Remove(client);
            return true;
        }

        public void Remove(ulong item)
        {
            if (!claims.TryGetValue(item, out var claim)) return;

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
