using ArtDock.Core;
using NUnit.Framework;

namespace ArtDock.Tests
{
    public class ItemClaimsTests
    {
        [Test]
        public void OnePlayerCannotHoldTwoDifferentItems()
        {
            var claims = Create();
            Assert.That(claims.TryGrab(10, 1, 0), Is.True);
            Assert.That(claims.TryGrab(20, 1, 0), Is.False);
            Assert.That(claims.For(20).Revision, Is.Zero);
            Assert.That(claims.TryGrab(20, 2, 0), Is.True);
        }

        [Test]
        public void OnlyOneRemoteRequestWinsTheSameRevision()
        {
            var claims = Create();
            Assert.That(claims.TryGrab(10, 1, 0), Is.True);
            Assert.That(claims.TryGrab(10, 2, 0), Is.False);
            Assert.That(claims.For(10).Holder, Is.EqualTo(1ul));
            Assert.That(claims.For(10).Revision, Is.EqualTo(1u));
        }

        [Test]
        public void StaleOrForeignReleaseCannotFreeTheHoldersOtherSlot()
        {
            var claims = Create();
            claims.TryGrab(10, 1, 0);
            Assert.That(claims.TryRelease(10, 2, 1), Is.False);
            Assert.That(claims.TryRelease(10, 1, 0), Is.False);
            Assert.That(claims.TryGrab(20, 1, 0), Is.False);
            Assert.That(claims.TryRelease(10, 1, 1), Is.True);
            Assert.That(claims.TryGrab(20, 1, 0), Is.True);
            Assert.That(claims.TryRelease(10, 1, 1), Is.False);
            Assert.That(claims.For(20).Holder, Is.EqualTo(1ul));
        }

        [Test]
        public void DespawnFreesHolderAndReusedNetworkIdStartsAtRevisionZero()
        {
            var claims = Create();
            claims.TryGrab(10, 1, 0);
            claims.Remove(10);
            Assert.That(claims.TryGrab(20, 1, 0), Is.True);
            claims.Register(10);
            Assert.That(claims.For(10).Holder, Is.Null);
            Assert.That(claims.For(10).Revision, Is.Zero);
            Assert.That(claims.TryGrab(10, 2, 0), Is.True);
        }

        [Test]
        public void UnknownItemsCannotBeClaimedAndSessionResetClearsHolders()
        {
            var claims = Create();
            Assert.That(claims.TryGrab(999, 1, 0), Is.False);
            claims.TryGrab(10, 1, 0);
            claims.Clear();
            claims.Register(20);
            Assert.That(claims.TryGrab(20, 1, 0), Is.True);
        }

        private static ItemClaims Create()
        {
            var claims = new ItemClaims();
            claims.Register(10);
            claims.Register(20);
            return claims;
        }
    }
}
