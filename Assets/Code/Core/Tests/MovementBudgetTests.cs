using ArtDock.Core;
using NUnit.Framework;

namespace ArtDock.Tests
{
    public class MovementBudgetTests
    {
        [Test]
        public void ToleranceIsNotGrantedAgainForEverySnapshot()
        {
            var budget = new MovementBudget(1, 5, .5f);
            Assert.That(budget.TryConsume(1, 0), Is.True);
            Assert.That(budget.TryConsume(.2f, .01f), Is.False);
            Assert.That(budget.TryConsume(.5f, .09f), Is.True);
        }

        [Test]
        public void IdleTimeCannotAccumulateAnUnlimitedTeleport()
        {
            var budget = new MovementBudget(1, 5, .5f);
            Assert.That(budget.TryConsume(4, 100), Is.False);
            Assert.That(budget.TryConsume(3.5f, 0), Is.True);
            Assert.That(budget.TryConsume(.1f, 0), Is.False);
        }

        [TestCase(float.NaN, 1)]
        [TestCase(float.PositiveInfinity, 1)]
        [TestCase(-1, 1)]
        [TestCase(1, float.NaN)]
        [TestCase(1, float.PositiveInfinity)]
        [TestCase(1, -1)]
        public void InvalidSamplesDoNotChangeTheBudget(float distance, float elapsed)
        {
            var budget = new MovementBudget(1, 5, .5f);
            Assert.That(budget.TryConsume(distance, elapsed), Is.False);
            Assert.That(budget.TryConsume(1, 0), Is.True);
            Assert.That(budget.TryConsume(.1f, 0), Is.False);
        }

        [Test]
        public void ResetRestoresOnlyTheInitialTolerance()
        {
            var budget = new MovementBudget(1, 5, .5f);
            Assert.That(budget.TryConsume(1, 0), Is.True);
            budget.Reset();
            Assert.That(budget.TryConsume(1.1f, 0), Is.False);
            Assert.That(budget.TryConsume(1, 0), Is.True);
        }
    }
}
