using UnityEngine;

namespace ArtDock.Networking
{
    [CreateAssetMenu(menuName = "Game/Settings")]
    public class GameSettings : ScriptableObject
    {
        [SerializeField, Range(2, 4)] private int maxPlayers = 4;
        [SerializeField, Min(1)] private int tickRate = 30;
        [SerializeField] private string protocolVersion = "1";
        [SerializeField, Range(1, 65535)] private int port = 7777;
        [Space]
        [SerializeField, Min(1)] private float connectionTimeout = 10;
        [SerializeField, Min(1)] private float sceneLoadTimeout = 30;
        [Space]
        [SerializeField, Min(1)] private int maxPayloadBytes = 512;
        [SerializeField, Min(1)] private int maxNameLength = 32;
        [SerializeField, Min(1)] private int maxIdentityLength = 64;

        public int MaxPlayers => maxPlayers;
        public int TickRate => tickRate;
        public string ProtocolVersion => protocolVersion;
        public ushort Port => (ushort)Mathf.Clamp(port, 1, 65535);
        
        public float ConnectionTimeout => connectionTimeout;
        public float SceneLoadTimeout => sceneLoadTimeout;
        
        public int MaxPayloadBytes => maxPayloadBytes;
        public int MaxNameLength => maxNameLength;
        public int MaxIdentityLength => maxIdentityLength;
    }
}
