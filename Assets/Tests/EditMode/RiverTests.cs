using System;
using System.Threading;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class RiverTests
    {
        [Test]
        public void CompiledQueriesMatchManagedReferenceAcrossBanksAndCubeSeams()
        {
            var segments = new System.Collections.Generic.List<RiverSegment>();
            for (int i = 0; i < 24; i++)
            {
                float x = .98f + i * .002f;
                segments.Add(new RiverSegment
                {
                    A = new float4(math.normalize(new float3(x, 1f, .005f * math.sin(i))), 5030f - i),
                    B = new float4(math.normalize(new float3(x + .002f, 1f, .005f * math.sin(i + 1))), 5029f - i),
                    Shape = new float4(5f, 2f, 3f, i == 12 ? 1f : 0f),
                    Flow = new float4(1f, i * 10f, (i + 1) * 10f, 9f),
                    Profile = new float4(6f, i % 3, i < 12 ? 1f : 2f, i == 11 ? 2f : i == 13 ? 1f : 0f)
                });
            }
            using var field = new RiverField(segments, 5000f);
            var data = field.Data;
            for (int i = 0; i < 1800; i++)
            {
                float3 dir = math.normalize(new float3(.97f + (i % 60) * .0015f, 1f, (i / 60 - 15) * .0008f));
                bool actual = data.Sample(dir, out var selected, out float t, out float distance);
                bool expected = data.SampleReference(dir, out var reference, out float rt, out float rd);
                Assert.That(actual, Is.EqualTo(expected), "wet classification at " + i);
                if (expected)
                {
                    Assert.That(selected.Radius(t), Is.EqualTo(reference.Radius(rt)).Within(.004f));
                    Assert.That(distance, Is.EqualTo(rd).Within(.002f));
                    Assert.That(selected.Flow.x, Is.EqualTo(reference.Flow.x));
                }
                Assert.That(data.Carve(dir, .008f), Is.EqualTo(data.CarveReference(dir, .008f)).Within(1e-6f));
            }
            Assert.That(default(RiverFieldData).Sample(float3.zero, out _, out _, out _), Is.False);
            Assert.That(default(RiverFieldData).Carve(float3.zero, -.01f), Is.EqualTo(-.01f));
        }

        [Test]
        public void WaterfallKeepsLipAndReceivingPoolAtSeparateLevels()
        {
            float4 P(float x, float radius) => new float4(math.normalize(new float3(x, 1f, 0f)), radius);
            var shape = new float4(10f, 2f, 1f, 0f);
            var flow = new float4(1f, 0f, 100f, 18f);
            var segments = new System.Collections.Generic.List<RiverSegment>
            {
                new() { A = P(-.01f, 5020f), B = P(0f, 5020f), Shape = shape, Flow = flow },
                new() { A = P(0f, 5020f), B = P(.001f, 5000f), Shape = new float4(10f, 2f, 8f, 1f), Flow = flow },
                new() { A = P(.001f, 5000f), B = P(.011f, 5000f), Shape = shape, Flow = flow }
            };
            RiverGenerator.ShapeWidths(segments, 5000f);
            using var field = new RiverField(segments, 5000f);
            Assert.That(segments[0].Profile.z, Is.Not.EqualTo(segments[2].Profile.z));
            Assert.That(field.Data.Sample(segments[1].A.xyz, out var lip, out float a, out _), Is.True);
            Assert.That(field.Data.Sample(segments[1].B.xyz, out var pool, out float b, out _), Is.True);
            Assert.That(lip.Radius(a), Is.EqualTo(5020f).Within(.002f));
            Assert.That(pool.Radius(b), Is.EqualTo(5000f).Within(.002f));
            Assert.That(5000f * (1f + field.Data.Carve(segments[1].A.xyz, .01f)), Is.EqualTo(5018f).Within(.003f));
        }

        [Test]
        public void OverlappingBendHasContinuousSurfaceHeight()
        {
            var corner = new float4(0f, 1f, 0f, 5005f);
            var shape = new float4(10f, 2f, 1f, 0f);
            var flow = new float4(1f, 0f, 100f, 18f);
            using var field = new RiverField(new[]
            {
                new RiverSegment { A = new float4(math.normalize(new float3(-.02f, 1f, 0f)), 5010f), B = corner, Shape = shape, Flow = flow },
                new RiverSegment { A = corner, B = new float4(math.normalize(new float3(0f, 1f, .02f)), 5000f), Shape = shape, Flow = flow }
            }, 5000f);
            Assert.That(field.Data.Sample(math.normalize(new float3(-.0002f - 1e-7f, 1f, .0002f)), out var a, out float ta, out _), Is.True);
            Assert.That(field.Data.Sample(math.normalize(new float3(-.0002f + 1e-7f, 1f, .0002f)), out var b, out float tb, out _), Is.True);
            Assert.That(math.abs(a.Radius(ta) - b.Radius(tb)), Is.LessThan(.005f));
        }

        [TestCase(0f)]
        [TestCase(.5f)]
        [TestCase(1f)]
        public void TaperAndPoolShareCarvedAndWetFootprints(float along)
        {
            var segment = new RiverSegment
            {
                A = new float4(math.normalize(new float3(-.02f, 1f, 0f)), 5000f),
                B = new float4(math.normalize(new float3(.02f, 1f, 0f)), 5000f),
                Shape = new float4(5f, 3f, 1f, 0f), Flow = new float4(1f, 0f, 200f, 9f),
                Profile = new float4(15f, 8f, 0f, 0f)
            };
            using var field = new RiverField(new[] { segment }, 5000f);
            float3 center = math.normalize(math.lerp(segment.A.xyz, segment.B.xyz, along));
            float width = segment.Width(along);
            float3 inside = math.normalize(center + new float3(0f, 0f, width * .8f / 5000f));
            float3 outside = math.normalize(center + new float3(0f, 0f, width * 1.2f / 5000f));
            Assert.That(field.Data.Sample(inside, out _, out _, out _), Is.True);
            Assert.That(field.Data.Sample(outside, out _, out _, out _), Is.False);
            Assert.That(field.Data.Carve(inside, .01f), Is.LessThan(0f));
        }

        [Test]
        public void RoundedBendPreservesEndpointsAndDownhillProfile()
        {
            var a = new float4(math.normalize(new float3(-.02f, 1f, 0f)), 5010f);
            var corner = new float4(0f, 1f, 0f, 5005f);
            var b = new float4(math.normalize(new float3(0f, 1f, .02f)), 5000f);
            var shape = new float4(5f, 2f, 1f, 0f);
            var segments = new System.Collections.Generic.List<RiverSegment>
            {
                new() { A = a, B = corner, Shape = shape, Flow = new float4(1f, -200f, -100f, 9f) },
                new() { A = corner, B = b, Shape = shape, Flow = new float4(1f, -100f, 0f, 9f) }
            };
            RiverGenerator.RoundBends(segments);
            Assert.That(segments.Count, Is.InRange(3, 14));
            Assert.That(segments[0].A, Is.EqualTo(a));
            Assert.That(segments[1].B, Is.EqualTo(b));
            Assert.That(segments[2].A, Is.EqualTo(segments[0].B));
            Assert.That(segments[segments.Count - 1].B, Is.EqualTo(segments[1].A));
            foreach (var segment in segments)
            {
                Assert.That(segment.A.w, Is.GreaterThanOrEqualTo(segment.B.w));
                Assert.That(segment.Flow.y, Is.LessThanOrEqualTo(segment.Flow.z));
                Assert.That(math.length(segment.A.xyz), Is.EqualTo(1f).Within(1e-6f));
            }
            using var field = new RiverField(segments, 5000f);
            Assert.That(field.Data.Sample(segments[2].B.xyz, out _, out _, out _), Is.True);
        }

        static int[] Line(int count)
        {
            var neighbors = new int[count * 4];
            Array.Fill(neighbors, -1);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) neighbors[i * 4] = i - 1;
                if (i + 1 < count) neighbors[i * 4 + 1] = i + 1;
            }
            return neighbors;
        }

        [Test]
        public void DepressionAndFlatDrainWithoutCycles()
        {
            var elevation = new[] { 0f, 5f, 1f, 1f, 2f, 8f };
            var seeds = new[] { true, false, false, false, false, false };
            var result = WaterSpillSolver.SolveDrainage(elevation, Line(6), seeds, 0f);
            CollectionAssert.AreEqual(new[] { 0f, 5f, 5f, 5f, 5f, 8f }, result.Filled);
            for (int i = 1; i < 6; i++) Assert.That(result.Receiver[i], Is.EqualTo(i - 1));
            var repeated = WaterSpillSolver.SolveDrainage(elevation, Line(6), seeds, 0f);
            CollectionAssert.AreEqual(result.Receiver, repeated.Receiver);
            CollectionAssert.AreEqual(result.Order, repeated.Order);
        }

        [Test]
        public void EqualOutletsAreStableAndNeverAcquireAReceiver()
        {
            var result = WaterSpillSolver.SolveDrainage(new[] { 0f, 2f, 2f, 0f }, Line(4), new[] { true, false, false, true }, 0f);
            Assert.That(result.Receiver[0], Is.EqualTo(-1));
            Assert.That(result.Receiver[3], Is.EqualTo(-1));
            Assert.That(result.Receiver[1], Is.EqualTo(0));
            Assert.That(result.Receiver[2], Is.EqualTo(3));
        }

        [Test]
        public void MissingOutletsAndCancellationAreExplicit()
        {
            var result = WaterSpillSolver.SolveDrainage(new[] { 1f, 2f }, Line(2), new bool[2], 0f);
            Assert.That(result.Order, Is.Empty);
            CollectionAssert.AreEqual(new[] { -1, -1 }, result.Receiver);
            Assert.Throws<OperationCanceledException>(() => WaterSpillSolver.SolveDrainage(new[] { 1f }, Line(1), new[] { true }, 0f, new CancellationToken(true)));
            Assert.Throws<ArgumentException>(() => WaterSpillSolver.SolveDrainage(new[] { float.NaN }, Line(1), new[] { true }, 0f));
        }

        [TestCase(0f)]
        [TestCase(45f)]
        [TestCase(90f)]
        public void ChannelFootprintAndBedAgreeAcrossFaces(float longitude)
        {
            Quaternion rotation = Quaternion.Euler(0f, longitude, 0f);
            Vector3 a = rotation * new Vector3(-.012f, .7f, .7f).normalized;
            Vector3 b = rotation * new Vector3(.012f, .7f, .7f).normalized;
            var segment = new RiverSegment
            {
                A = new float4(a.x, a.y, a.z, 5010f), B = new float4(b.x, b.y, b.z, 5008f),
                Shape = new float4(6f, 2f, 3f, 0f), Flow = new float4(42f, 0f, 100f, 12f)
            };
            using var field = new RiverField(new[] { segment }, 5000f);
            Vector3 center = (a + b).normalized;
            Assert.That(field.Data.Sample(center, out var sample, out float t, out float distance), Is.True);
            Assert.That(t, Is.EqualTo(.5f).Within(.001f));
            Assert.That(distance, Is.LessThan(.01f));
            float bed = (1f + field.Data.Carve(center, .01f)) * 5000f;
            Assert.That(bed, Is.EqualTo(sample.Radius(t) - 2f).Within(.002f));
            Vector3 side = Vector3.Cross(b - a, center).normalized;
            Vector3 dry = (center + side * (20f / 5000f)).normalized;
            Assert.That(field.Data.Sample(dry, out _, out _, out _), Is.False);
            Assert.That(field.Data.Carve(dry, .01f), Is.EqualTo(.01f).Within(.000001f));
        }

        [Test]
        public void JunctionCarvingUsesUnionInsteadOfAddingDepths()
        {
            var s = new RiverSegment
            {
                A = new float4(0, 1, 0, 5000), B = new float4(math.normalize(new float3(.01f, 1, 0)), 5000),
                Shape = new float4(5, 2, 1, 0), Flow = new float4(1, 0, 50, 10)
            };
            using var field = new RiverField(new[] { s, s }, 5000f);
            Assert.That((field.Data.Carve(new float3(0, 1, 0), 0f) + 1f) * 5000f, Is.EqualTo(4998f).Within(.001f));
        }

        [Test]
        public void RiverQueryReportsCurrentAndRejectsFallingSheetVolume()
        {
            var s = new RiverSegment
            {
                A = new float4(0, 1, 0, 5000), B = new float4(math.normalize(new float3(.01f, 1, 0)), 4998),
                Shape = new float4(5, 2, 3, 0), Flow = new float4(42, 0, 50, 10)
            };
            var owner = new GameObject("River query test");
            try
            {
                using var field = new RiverField(new[] { s }, 5000f);
                var query = new WaterQueryService(owner.transform);
                query.Configure(new WaterBodyMap(), null, 5000f, 0f, .15f, field.Data);
                Vector3 middle = math.normalize(s.A.xyz + s.B.xyz);
                Assert.That(query.TryGetWaterSurface(middle * 4998f, out var sample), Is.True);
                Assert.That(sample.BodyId, Is.EqualTo(42));
                Assert.That(sample.Velocity.magnitude, Is.EqualTo(3f).Within(.001f));
                Assert.That(sample.SignedDepth, Is.GreaterThan(0f));
                Assert.That(query.TryGetWaterSurface(new Vector3(float.NaN, 0, 0), out _), Is.False);
                s.Shape.w = 1;
                using var sheet = new RiverField(new[] { s }, 5000f);
                query.Configure(new WaterBodyMap(), null, 5000f, 0f, .15f, sheet.Data);
                Assert.That(query.TryGetWaterSurface(middle * 4998f, out _), Is.False);
                query.Reset();
                Assert.That(query.TryGetWaterSurface(middle * 4998f, out _), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void RiverCommandRejectsMissingPlanetThroughExecutor()
        {
            var previous = ConsoleRegistry.GetInstance(typeof(RiverCommands));
            using var commands = new RiverCommands(null);
            try
            {
                Assert.That(CommandExecutor.ExecuteImmediate("river.visit -1").Success, Is.False);
                Assert.That(CommandExecutor.ExecuteImmediate("river.status").Success, Is.False);
            }
            finally
            {
                commands.Dispose();
                if (previous is RiverCommands adapter) ConsoleRegistry.RegisterInstance(adapter);
            }
        }
    }
}
