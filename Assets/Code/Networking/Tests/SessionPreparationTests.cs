using System;
using System.Threading;
using System.Threading.Tasks;
using ArtDock.Core;
using ArtDock.Networking;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace ArtDock.Tests
{
    public class SessionPreparationTests
    {
        private class DelayedPlatform : IConnectionPlatform
        {
            private readonly TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>();
            private readonly bool cancelImmediately;
            private CancellationTokenRegistration registration;
            public ConnectionMode Mode => ConnectionMode.Lan;
            public string PlayerId => "player";
            public string PlayerName => "Player";
            public string LobbyId => "";
            public string Error => "";
            public bool Left { get; private set; }
            public event Action<string> JoinRequested { add { } remove { } }
            public event Action<ConnectionMode, string> ConnectionLost { add { } remove { } }
            public DelayedPlatform(bool cancelImmediately = false) => this.cancelImmediately = cancelImmediately;
            public Task<bool> PrepareHost(string title, int capacity, bool isPrivate, CancellationToken cancellationToken) => Prepare(cancellationToken);
            public Task<bool> PrepareJoin(string address, CancellationToken cancellationToken) => Prepare(cancellationToken);
            private Task<bool> Prepare(CancellationToken cancellationToken)
            {
                if (cancelImmediately) registration = cancellationToken.Register(() => ready.TrySetCanceled());
                return ready.Task;
            }
            public void Complete() => ready.TrySetResult(true);
            public void Tick() { }
            public void UpdatePresence(bool inGame, int players, int capacity, bool acceptingPlayers) { }
            public void Leave() => Left = true;
            public void Dispose() => registration.Dispose();
        }

        [Test]
        public async Task CancelledPreparationFinishesBeforeAdapterCanBeReused()
        {
            var root = new GameObject("Session Test");
            var settings = ScriptableObject.CreateInstance<GameSettings>();
            var platform = new DelayedPlatform();
            var platforms = new PlatformService(new[] { platform });
            var session = new SessionService(root.AddComponent<NetworkManager>(), root.AddComponent<NetworkContext>(), platforms, settings);
            try
            {
                var firstAttempt = session.Host("Test", 2, false);
                session.Stop();
                session.Tick();
                Assert.That(session.State, Is.EqualTo(SessionState.Stopping));
                Assert.That(platform.Left, Is.False);
                Assert.That(await session.Host("Another", 2, false), Is.False);

                platform.Complete();
                Assert.That(await firstAttempt, Is.False);
                session.Tick();
                Assert.That(session.State, Is.EqualTo(SessionState.Offline));
                Assert.That(platform.Left, Is.True);
            }
            finally
            {
                session.Dispose();
                platform.Complete();
                platforms.Dispose();
                platform.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public async Task SynchronousPreparationCancellationDoesNotRepeatStopNotification()
        {
            var root = new GameObject("Session Test");
            var settings = ScriptableObject.CreateInstance<GameSettings>();
            var platform = new DelayedPlatform(true);
            var platforms = new PlatformService(new[] { platform });
            var session = new SessionService(root.AddComponent<NetworkManager>(), root.AddComponent<NetworkContext>(), platforms, settings);
            try
            {
                var firstAttempt = session.Host("Test", 2, false);
                var changes = 0;
                session.Changed += () => changes++;
                session.Stop("Cancelled by user");
                Assert.That(await firstAttempt, Is.False);
                Assert.That(changes, Is.EqualTo(1));
                Assert.That(session.Message, Is.EqualTo("Cancelled by user"));
                session.Tick();
                Assert.That(session.State, Is.EqualTo(SessionState.Offline));
            }
            finally
            {
                session.Dispose();
                platform.Complete();
                platforms.Dispose();
                platform.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }
    }
}
