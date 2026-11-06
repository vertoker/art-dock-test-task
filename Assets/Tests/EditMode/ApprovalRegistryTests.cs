using ArtDock.Core;
using NUnit.Framework;

namespace ArtDock.Tests
{
    public class ApprovalRegistryTests
    {
        [Test]
        public void ReservationsCountTowardsCapacityBeforeClientsConnect()
        {
            var registry = new ApprovalRegistry(2, "1");
            Assert.That(registry.TryReserveSlot(0, "host", "1"), Is.EqualTo(ApprovalResult.Approved));
            Assert.That(registry.TryReserveSlot(1, "pending", "1"), Is.EqualTo(ApprovalResult.Approved));
            Assert.That(registry.TryReserveSlot(2, "third", "1"), Is.EqualTo(ApprovalResult.SessionFull));
        }

        [Test]
        public void RemovingPendingClientReleasesItsSlotAndIdentity()
        {
            var registry = new ApprovalRegistry(1, "1");
            registry.TryReserveSlot(1, "player", "1");
            registry.Remove(1);
            registry.Remove(1);
            Assert.That(registry.TryReserveSlot(2, "player", "1"), Is.EqualTo(ApprovalResult.Approved));
        }

        [Test]
        public void WrongVersionDoesNotConsumeSlot()
        {
            var registry = new ApprovalRegistry(1, "1");
            Assert.That(registry.TryReserveSlot(1, "player", "2"), Is.EqualTo(ApprovalResult.VersionMismatch));
            Assert.That(registry.TryReserveSlot(2, "player", "1"), Is.EqualTo(ApprovalResult.Approved));
        }

        [Test]
        public void DuplicateIdentityAndClientAreRejectedWithoutOverwritingReservation()
        {
            var registry = new ApprovalRegistry(4, "1");
            registry.TryReserveSlot(1, "first", "1");
            Assert.That(registry.TryReserveSlot(2, "first", "1"), Is.EqualTo(ApprovalResult.DuplicateIdentity));
            Assert.That(registry.TryReserveSlot(1, "second", "1"), Is.EqualTo(ApprovalResult.DuplicateIdentity));
            Assert.That(registry.TryReserveSlot(2, "second", "1"), Is.EqualTo(ApprovalResult.Approved));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("123456789")]
        public void InvalidIdentityDoesNotConsumeSlot(string identity)
        {
            var registry = new ApprovalRegistry(1, "1", 8);
            Assert.That(registry.TryReserveSlot(1, identity, "1"), Is.EqualTo(ApprovalResult.InvalidIdentity));
            Assert.That(registry.TryReserveSlot(2, "valid", "1"), Is.EqualTo(ApprovalResult.Approved));
        }
    }
}
