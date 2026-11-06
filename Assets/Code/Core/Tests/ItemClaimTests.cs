using ArtDock.Core;
using NUnit.Framework;

namespace ArtDock.Tests
{
    public class ItemClaimTests
    {
        [Test]
        public void OnlyOneClientWinsTheSameRevision()
        {
            var claim = new ItemClaim();
            Assert.That(claim.TryGrab(1, 0), Is.True);
            Assert.That(claim.TryGrab(2, 0), Is.False);
            Assert.That(claim.Holder, Is.EqualTo(1ul));
            Assert.That(claim.Revision, Is.EqualTo(1u));
        }

        [Test]
        public void AnotherClientCannotReleaseHeldItem()
        {
            var claim = new ItemClaim();
            claim.TryGrab(1, 0);
            Assert.That(claim.TryRelease(2, claim.Revision), Is.False);
            Assert.That(claim.Holder, Is.EqualTo(1ul));
        }

        [Test]
        public void OldReleaseCannotAffectNewGrabByTheSameClient()
        {
            var claim = new ItemClaim();
            claim.TryGrab(1, 0);
            var oldRevision = claim.Revision;
            Assert.That(claim.TryRelease(1, oldRevision), Is.True);
            Assert.That(claim.TryGrab(1, claim.Revision), Is.True);
            Assert.That(claim.TryRelease(1, oldRevision), Is.False);
            Assert.That(claim.Holder, Is.EqualTo(1ul));
        }

        [Test]
        public void DisconnectReleasesOnlyTheCurrentHolder()
        {
            var claim = new ItemClaim();
            claim.TryGrab(1, 0);
            Assert.That(claim.Disconnect(2), Is.False);
            Assert.That(claim.Disconnect(1), Is.True);
            Assert.That(claim.Holder, Is.Null);
            Assert.That(claim.TryGrab(2, 0), Is.False);
            Assert.That(claim.TryGrab(2, claim.Revision), Is.True);
        }
    }
}
