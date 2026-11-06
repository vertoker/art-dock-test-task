using System;
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
        [Space]
        [SerializeField] private PlayerConfiguration player = new();

        public int MaxPlayers => maxPlayers;
        public int TickRate => tickRate;
        public string ProtocolVersion => protocolVersion;
        public ushort Port => (ushort)Mathf.Clamp(port, 1, 65535);
        
        public float ConnectionTimeout => connectionTimeout;
        public float SceneLoadTimeout => sceneLoadTimeout;
        
        public int MaxPayloadBytes => maxPayloadBytes;
        public int MaxNameLength => maxNameLength;
        public int MaxIdentityLength => maxIdentityLength;
        public PlayerConfiguration Player => player;

        [Serializable]
        public sealed class PlayerConfiguration
        {
            public Vector3 SpawnOrigin = new(0, .1f, -5);
            [Min(1)] public float SpawnSpacing = 2;
            [Min(.1f)] public float RespawnCooldown = 1;
            
            [Min(.1f)] public float MoveSpeed = 5;
            [Min(.01f)] public float MouseSensitivity = .12f;
            [Range(1, 89)] public float PitchLimit = 85;
            
            [Min(.1f)] public float JumpSpeed = 6;
            [Min(.1f)] public float Gravity = 18;
            [Min(.1f)] public float FallSpeed = 12;
            
            [Min(.1f)] public float EyeHeight = 1.6f;
            [Min(0)] public float PositionTolerance = 1.5f;
            [Min(.1f)] public float AllowedPositionSpeed = 15;
            
            [Min(0)] public float BufferedMovementSeconds = .5f;
            [Min(.1f)] public float CorrectionInterval = .5f;
            
            [Min(.01f)] public float AimSendInterval = .1f;
            [Min(.01f)] public float AimReceiveInterval = .05f;
            [Min(.01f)] public float AimRefreshInterval = .5f;
            [Min(0)] public float AimAngleThreshold = .25f;
            
            public Vector3 MinimumPosition = new(-25, -8, -25);
            public Vector3 MaximumPosition = new(25, 20, 25);
        }
    }
}
