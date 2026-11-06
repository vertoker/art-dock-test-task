using System;
using Unity.Collections;
using Unity.Netcode;

namespace ArtDock.Networking
{
    public struct SessionPlayer : INetworkSerializable, IEquatable<SessionPlayer>
    {
        public ulong ClientId;
        public bool IsLoaded;
        private FixedString128Bytes playerName;

        public string Name => playerName.ToString();

        public SessionPlayer(ulong clientId, string name, bool isLoaded)
        {
            ClientId = clientId;
            playerName = new FixedString128Bytes(name);
            IsLoaded = isLoaded;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref playerName);
            serializer.SerializeValue(ref IsLoaded);
        }

        public bool Equals(SessionPlayer other)
            => ClientId == other.ClientId && playerName.Equals(other.playerName) && IsLoaded == other.IsLoaded;
    }
}
