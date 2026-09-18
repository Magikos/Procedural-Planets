using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureNavigationTests
    {
        [Test]
        public void UnreachableAttackerForcesRetreatInsteadOfAnEmergencyCounterattack()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var id = new EntityId(EntityId.HostOwner, 10);
            var senses = new CreatureSenses { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = .1f,
                HasPrey = true, PreyId = id, PreyPosition = Vector3.forward * 3,
                HasThreat = true, ThreatId = id, ThreatPosition = Vector3.forward * 3, ThreatDistance = 3,
                HasDisposition = true, CanDefend = true, Confidence = 1, Courage = 1,
                PreyUnreachable = true, ThreatUnreachable = true, Needs = new ActorNeeds(.9, 0) };
            for (uint tick = 0; tick < 100; tick++)
            {
                brain.Observe(senses); brain.Sample(tick);
                Assert.AreEqual(CreatureObjective.Escape, brain.Objective);
                Assert.IsFalse(brain.HitRequested);
            }
        }

        [Test]
        public void NavigationFailureRejectsOnlyTheCurrentResource()
        {
            var brain = new CreatureBrain(1, null, CreatureBehaviour.Wander);
            var senses = new CreatureSenses { Up = Vector3.up, Forward = Vector3.forward, DeltaTime = .1f,
                Needs = new ActorNeeds(.9, 0), Food = new CreatureResourceTarget {
                    Id = new EntityId(EntityId.HostOwner, 10), Available = true, Position = Vector3.forward } };
            brain.Observe(senses); brain.Sample(0); brain.ReportNavigationFailure();
            brain.Observe(senses); brain.Sample(1);
            Assert.AreNotEqual(CreatureObjective.FindFood, brain.Objective);
            senses.Food.Id = new EntityId(EntityId.HostOwner, 11); senses.DeltaTime = 3f;
            brain.Observe(senses); brain.Sample(2);
            Assert.AreEqual(CreatureObjective.FindFood, brain.Objective);
        }

        [Test]
        public void RoutesAroundBarrierRejectsElevationAndStopsWhenStalled()
        {
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = .65f; settings.agentHeight = 2; settings.agentClimb = .3f;
            var sources = new List<NavMeshBuildSource> {
                new() { shape = NavMeshBuildSourceShape.Box, transform = Matrix4x4.Translate(new Vector3(200, -.5f, 200)), size = new Vector3(30, 1, 30), area = 0 },
                new() { shape = NavMeshBuildSourceShape.Box, transform = Matrix4x4.Translate(new Vector3(200, 1.5f, 200)), size = new Vector3(2, 3, 8), area = 1 },
                new() { shape = NavMeshBuildSourceShape.Box, transform = Matrix4x4.Translate(new Vector3(225, -.5f, 200)), size = new Vector3(4, 1, 4), area = 0 } };
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(new Vector3(207, 0, 200), new Vector3(50, 10, 34)), Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(data);
            var instance = NavMesh.AddNavMeshData(data);
            try
            {
                var route = new CreatureNavigation(); var start = new Vector3(195, 0, 200); var goal = new Vector3(205, 0, 200);
                Assert.IsTrue(route.Steer(start, goal, .1f, out var direction));
                Assert.Greater(Mathf.Abs(direction.z), .1f);
                Assert.IsFalse(CreatureNavigation.ClearContact(start, goal));
                Assert.IsTrue(route.TryApproach(start, goal + Vector3.forward, goal, out _));
                Assert.IsFalse(route.TryApproach(start, goal + Vector3.up * 3, goal, out var fallback));
                Assert.AreEqual(goal, fallback); Assert.IsFalse(route.Failed);
                var island = new Vector3(225, 0, 200);
                Assert.IsTrue(CreatureNavigation.TryPoint(island, out _));
                Assert.IsFalse(route.TryApproach(start, island, goal, out fallback));
                Assert.AreEqual(goal, fallback); Assert.IsFalse(route.Failed);
                var constrained = CreatureNavigation.Constrain(start, goal);
                Assert.Less(constrained.x, 200);
                for (int i = 0; i < 45; i++) route.Steer(start, goal, .1f, out _);
                Assert.IsTrue(route.Failed); Assert.AreEqual("No progress", route.Status);
                // Start against the long face, with the threat behind and the desired escape through the wall.
                route.Reset();
                var escapePosition = new Vector3(198.25f, 0, 200);
                var escapeStart = escapePosition; var threat = new Vector3(196.25f, 0, 200);
                Quaternion rotation = Quaternion.LookRotation(Vector3.right);
                for (int i = 0; i < 120; i++)
                {
                    Assert.IsTrue(route.Escape(escapePosition, threat, rotation * Vector3.forward, .05f, out var escapeDirection), route.Status);
                    float bearing = CharacterMath.TangentBearing(escapePosition, rotation * Vector3.forward, Vector3.up, escapePosition + escapeDirection);
                    rotation = Quaternion.AngleAxis(Mathf.Clamp(bearing * 5f, -220f, 220f) * .05f, Vector3.up) * rotation;
                    float throttle = Mathf.Clamp01(1 - Mathf.Abs(bearing) / 100f);
                    escapePosition = CreatureNavigation.Constrain(escapePosition, escapePosition + rotation * Vector3.forward * (.3f * throttle));
                    Assert.IsTrue(CreatureNavigation.TryPoint(escapePosition, out _));
                    if (Vector3.Distance(escapePosition, threat) > 10) break;
                }
                Assert.Greater(Vector3.Distance(escapePosition, escapeStart), 5f);
                Assert.Greater(Vector3.Distance(escapePosition, threat), 8f);
                route.Reset(); Assert.IsFalse(route.Steer(start, goal + Vector3.up * 3, .1f, out _));
                Assert.AreEqual("Unreachable", route.Status);
            }
            finally { instance.Remove(); Object.DestroyImmediate(data); }
        }
    }
}
