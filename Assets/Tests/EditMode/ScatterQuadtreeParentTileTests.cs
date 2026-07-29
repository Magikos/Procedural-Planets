using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // ParentTile is the fixed partition the incremental scatter gather caches by: every candidate cell at
    // (level, x, y) maps to exactly one coarser tile, and every child of a tile maps back to it. A break
    // here would double-draw or drop props at a tile seam, so the partition is locked under test.
    public sealed class ScatterQuadtreeParentTileTests
    {
        [Test]
        public void ParentTile_IsRightShiftByLevelDelta()
        {
            Assert.AreEqual(new Vector2Int(1234567 >> 5, 7654321 >> 5),
                ScatterQuadtree.ParentTile(12, 1234567, 7654321, 7));
        }

        [Test]
        public void ParentTile_EqualLevels_IsIdentity()
        {
            Assert.AreEqual(new Vector2Int(40, 25), ScatterQuadtree.ParentTile(7, 40, 25, 7));
        }

        [Test]
        public void ParentTile_AllChildrenOfATileMapBackToIt()
        {
            const int tileLevel = 7, level = 11; // shift 4 -> 16x16 children per tile
            int shift = level - tileLevel, span = 1 << shift;
            var tiles = new[] { new Vector2Int(0, 0), new Vector2Int(3, 9), new Vector2Int(63, 40) };
            foreach (var t in tiles)
            {
                int x0 = t.x << shift, y0 = t.y << shift;
                for (int dy = 0; dy < span; dy++)
                for (int dx = 0; dx < span; dx++)
                    Assert.AreEqual(t, ScatterQuadtree.ParentTile(level, x0 + dx, y0 + dy, tileLevel),
                        $"child ({x0 + dx},{y0 + dy}) must belong to tile {t}");
            }
        }

        [Test]
        public void ParentTile_AdjacentChildBlocksHaveDistinctParents()
        {
            const int tileLevel = 7, level = 10;
            int shift = level - tileLevel, span = 1 << shift; // 8
            var a = ScatterQuadtree.ParentTile(level, (5 << shift) + span - 1, 5 << shift, tileLevel);
            var b = ScatterQuadtree.ParentTile(level, 6 << shift, 5 << shift, tileLevel);
            Assert.AreEqual(new Vector2Int(5, 5), a);
            Assert.AreEqual(new Vector2Int(6, 5), b);
        }
    }
}
