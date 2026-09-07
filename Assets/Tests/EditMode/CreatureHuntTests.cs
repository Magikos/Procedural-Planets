using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureHuntTests
    {
        static CreatureSenses Senses(float distance = 1f) => new()
        {
            Up = Vector3.up, Forward = Vector3.forward, HasPrey = true, PreyPosition = Vector3.forward * distance,
            Needs = new ActorNeeds(0.8d, 0d),
            DeltaTime = .1f, AttackDuration = 1f, AttackHitTime = .25f
        };

        [Test]
        public void HuntProgressesAndMarkerFiresOnceAcrossLargeTick()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = Senses(6);
            brain.Observe(senses); brain.Sample(0);
            Assert.AreEqual(CreatureBehaviour.Stalk, brain.Behaviour);
            senses.PreyAlert = true;
            brain.Observe(senses); brain.Sample(1);
            Assert.AreEqual(CreatureBehaviour.Chase, brain.Behaviour);
            senses.PreyPosition = Vector3.forward;
            brain.Observe(senses); brain.Sample(2);
            Assert.AreEqual(CreatureBehaviour.Attack, brain.Behaviour);
            Assert.IsFalse(brain.HitRequested);
            senses.DeltaTime = 1.5f;
            brain.Observe(senses); brain.Sample(3);
            Assert.IsTrue(brain.HitRequested);
            brain.Sample(4);
            Assert.IsFalse(brain.HitRequested);
            Assert.AreEqual(CreatureBehaviour.Recover, brain.Behaviour);
            Assert.AreEqual(1u, brain.AttackSequence);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AttackCancelsOnTargetLossOrThreat(bool threat)
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Attack);
            var senses = Senses();
            senses.HasPrey = threat;
            senses.HasThreat = threat;
            senses.DeltaTime = 2f;
            brain.Observe(senses); brain.Sample(0);
            Assert.IsFalse(brain.HitRequested);
            Assert.AreNotEqual(CreatureBehaviour.Attack, brain.Behaviour);
        }

        [Test]
        public void BiteRequiresCurrentRangeAndFacing()
        {
            Assert.IsTrue(CreatureHunt.CanBite(Vector3.zero, Vector3.forward, Vector3.up, Vector3.forward));
            Assert.IsFalse(CreatureHunt.CanBite(Vector3.zero, Vector3.forward, Vector3.up, Vector3.back));
            Assert.IsFalse(CreatureHunt.CanBite(Vector3.zero, Vector3.forward, Vector3.up, Vector3.forward * 2));
        }

        [Test]
        public void LongChaseGivesUpWithoutAttacking()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = Senses(8);
            senses.PreyAlert = true;
            senses.DeltaTime = 1f;
            for (uint tick = 0; tick < 22; tick++)
            {
                brain.Observe(senses); brain.Sample(tick);
                Assert.IsFalse(brain.HitRequested);
            }
            Assert.AreEqual(CreatureBehaviour.Wander, brain.Behaviour);
            Assert.AreEqual(0u, brain.AttackSequence);
        }
    }
}
