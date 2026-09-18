using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Unity.Mathematics;

namespace ProceduralPlanets.Tests
{
    public sealed class RiverReachSmoothingTests
    {
        [Test]
        public void WaterfallLipAndReceivingRunKeepTheirOriginalSupport()
        {
            var segments = Zigzag();
            var waterfall = segments[10];
            waterfall.Shape.w = 1f;
            segments[10] = waterfall;
            var lip = segments[9];
            var receiving = segments[11];

            RiverGenerator.RoundBends(segments);

            Assert.That(segments[9], Is.EqualTo(lip));
            Assert.That(segments[10], Is.EqualTo(waterfall));
            Assert.That(segments[11], Is.EqualTo(receiving));
            Assert.That(segments[9].B, Is.EqualTo(segments[10].A));
            Assert.That(segments[10].B, Is.EqualTo(segments[11].A));
        }

        [Test]
        public void RepeatedGridZigzagsLoseTurningWithoutExcessiveSegments()
        {
            var original = Zigzag();
            var smoothed = Smooth(original);

            Assert.That(smoothed.Count, Is.LessThanOrEqualTo(original.Count * 2));
            Assert.That(TotalTurn(smoothed), Is.LessThan(TotalTurn(original) * 0.35));
        }

        [Test]
        public void SmoothedReachPreservesEndpointsContinuityAndDownstreamProfile()
        {
            var original = Zigzag();
            var smoothed = Smooth(original);

            Assert.That(smoothed[0].A, Is.EqualTo(original[0].A));
            Assert.That(smoothed[^1].B, Is.EqualTo(original[^1].B));
            foreach (var segment in smoothed)
            {
                Assert.That(segment.Flow.y, Is.LessThanOrEqualTo(segment.Flow.z));
                Assert.That(segment.A.w, Is.GreaterThanOrEqualTo(segment.B.w));
                Assert.That(math.length(segment.A.xyz), Is.EqualTo(1f).Within(1e-6f));
                Assert.That(math.length(segment.B.xyz), Is.EqualTo(1f).Within(1e-6f));
            }
            for (int i = 1; i < smoothed.Count; i++)
            {
                Assert.That(smoothed[i - 1].B, Is.EqualTo(smoothed[i].A));
                Assert.That(smoothed[i - 1].Flow.z, Is.EqualTo(smoothed[i].Flow.y));
            }
        }

        [Test]
        public void RepeatedAndReorderedInputProduceIdenticalSegments()
        {
            var original = Zigzag();
            var expected = Smooth(original);
            CollectionAssert.AreEqual(expected, Smooth(original));

            original.Reverse();
            CollectionAssert.AreEqual(expected, Smooth(original));
        }

        [Test]
        public void JunctionsAndEntireWaterfallSegmentsStayPinned()
        {
            var segments = Zigzag();
            var waterfall = segments[10];
            waterfall.Shape.w = 1f;
            segments[10] = waterfall;
            var branch = segments[5];
            branch.A.xyz = Direction(-1f, 3f);
            int branchIndex = segments.Count;
            segments.Add(branch);
            float4 junction = segments[5].B;

            RiverGenerator.RoundBends(segments);

            Assert.That(segments[10], Is.EqualTo(waterfall));
            Assert.That(segments[5].B, Is.EqualTo(junction));
            Assert.That(segments[branchIndex].B, Is.EqualTo(junction));
            Assert.That(segments[6].A, Is.EqualTo(junction));
        }

        [Test]
        public void CancelledSmoothingLeavesInputUnchanged()
        {
            var segments = Zigzag();
            var original = segments.ToArray();

            Assert.Throws<OperationCanceledException>(() =>
                RiverGenerator.RoundBends(segments, new CancellationToken(true)));

            CollectionAssert.AreEqual(original, segments);
        }

        [Test]
        public void ShortRightAngleBendMeetsTangentTurnBudget()
        {
            var bend = Smooth(Zigzag().Take(2));

            Assert.That(bend.Count, Is.InRange(3, 14));
            for (int i = 1; i < bend.Count; i++)
                Assert.That(math.degrees(Turn(bend[i - 1], bend[i])), Is.LessThanOrEqualTo(15.1f));
        }

        [Test]
        public void ShortRightAngleBendMeetsMidpointChordErrorBudget()
        {
            var bend = Smooth(Zigzag().Take(2));
            Assert.That(bend.Count, Is.InRange(3, 14));

            // Recover the quadratic control from the two retained endpoint tangent lines.
            float2 origin = Project(bend[0].A.xyz);
            float2 direction = Project(bend[0].B.xyz) - origin;
            float2 tail = Project(bend[^1].A.xyz);
            float2 tangent = Project(bend[^1].B.xyz) - tail;
            float denominator = Cross(direction, tangent);
            Assert.That(math.abs(denominator), Is.GreaterThan(1e-12f));
            float2 control2 = origin + direction * (Cross(tail - origin, tangent) / denominator);
            float3 control = math.normalize(new float3(control2.x, 1f, control2.y));
            float3 start = bend[0].B.xyz;
            float3 end = bend[^1].A.xyz;
            float flowStart = bend[0].Flow.z;
            float flowEnd = bend[^1].Flow.y;
            Assert.That(flowEnd, Is.GreaterThan(flowStart));

            for (int i = 1; i < bend.Count - 1; i++)
            {
                float t = ((bend[i].Flow.y + bend[i].Flow.z) * 0.5f - flowStart) / (flowEnd - flowStart);
                float3 left = math.normalize(math.lerp(start, control, t));
                float3 right = math.normalize(math.lerp(control, end, t));
                float3 curveMidpoint = math.normalize(math.lerp(left, right, t));
                float3 chordMidpoint = math.normalize((bend[i].A.xyz + bend[i].B.xyz) * 0.5f);
                float errorMetres = math.distance(curveMidpoint, chordMidpoint) * 5100f;
                // Half-width is 5 metres: 5% chord budget plus 1 mm floating-point allowance.
                Assert.That(errorMetres, Is.LessThanOrEqualTo(0.251f));
            }
        }

        static List<RiverSegment> Smooth(IEnumerable<RiverSegment> source)
        {
            var segments = source.ToList();
            RiverGenerator.RoundBends(segments);
            return segments.OrderBy(segment => segment.Flow.y).ToList();
        }

        static float3 Direction(float x, float z) => math.normalize(new float3(x * 0.002f, 1f, z * 0.002f));

        static List<RiverSegment> Zigzag()
        {
            var segments = new List<RiverSegment>();
            for (int i = 0; i < 24; i++)
            {
                // Integer division alternates steps along the two grid axes.
                segments.Add(new RiverSegment
                {
                    A = new float4(Direction(i / 2, (i + 1) / 2), 5100f - i),
                    B = new float4(Direction((i + 1) / 2, (i + 2) / 2), 5099f - i),
                    Shape = new float4(5f, 2f, 1f, 0f),
                    Flow = new float4(1f, i, i + 1, 9f)
                });
            }
            return segments;
        }

        static float Turn(RiverSegment before, RiverSegment after)
        {
            float3 incoming = math.normalize(before.B.xyz - before.A.xyz);
            float3 outgoing = math.normalize(after.B.xyz - after.A.xyz);
            return math.acos(math.clamp(math.dot(incoming, outgoing), -1f, 1f));
        }

        static double TotalTurn(List<RiverSegment> segments)
        {
            double total = 0;
            for (int i = 1; i < segments.Count; i++) total += Turn(segments[i - 1], segments[i]);
            return total;
        }

        static float2 Project(float3 point) => new float2(point.x, point.z) / point.y;
        static float Cross(float2 left, float2 right) => left.x * right.y - left.y * right.x;
    }
}

