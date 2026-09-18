using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureCarrionTests
    {
        static CreatureSpeciesDto Vulture => CreatureLibraryDto.Placeholder.At(2) with { Scavenger = true };

        [Test]
        public void CarrionUsesNearestIdentityAndIgnoresBonesButNotLootedHide()
        {
            var store = new CreatureCorpseStore();
            store.Record(0, Vector3.right, Quaternion.identity, 0);
            EntityId fresh = store.Record(0, Vector3.right * 5f, Quaternion.identity, 86400);
            store.MarkLooted(fresh);
            var target = CreatureCarrion.Find(store, Vector3.zero, 20f, 86400);
            Assert.AreEqual(fresh, target.Id);
            Assert.IsTrue(target.Available);
            Assert.IsFalse(CreatureCarrion.Find(store, Vector3.zero, 4f, 86400).Available);
            Assert.IsFalse(CreatureCarrion.Find(store, Vector3.zero, 20f, 172800).Available);
        }

        [Test]
        public void ThreatOverridesCircleAndMissingCarcassEndsCircle()
        {
            var senses = new CreatureSenses { Species = Vulture, Position = Vector3.right * 20f,
                Up = Vector3.up, Forward = Vector3.forward, DeltaTime = 1f / 60f,
                Carrion = new CreatureResourceTarget { Available = true, Position = Vector3.zero } };
            var brain = new CreatureBrain(7, Vulture, CreatureBehaviour.Wander);
            brain.Observe(senses);
            brain.Sample(0);
            Assert.AreEqual(CreatureBehaviour.Circle, brain.Behaviour);
            senses.HasThreat = true;
            senses.ThreatPosition = Vector3.right * 19f;
            brain.Observe(senses);
            brain.Sample(1);
            Assert.AreEqual(CreatureBehaviour.Flee, brain.Behaviour);
            senses.HasThreat = false;
            senses.Carrion = default;
            for (uint i = 2; i < 200; i++) { brain.Observe(senses); brain.Sample(i); }
            Assert.AreEqual(CreatureBehaviour.Wander, brain.Behaviour);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void CircleConvergesFromOutsideAndContinuesFlying(int fps)
        {
            var brain = new CreatureBrain(7, Vulture, CreatureBehaviour.Wander);
            Vector3 center = Vector3.zero;
            var senses = new CreatureSenses { Species = Vulture, Position = new Vector3(60, 1000, 0),
                Forward = Vector3.forward, DeltaTime = 1f / fps,
                Carrion = new CreatureResourceTarget { Available = true, Position = Vector3.up * 1000f } };
            float travelled = 0f;
            for (uint i = 0; i < fps * 60; i++)
            {
                senses.Up = (senses.Position - center).normalized;
                brain.Observe(senses);
                var intent = brain.Sample(i);
                senses.Forward = Vector3.ProjectOnPlane(Quaternion.AngleAxis(intent.Look.x / fps,
                    senses.Up) * senses.Forward, senses.Up).normalized;
                Vector3 previous = senses.Position;
                senses.Position = (senses.Position + senses.Forward * (Vulture.WalkSpeedMps * intent.Move.y / fps)).normalized * 1000f;
                travelled += Vector3.Distance(previous, senses.Position);
            }
            float radius = Vector3.ProjectOnPlane(senses.Position - senses.Carrion.Position, Vector3.up).magnitude;
            Assert.That(radius, Is.InRange(12f, 30f));
            Assert.Greater(travelled, 250f);
            Assert.AreEqual(CreatureBehaviour.Circle, brain.Behaviour);
        }
    }
}
