using System;
using Unity.Netcode;

namespace ArtDock.Networking
{
    public struct ItemState : INetworkSerializable, IEquatable<ItemState>
    {
        public ulong Holder;
        public uint Revision;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Holder);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(ItemState other) => Holder == other.Holder && Revision == other.Revision;
    }
}
