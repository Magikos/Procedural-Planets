using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // ScatterDrawBuckets removes a departed tile's instances by swap-remove (O(its instances)) instead of
    // rebuilding the whole per-prototype bucket. Swap-remove reorders and repoints moved slots, so the risk
    // is a bookkeeping bug that drops, duplicates, or unpairs an instance. Each instance encodes a unique id
    // in BOTH its matrix (m03) and its position (x); after any sequence of adds/removes the bucket's id
    // multiset must equal the union of resident tiles, and matrices[i] must stay paired with positions[i].
    public sealed class ScatterDrawBucketsTests
    {
        const int Protos = 3;

        [Test]
        public void TileCache_DropsWorkCommittedBetweenFilterAndPublish()
        {
            var previous = ConsoleRegistry.GetInstance(typeof(ScatterTileCache)) as ScatterTileCache;
            var cache = new ScatterTileCache(null, null);
            var type = typeof(ScatterTileCache);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            try
            {
                type.GetField("_protoCount", flags).SetValue(cache, 1);
                type.GetField("_buckets", flags).SetValue(cache, new ScatterDrawBuckets(1));
                var keyType = type.GetNestedType("WorkKey", System.Reflection.BindingFlags.NonPublic);
                object key = System.Activator.CreateInstance(keyType, new object[] { 1L, 0 });
                var next = (System.Collections.IList)type.GetField("_workNext", flags).GetValue(cache);
                var itemType = next.GetType().GetGenericArguments()[0];
                next.Add(System.Activator.CreateInstance(itemType, new[] { key, (object)1f }));
                var instances = new List<ScatterInstance> { new(42, Vector3.zero, Quaternion.identity, 1, 0) };
                var commit = type.GetMethod("Commit", flags);
                commit.Invoke(cache, new object[] { key, instances });
                type.GetMethod("ReplanPublish", flags).Invoke(cache, null);
                Assert.AreEqual(0, cache.PendingPairCount);
                commit.Invoke(cache, new object[] { key, instances });
                Assert.AreEqual(1, cache.LiveInstanceCount, "A repeated result must not append the tile twice.");
            }
            finally
            {
                cache.Dispose();
                if (previous != null) ConsoleRegistry.RegisterInstance(previous);
                else ConsoleRegistry.UnregisterInstance(typeof(ScatterTileCache));
            }
        }

        sealed class Model
        {
            // tileId -> per-proto list of instance ids
            public readonly Dictionary<long, List<int>[]> Tiles = new();
            public int NextId;
        }

        static void AddTile(ScatterDrawBuckets b, Model m, long tileId, int[] countPerProto)
        {
            var perProto = new List<int>[Protos];
            for (int p = 0; p < Protos; p++)
            {
                perProto[p] = new List<int>();
                for (int i = 0; i < countPerProto[p]; i++)
                {
                    int id = m.NextId++;
                    perProto[p].Add(id);
                    var mat = Matrix4x4.identity; mat.m03 = id;
                    b.Add(tileId, p, mat, new Vector3(id, 0f, 0f), (ulong)id);
                }
            }
            m.Tiles[tileId] = perProto;
        }

        static void Verify(ScatterDrawBuckets b, Model m)
        {
            int expectedTotal = 0;
            for (int p = 0; p < Protos; p++)
            {
                var matrices = b.Matrices(p);
                var positions = b.Positions(p);
                var ids = b.Ids(p);
                Assert.AreEqual(matrices.Count, positions.Count, $"proto {p}: matrix/position count mismatch");
                Assert.AreEqual(matrices.Count, ids.Count, $"proto {p}: matrix/id count mismatch");

                var actual = new List<int>(matrices.Count);
                for (int i = 0; i < matrices.Count; i++)
                {
                    int idM = Mathf.RoundToInt(matrices[i].m03);
                    int idP = Mathf.RoundToInt(positions[i].x);
                    int idB = (int)ids[i];
                    Assert.AreEqual(idM, idP, $"proto {p} slot {i}: matrix/position unpaired ({idM} vs {idP})");
                    Assert.AreEqual(idM, idB, $"proto {p} slot {i}: matrix/id unpaired ({idM} vs {idB})");
                    actual.Add(idM);
                }

                var expected = new List<int>();
                foreach (var kv in m.Tiles) expected.AddRange(kv.Value[p]);

                actual.Sort(); expected.Sort();
                CollectionAssert.AreEqual(expected, actual, $"proto {p}: id multiset mismatch");
                expectedTotal += expected.Count;
            }
            Assert.AreEqual(expectedTotal, b.InstanceCount, "InstanceCount mismatch");
        }

        [Test]
        public void RemoveMiddleTiles_LeavesUnionOfRemaining()
        {
            var b = new ScatterDrawBuckets(Protos);
            var m = new Model();
            AddTile(b, m, 100, new[] { 5, 0, 3 });
            AddTile(b, m, 200, new[] { 2, 7, 1 });
            AddTile(b, m, 300, new[] { 0, 4, 6 });
            AddTile(b, m, 400, new[] { 9, 1, 0 });
            Verify(b, m);

            b.RemoveTile(200); m.Tiles.Remove(200);
            Verify(b, m);
            b.RemoveTile(300); m.Tiles.Remove(300);
            Verify(b, m);
        }

        [Test]
        public void RemoveTailAndHeadTiles()
        {
            var b = new ScatterDrawBuckets(Protos);
            var m = new Model();
            AddTile(b, m, 1, new[] { 4, 4, 4 });
            AddTile(b, m, 2, new[] { 4, 4, 4 });
            AddTile(b, m, 3, new[] { 4, 4, 4 });
            b.RemoveTile(3); m.Tiles.Remove(3); // tail
            Verify(b, m);
            b.RemoveTile(1); m.Tiles.Remove(1); // head (its slots are now interior after prior swaps)
            Verify(b, m);
        }

        [Test]
        public void RemoveAll_LeavesEmpty()
        {
            var b = new ScatterDrawBuckets(Protos);
            var m = new Model();
            AddTile(b, m, 10, new[] { 3, 2, 5 });
            AddTile(b, m, 20, new[] { 1, 6, 2 });
            b.RemoveTile(10); m.Tiles.Remove(10);
            b.RemoveTile(20); m.Tiles.Remove(20);
            Verify(b, m);
            Assert.AreEqual(0, b.InstanceCount);
        }

        [Test]
        public void ReAddAfterRemove_IsConsistent()
        {
            var b = new ScatterDrawBuckets(Protos);
            var m = new Model();
            AddTile(b, m, 7, new[] { 3, 3, 3 });
            AddTile(b, m, 8, new[] { 2, 0, 4 });
            b.RemoveTile(7); m.Tiles.Remove(7);
            AddTile(b, m, 9, new[] { 5, 1, 0 });
            AddTile(b, m, 7, new[] { 1, 1, 1 }); // re-enter a previously-evicted tile id
            Verify(b, m);
            b.RemoveTile(8); m.Tiles.Remove(8);
            Verify(b, m);
        }

        [Test]
        public void RemoveUnknownTile_IsNoOp()
        {
            var b = new ScatterDrawBuckets(Protos);
            var m = new Model();
            AddTile(b, m, 1, new[] { 2, 2, 2 });
            b.RemoveTile(999);
            Verify(b, m);
        }

        [Test]
        public void RemoveInstanceById_RemovesOnlyThatInstance_KeepsInvariants()
        {
            var b = new ScatterDrawBuckets(Protos);
            var m = new Model();
            AddTile(b, m, 100, new[] { 5, 0, 3 });
            AddTile(b, m, 200, new[] { 2, 7, 1 });
            AddTile(b, m, 300, new[] { 0, 4, 6 });
            Verify(b, m);

            // Harvest one interior instance (proto 1, 4th of tile 200's seven).
            int victim = m.Tiles[200][1][3];
            Assert.IsTrue(b.RemoveInstanceById(1, (ulong)victim));
            m.Tiles[200][1].Remove(victim);
            Verify(b, m);

            // Removing a non-resident id is a no-op returning false.
            Assert.IsFalse(b.RemoveInstanceById(1, 999999UL));
            Verify(b, m);

            // The partially-harvested tile still evicts cleanly (tile-index bookkeeping intact).
            b.RemoveTile(200); m.Tiles.Remove(200);
            Verify(b, m);
        }
    }
}
