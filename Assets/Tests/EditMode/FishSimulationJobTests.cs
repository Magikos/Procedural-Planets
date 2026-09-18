using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class FishSimulationJobTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ParallelSchoolsMatchManagedSimulation(bool fleeing, bool nearBoundary)
        {
            using var levelsOwner = new NativeArray<float>(6, Allocator.TempJob); var levels = levelsOwner;
            using var bodiesOwner = new NativeArray<ushort>(6, Allocator.TempJob); var bodies = bodiesOwner;
            using var kindsOwner = new NativeArray<byte>(2, Allocator.TempJob); var kinds = kindsOwner;
            using var noiseOwner = new NativeArray<NoiseFilterData>(0, Allocator.TempJob); var noise = noiseOwner;
            using var cellsOwner = new NativeArray<byte>(0, Allocator.TempJob); var cells = cellsOwner;
            using var rivers = new RiverField(new System.Collections.Generic.List<RiverSegment>(), 100f);
            for (int i = 0; i < 6; i++) { levels[i] = .05f; bodies[i] = 1; }
            kinds[1] = (byte)WaterBodyKind.Ocean;
            var water = new WaterQueryJobData
            {
                Levels = levels, Bodies = bodies, Kinds = kinds, Resolution = 1,
                PlanetRadius = 100f, WorldScale = 1f, Rotation = Quaternion.identity,
                LocalToWorld = Matrix4x4.identity, WorldToLocal = Matrix4x4.identity,
                Terrain = new SurfaceQueryJobData { Noise = noise, DiagnosticCells = cells, PlanetRadius = 100f, Rivers = rivers.Data }
            };
            using var agentsOwner = new NativeArray<FishAgentState>(4, Allocator.TempJob); var agents = agentsOwner;
            using var schoolsOwner = new NativeArray<FishJobSchool>(2, Allocator.TempJob); var schools = schoolsOwner;
            using var threatsOwner = new NativeArray<FishThreatSnapshot>(4, Allocator.TempJob); var threats = threatsOwner;
            var managed = new FishSchool[2];
            var registry = new ThreatRegistry();
            Vector3 threat = new(0f, 106f, -2f);
            if (fleeing) registry.Report(ThreatRegistry.LocalPlayer, threat, CreatureFaction.Player);
            for (int school = 0; school < 2; school++)
            {
                float height = nearBoundary ? 100.30002f : 103f;
                var positions = new[] { new Vector3(school * 2f, height, 0f), new Vector3(school * 2f + .8f, height, 0f) };
                managed[school] = new FishSchool(water, positions, Vector3.forward, FishSpecies.Coastal);
                schools[school] = new FishJobSchool { Offset = school * 2, Count = 2,
                    State = new FishSchoolState { Body = 1, Heading = Vector3.forward, Rules = FishRules.From(FishSpecies.Coastal), Viable = true } };
                for (int j = 0; j < 2; j++)
                {
                    water.TryGetWaterSurface(positions[j], out var sample);
                    agents[school * 2 + j] = new FishAgentState { Position = positions[j], Heading = Vector3.forward, Normal = sample.Normal, Depth = sample.SignedDepth };
                    threats[school * 2 + j] = new FishThreatSnapshot { Found = fleeing, Position = threat };
                }
            }
            for (int frame = 0; frame < 30; frame++)
            {
                var job = new FishSimulationJob { Schools = schools, Agents = agents, Threats = threats, Water = water, DeltaTime = 1f / 30f };
                job.Schedule(2, 1).Complete();
                for (int i = 0; i < 2; i++)
                {
                    if (nearBoundary && i == 0)
                    {
                        Assert.That(schools[i].State.Viable, Is.False, "The job must reserve clearance for float rounding at the bed.");
                        continue;
                    }
                    managed[i].Tick(1f / 30f, registry, 100);
                    Assert.That(schools[i].State.Viable, Is.True);
                    for (int j = 0; j < 2; j++)
                    {
                        Assert.That(Vector3.Distance(agents[i * 2 + j].Position, managed[i].Positions[j]), Is.LessThan(.002f));
                        Assert.That(FishMovement.IsHabitat(water, agents[i * 2 + j].Position, 1, FishSpecies.Coastal.Clearance, FishSpecies.Coastal), Is.True);
                    }
                }
            }
        }
    }
}

