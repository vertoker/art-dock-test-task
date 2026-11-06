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
        [SerializeField] private ItemConfiguration items = new();
        [SerializeField] private SteamConfiguration steam = new();

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
        public ItemConfiguration Items => items;
        public SteamConfiguration Steam => steam;

        [Serializable]
        public sealed class SteamConfiguration
        {
            public uint AppId = 480;
            [Min(0.1f)] public float PresenceInterval = 1;
            [Min(1)] public float RetryInterval = 5;
        }

        [Serializable]
        public sealed class ItemConfiguration
        {
            [Min(1)] public int MaxItems = 48;
            [Min(0.1f)] public float CubeSize = 0.6f;
            [Min(0.1f)] public float InteractionRange = 4;
            
            [Min(0.1f)] public float HoldDistance = 2.2f;
            [Min(0.1f)] public float MaxHolderDistance = 5;
            [Min(0.1f)] public float MaxHoldSpeed = 12;
            [Min(0.1f)] public float HoldSharpness = 14;
            
            [Min(0)] public float ContactGap = 0.03f;
            [Min(0.1f)] public float ThrowImpulse = 9;
            [Min(0.01f)] public float ActionCooldown = 0.15f;
            [Min(0.01f)] public float SpawnCooldown = 0.5f;
            [Min(0)] public float MaxVisualOffset = 0.3f;
            [Min(0.1f)] public float VisualSharpness = 24;
            
            public Vector3 HalfExtents => Vector3.one * (CubeSize * 0.5f);
            public float HoldRadius => HalfExtents.magnitude;
        }

        [Serializable]
        public sealed class PlayerConfiguration
        {
            public Vector3 SpawnOrigin = new(0, 0.1f, -5);
            [Min(1)] public float SpawnSpacing = 2;
            [Min(0.1f)] public float RespawnCooldown = 1;
            
            [Min(0.1f)] public float MoveSpeed = 5;
            [Min(0.01f)] public float MouseSensitivity = 0.12f;
            [Range(1, 89)] public float PitchLimit = 85;
            
            [Min(0.1f)] public float JumpSpeed = 6;
            [Min(0.1f)] public float Gravity = 18;
            [Min(0.1f)] public float FallSpeed = 12;
            
            [Min(0.1f)] public float EyeHeight = 1.6f;
            [Min(0)] public float PositionTolerance = 1.5f;
            [Min(0.1f)] public float AllowedPositionSpeed = 15;
            
            [Min(0)] public float BufferedMovementSeconds = 0.5f;
            [Min(0.1f)] public float CorrectionInterval = 0.5f;
            
            [Min(0.01f)] public float AimSendInterval = 0.1f;
            [Min(0.01f)] public float AimReceiveInterval = 0.05f;
            [Min(0.01f)] public float AimRefreshInterval = 0.5f;
            [Min(0)] public float AimAngleThreshold = 0.25f;
            
            public Vector3 MinimumPosition = new(-25, -8, -25);
            public Vector3 MaximumPosition = new(25, 20, 25);
        }
    }
}
