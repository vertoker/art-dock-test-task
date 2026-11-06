using ArtDock.Networking;
using NUnit.Framework;
using UnityEngine;

namespace ArtDock.Tests
{
    public class PlayerValidationTests
    {
        private GameSettings settings;
        [SetUp] public void SetUp() => settings = ScriptableObject.CreateInstance<GameSettings>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(settings);

        [Test]
        public void InvalidMovementKeepsTheAcceptedPositionAndCanRecover()
        {
            var accepted = new Vector3(1, 0, 1);
            var state = new PlayerValidation(settings, accepted);
            Assert.That(state.TryMove(accepted + Vector3.right * 100, 0.1f), Is.False);
            Assert.That(state.TrustedPosition, Is.EqualTo(accepted));
            Assert.That(state.PositionValid, Is.False);
            Assert.That(state.TryMove(accepted, 0.1f), Is.True);
            Assert.That(state.PositionValid, Is.True);
            Assert.That(state.TryMove(new Vector3(float.NaN, 0, 0), 0.1f), Is.False);
            Assert.That(state.TrustedPosition, Is.EqualTo(accepted));
        }

        [Test]
        public void RespawnResetsTheMovementOrigin()
        {
            var state = new PlayerValidation(settings, Vector3.zero);
            var spawn = PlayerValidation.SpawnPosition(0, settings);
            state.Reset(spawn);
            Assert.That(state.TryMove(spawn, 0), Is.True);
            Assert.That(state.TrustedPosition, Is.EqualTo(spawn));
        }

        [TestCase(0ul)]
        [TestCase(1ul)]
        [TestCase(ulong.MaxValue)]
        public void SpawnPositionsStayInsideArenaWithoutUnsignedUnderflow(ulong clientId)
        {
            var spawn = PlayerValidation.SpawnPosition(clientId, settings);
            Assert.That(PlayerValidation.OutsideBounds(spawn, settings), Is.False);
            Assert.That(Mathf.Abs(spawn.x), Is.LessThan(8));
        }

        [Test]
        public void AimRejectsNonFiniteAndUnnormalizedDirectionsAndLimitsFrequency()
        {
            var state = new PlayerValidation(settings, Vector3.zero);
            Assert.That(state.TryAim(new Vector3(float.NaN, 0, 0), 1), Is.False);
            Assert.That(state.TryAim(Vector3.zero, 1), Is.False);
            Assert.That(state.TryAim(Vector3.right * 2, 1), Is.False);
            Assert.That(state.TryAim(Vector3.right, 1), Is.True);
            Assert.That(state.TryAim(Vector3.back, 1.001f), Is.False);
            Assert.That(state.AimDirection, Is.EqualTo(Vector3.right));
            Assert.That(state.TryAim(Vector3.back, 2), Is.True);
            Assert.That(state.TryAim(Vector3.forward, float.PositiveInfinity), Is.False);
        }

        [Test]
        public void InvalidAndOutOfWorldPositionsRequireRespawn()
        {
            Assert.That(PlayerValidation.OutsideBounds(new Vector3(float.PositiveInfinity, 0, 0), settings), Is.True);
            Assert.That(PlayerValidation.OutsideBounds(Vector3.down * 100, settings), Is.True);
            Assert.That(PlayerValidation.OutsideBounds(Vector3.zero, settings), Is.False);
        }
    }
}
