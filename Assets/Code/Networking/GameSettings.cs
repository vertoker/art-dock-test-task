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
        [SerializeField] private DiagnosticsConfiguration diagnostics = new();
        [SerializeField] private StatisticsConfiguration statistics = new();
        [SerializeField] private DiscoveryConfiguration discovery = new();
        [SerializeField] private LaunchConfiguration launch = new();
        [SerializeField] private WorldConfiguration world = new();

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
        public DiagnosticsConfiguration Diagnostics => diagnostics;
        public StatisticsConfiguration Statistics => statistics;
        public DiscoveryConfiguration Discovery => discovery;
        public LaunchConfiguration Launch => launch;
        public WorldConfiguration World => world;

        [Serializable]
        public sealed class LaunchConfiguration
        {
            public int TargetFrameRate = 60;
            public bool RunInBackground = true;
            public bool FullscreenOnStart = true;
            public string AuthorUrl = "https://www.linkedin.com/in/vertoker";
        }

        [Serializable]
        public sealed class WorldConfiguration
        {
            public Vector3 PyramidOrigin = new(2, 0, 10);
            [Range(0, 8)] public int PyramidRows = 7;
            [Min(0.1f)] public float PyramidSpacing = 0.65f;
            [Min(0)] public float PyramidGroundGap = 0.01f;
        }

        [Serializable]
        public sealed class DiagnosticsConfiguration
        {
            [Min(0.5f)] public float PingInterval = 2;
            [Min(1)] public float PingTimeout = 6;
            [Min(0.1f)] public float RefreshInterval = 0.25f;
        }

        [Serializable]
        public sealed class StatisticsConfiguration
        {
            [Min(1)] public float FlushInterval = 10;
            [Min(1)] public int AchievementThreshold = 10;
        }

        [Serializable]
        public sealed class DiscoveryConfiguration
        {
            [Range(1, 65535)] public int Port = 47778;
            [Min(0.5f)] public float BeaconInterval = 1;
            [Min(1)] public float HostLifetime = 4;
            [Min(1)] public float RetryInterval = 5;
            [Range(1, 64)] public int MaxHosts = 32;
            [Range(1, 32)] public int PacketsPerTick = 8;
            [Range(128, 2048)] public int MaxPayloadBytes = 512;
        }

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
            [Min(0.1f)] public float DispenserCooldown = 1;
            [Min(0)] public float MaxVisualOffset = 0.3f;
            [Min(0.1f)] public float VisualSharpness = 24;
            
            public Vector3 HalfExtents => Vector3.one * (CubeSize * 0.5f);
            public float HoldRadius => HalfExtents.magnitude;
        }

        [Serializable]
        public sealed class PlayerConfiguration
        {
            [Min(0.1f)] public float ColorCooldown = 0.5f;
            public Color[] Palette =
            {
                new(0.2f, 0.55f, 0.95f),
                new(0.95f, 0.3f, 0.25f),
                new(0.25f, 0.8f, 0.4f),
                new(0.95f, 0.75f, 0.2f),
                new(0.75f, 0.35f, 0.9f),
                new(0.2f, 0.85f, 0.85f),
            };

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
