using System;
using System.Reflection;
using ArtDock.Core;
using ArtDock.Networking;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArtDock.Tests
{
    public class PlayerLifecycleTests
    {
        [Test]
        public void ReconnectingClientUsesTheVacatedSpawnSlot()
        {
            var root = new GameObject("Spawn Slots Test");
            var settings = ScriptableObject.CreateInstance<GameSettings>();
            var context = root.AddComponent<NetworkContext>();
            var platforms = new PlatformService(Array.Empty<IConnectionPlatform>());
            var session = new SessionService(root.AddComponent<NetworkManager>(), context, platforms, settings);
            
            try
            {
                for (ulong id = 0; id < 4; id++)
                    context.SetName(id, "Player");
                var hostPosition = context.SpawnPositionFor(0);
                var vacantPosition = context.SpawnPositionFor(2);
                
                context.Remove(2);
                context.SetName(4, "Reconnected Player");
                Assert.That(context.SpawnPositionFor(4), Is.EqualTo(vacantPosition));
                Assert.That(context.SpawnPositionFor(4), Is.Not.EqualTo(hostPosition));
                
                context.Clear();
                context.SetName(ulong.MaxValue, "Next Session");
                Assert.That(context.SpawnPositionFor(ulong.MaxValue), Is.EqualTo(hostPosition));
            }
            finally
            {
                session.Dispose();
                platforms.Dispose();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidReceivedStateRemainsFlaggedUntilAFiniteTeleport(float coordinate)
        {
            var root = new GameObject("Transform Validation Test");
            try
            {
                var target = root.AddComponent<PlayerNetworkTransform>();
                Receive(target, coordinate, false);
                Assert.That(target.HasInvalidState, Is.True);
                
                Receive(target, 1, false);
                Assert.That(target.HasInvalidState, Is.True);
                
                Receive(target, 1, true);
                Assert.That(target.HasInvalidState, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Receive(PlayerNetworkTransform target, float coordinate, bool teleport)
        {
            object state = new NetworkTransform.NetworkTransformState();
            
            typeof(NetworkTransform.NetworkTransformState).GetProperty("HasPositionX")
                ?.GetSetMethod(true).Invoke(state, new object[] { true });
            
            typeof(NetworkTransform.NetworkTransformState).GetProperty("IsTeleportingNextFrame")
                ?.GetSetMethod(true).Invoke(state, new object[] { teleport });
            
            typeof(NetworkTransform.NetworkTransformState).GetField("PositionX",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(state, coordinate);
            
            typeof(PlayerNetworkTransform).GetMethod("OnNetworkTransformStateUpdated",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target,
                new[] { new NetworkTransform.NetworkTransformState(), state });
        }
    }
}
