namespace ArtDock.Core
{
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
}
