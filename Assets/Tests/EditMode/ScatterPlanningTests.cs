using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ScatterPlanningTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        sealed class Rig : IDisposable
        {
            readonly object _previous = ConsoleRegistry.GetInstance(typeof(ScatterTileCache));
            readonly GameObject _planet = new("scatter planner test");
            public readonly ScatterTileCache Cache = new(null, null);
            public readonly PlanetTransformSnapshot Snapshot;
            public readonly Vector3 Camera, Anchor;
            public readonly int[] Prototypes = { 0, 64, 129 };
            public readonly float[] Radii = { 60, 30, 10 };

            public Rig(Vector3 direction, bool transformed)
            {
                if (transformed)
                {
                    _planet.transform.position = new Vector3(200, -30, 45);
                    _planet.transform.rotation = Quaternion.Euler(21, 52, 7);
                    _planet.transform.localScale = Vector3.one * 1.5f;
                }
                Snapshot = PlanetTransformSnapshot.Capture(_planet.transform);
                Anchor = Snapshot.TransformPoint(direction.normalized * 100);
                Camera = Snapshot.TransformPoint(direction.normalized * 103);
                Set("_protoCount", 130);
                Set("_tileLevel", 4);
                Set("_globalMaxRadius", 60f);
                Set("_sortedProtoIndex", Prototypes);
                Set("_sortedProtoRadius", Radii);
                Set("_sortedProtoCount", 3);
                Set("_buckets", new ScatterDrawBuckets(130));
                Set("_replanCtx", new ScatterField.GatherContext(null, null, 0, 100, 100, false, null, 0));
                Set("_replanSnap", Snapshot);
                Set("_replanCameraPos", Camera);
                Set("_replanAnchor", Anchor);
            }

            public void Set(string name, object value) => typeof(ScatterTileCache).GetField(name, Private).SetValue(Cache, value);
            public object Get(string name) => typeof(ScatterTileCache).GetField(name, Private).GetValue(Cache);
            public object Call(string name, params object[] args) => typeof(ScatterTileCache).GetMethod(name, Private).Invoke(Cache, args);
            public object Key(long tile, int prototype) => Activator.CreateInstance(
                typeof(ScatterTileCache).GetNestedType("WorkKey", BindingFlags.NonPublic), tile, prototype);
            public void Commit(long tile, int prototype) => Call("Commit", Key(tile, prototype), new List<ScatterInstance>());

            public List<(long tile, int proto, float distance)> Work()
            {
                var result = new List<(long, int, float)>();
                foreach (object item in (IEnumerable)Get("_workNext"))
                {
                    object key = item.GetType().GetField("Item1").GetValue(item);
                    result.Add(((long)key.GetType().GetField("Tile").GetValue(key),
                        (int)key.GetType().GetField("Proto").GetValue(key),
                        (float)item.GetType().GetField("Item2").GetValue(item)));
                }
                return result;
            }

            public void Build()
            {
                Call("ReplanCandidates");
                Call("ReplanSortTiles");
                Call("ReplanFilter");
            }

            public void Dispose()
            {
                Cache.Dispose();
                UnityEngine.Object.DestroyImmediate(_planet);
                if (_previous != null) ConsoleRegistry.RegisterInstance(_previous);
                else ConsoleRegistry.UnregisterInstance(typeof(ScatterTileCache));
            }
        }

        [TestCase(0f, 1f, 0f, false)]
        [TestCase(1f, 1f, 0f, false)]
        [TestCase(1f, 1f, 1f, true)]
        public void Plan_PreservesRangeMembershipAndNearestFirstOrder(float x, float y, float z, bool transformed)
        {
            using var rig = new Rig(new Vector3(x, y, z), transformed);
            rig.Call("CapturePlan");
            rig.Build();
            var actual = rig.Work();
            var expected = new HashSet<(long, int)>();
            var ranges = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];
            int count = FaceSpaceCellRangeBuilder.BuildRangesLocal(rig.Camera, rig.Snapshot, 100, 60,
                ScatterQuadtree.CellUvWidth(4), 1, ranges).Count;
            for (int i = 0; i < count; i++)
            for (int dy = 0; dy < ranges[i].GridSize.y; dy++)
            for (int dx = 0; dx < ranges[i].GridSize.x; dx++)
            {
                var range = ranges[i];
                int tx = range.PageOriginCellUV.x + dx, ty = range.PageOriginCellUV.y + dy;
                if ((uint)tx >= 16 || (uint)ty >= 16) continue;
                var direction = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(range.FaceIndex,
                    new Vector2((tx + .5f) / 16, (ty + .5f) / 16));
                float distance = Vector3.Distance(rig.Snapshot.TransformPoint(direction * 100), rig.Anchor);
                long tile = ((long)range.FaceIndex << 14) | ((long)tx << 7) | (uint)ty;
                for (int p = 0; p < rig.Prototypes.Length; p++)
                    if (distance <= rig.Radii[p]) expected.Add((tile, rig.Prototypes[p]));
            }
            var found = new HashSet<(long, int)>();
            float previous = -1;
            foreach (var item in actual)
            {
                Assert.GreaterOrEqual(item.distance, previous);
                Assert.IsTrue(found.Add((item.tile, item.proto)), "Duplicate tile/prototype.");
                previous = item.distance;
            }
            Assert.IsNotEmpty(found);
            CollectionAssert.AreEquivalent(expected, found);
        }

        [Test]
        public void Snapshot_IsolatedFromCommitsAndReplacementPrototypeTables()
        {
            using var rig = new Rig(Vector3.up, false);
            rig.Call("CapturePlan");
            rig.Build();
            var initial = rig.Work();
            long tile = initial[0].tile;
            rig.Commit(tile, 64);
            rig.Call("CapturePlan");
            rig.Commit(tile, 0); // Must only affect publication, not the captured readiness bitset.
            rig.Set("_sortedProtoIndex", Array.Empty<int>());
            rig.Set("_sortedProtoRadius", Array.Empty<float>());
            rig.Set("_sortedProtoCount", 0);
            rig.Set("_tileLevel", 7);
            rig.Build();
            var result = rig.Work();
            Assert.IsFalse(result.Exists(v => v.tile == tile && v.proto == 64));
            Assert.IsTrue(result.Exists(v => v.tile == tile && v.proto == 0));
            Assert.AreEqual(initial.Count - 1, result.Count);
            rig.Call("ReplanPublish");
            Assert.AreEqual(initial.Count - 2, rig.Cache.PendingPairCount);
        }

        [Test]
        public void SnapshotAndPublication_ExcludeInFlightPairs()
        {
            using var rig = new Rig(Vector3.up, false);
            rig.Call("CapturePlan");
            rig.Build();
            var initial = rig.Work();
            var first = initial[0];
            object pending = rig.Get("_inFlight");
            pending.GetType().GetMethod("Add").Invoke(pending, new[] { rig.Key(first.tile, first.proto) });
            rig.Call("CapturePlan");
            rig.Build();
            Assert.AreEqual(initial.Count - 1, rig.Work().Count);
            var second = initial[1];
            pending.GetType().GetMethod("Add").Invoke(pending, new[] { rig.Key(second.tile, second.proto) });
            rig.Call("ReplanPublish");
            Assert.AreEqual(initial.Count, rig.Cache.PendingPairCount, "Each pending pair must count exactly once.");
        }
    }
}
