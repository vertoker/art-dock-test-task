using System;
using Unity.Collections;
using Unity.Netcode;

namespace ArtDock.Networking
{
    /// <summary> Player roster data containing the name, load status and application RTT </summary>
    public struct SessionPlayer : INetworkSerializable, IEquatable<SessionPlayer>
    {
        public ulong ClientId;
        public bool IsLoaded;
        public int RoundTripMs;
        private FixedString128Bytes playerName;

        public string Name => playerName.ToString();

        public SessionPlayer(ulong clientId, string name, bool isLoaded)
        {
            ClientId = clientId;
            playerName = new FixedString128Bytes(name);
            IsLoaded = isLoaded;
            RoundTripMs = -1;
        }

        /// <summary> Serializes the client ID, name, loading status and application RTT </summary>
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref playerName);
            serializer.SerializeValue(ref IsLoaded);
            serializer.SerializeValue(ref RoundTripMs);
        }

        /// <summary> Compares every serialized field so the network list detects player data changes </summary>
        public bool Equals(SessionPlayer other)
            => ClientId == other.ClientId && playerName.Equals(other.playerName) && IsLoaded == other.IsLoaded && RoundTripMs == other.RoundTripMs;
    }
}
