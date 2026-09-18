using NUnit.Framework;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureSimulationDetailTests
    {
        [Test]
        public void DistanceHysteresisAvoidsRepeatedDetailChanges()
        {
            Assert.That(CreatureSimulationPolicy.Select(CreatureSimulationDetail.Full, 90f, true, false), Is.EqualTo(CreatureSimulationDetail.Full));
            Assert.That(CreatureSimulationPolicy.Select(CreatureSimulationDetail.Reduced, 90f, true, false), Is.EqualTo(CreatureSimulationDetail.Reduced));
            Assert.That(CreatureSimulationPolicy.Select(CreatureSimulationDetail.Reduced, 79f, true, false), Is.EqualTo(CreatureSimulationDetail.Full));
            Assert.That(CreatureSimulationPolicy.Select(CreatureSimulationDetail.Full, 101f, true, false), Is.EqualTo(CreatureSimulationDetail.Reduced));
        }

        [TestCase(CreatureBehaviour.Flee)]
        [TestCase(CreatureBehaviour.Chase)]
        [TestCase(CreatureBehaviour.Attack)]
        [TestCase(CreatureBehaviour.Feed)]
        public void RelevantActivityOverridesDistance(CreatureBehaviour behaviour)
        {
            Assert.That(CreatureSimulationPolicy.RequiresFullDetail(behaviour), Is.True);
            Assert.That(CreatureSimulationPolicy.Select(CreatureSimulationDetail.Reduced, 500f, true,
                CreatureSimulationPolicy.RequiresFullDetail(behaviour)), Is.EqualTo(CreatureSimulationDetail.Full));
        }

        [Test]
        public void AbsentResidentsUseCoarseStateAndReducedResidentsScanLessOften()
        {
            Assert.That(CreatureSimulationPolicy.Select(CreatureSimulationDetail.Full, 500f, false, false), Is.EqualTo(CreatureSimulationDetail.Coarse));
            Assert.That(CreatureSimulationPolicy.PerceptionInterval(CreatureSimulationDetail.Reduced),
                Is.GreaterThan(CreatureSimulationPolicy.PerceptionInterval(CreatureSimulationDetail.Full)));
        }
    }
}
