using NUnit.Framework;

namespace ProceduralPlanets.Tests
{
    // ScatterHash is the deterministic backbone of scatter placement: a gather's output must be a pure
    // function of (worldSeed, face, level, cell, slot), invariant to query order, and STABLE across
    // builds — a saved world's props are re-derived from these hashes, so changing a mix constant would
    // silently move every placed prop in every saved world. The golden-value tests pin the exact outputs
    // so that contract can't drift unnoticed; the rest assert determinism, range, and distinctness.
    public sealed class ScatterHashTests
    {
        // --- Golden values (regression guard on the persistence contract) ---
        // If any of these change, saved-world scatter placement changed. That is either a bug or a
        // deliberate, migration-requiring decision — never a silent edit.

        [Test]
        public void Mix_GoldenValues()
        {
            Assert.AreEqual(0u, ScatterHash.Mix(0u), "0 is a fixed point of Mix");
            Assert.AreEqual(1753845952u, ScatterHash.Mix(1u));
            Assert.AreEqual(3861431939u, ScatterHash.Mix(0xDEADBEEFu));
        }

        [Test]
        public void Node_GoldenValues()
        {
            Assert.AreEqual(1218994549u, ScatterHash.Node(0, 0, 0, 0, 0));
            Assert.AreEqual(2967262210u, ScatterHash.Node(12345, 3, 10, 1000, 2000));
        }

        [Test]
        public void Slot_GoldenValues()
        {
            uint node = ScatterHash.Node(12345, 3, 10, 1000, 2000);
            Assert.AreEqual(3183345152u, ScatterHash.Slot(node, 5));
            Assert.AreEqual(1369226050u, ScatterHash.Slot(node, 7));
            Assert.AreEqual(1388145687u, ScatterHash.Slot(node, 9));
        }

        // --- Determinism ---

        [Test]
        public void Node_IsDeterministic()
        {
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(ScatterHash.Node(42, 2, 8, i, i * 3),
                                ScatterHash.Node(42, 2, 8, i, i * 3));
        }

        [Test]
        public void Slot_IsDeterministic()
        {
            uint node = ScatterHash.Node(7, 1, 4, 5, 6);
            for (int s = 0; s < 64; s++)
                Assert.AreEqual(ScatterHash.Slot(node, s), ScatterHash.Slot(node, s));
        }

        // --- To01 range and masking ---

        [Test]
        public void To01_AlwaysInUnitIntervalExcludingOne()
        {
            uint x = 0x12345678u;
            for (int i = 0; i < 100000; i++)
            {
                x = ScatterHash.Mix(x + 0x9E3779B9u);
                float v = ScatterHash.To01(x);
                Assert.GreaterOrEqual(v, 0f);
                Assert.Less(v, 1f, "To01 must never reach 1.0 (accept test uses >= )");
            }
        }

        [Test]
        public void To01_UsesOnlyLow24Bits()
        {
            Assert.AreEqual(0f, ScatterHash.To01(0u));
            // The high byte is masked off, so flipping it must not change the result.
            Assert.AreEqual(ScatterHash.To01(0x00FFFFFFu), ScatterHash.To01(0xFFFFFFFFu));
            Assert.AreEqual(ScatterHash.To01(0x00ABCDEFu), ScatterHash.To01(0xFFABCDEFu));
        }

        // --- Distinctness (not cryptographic, but adjacent cells/slots must not collapse) ---

        [Test]
        public void Node_DistinctCellsRarelyCollide()
        {
            var seen = new System.Collections.Generic.HashSet<uint>();
            int count = 0, collisions = 0;
            for (int face = 0; face < 6; face++)
            for (int x = 0; x < 40; x++)
            for (int y = 0; y < 40; y++)
            {
                count++;
                if (!seen.Add(ScatterHash.Node(999, face, 6, x, y))) collisions++;
            }
            Assert.Zero(collisions, $"{collisions}/{count} node-seed collisions across distinct cells");
        }

        [Test]
        public void Slot_DistinctSlotsRarelyCollide()
        {
            uint node = ScatterHash.Node(3, 4, 5, 6, 7);
            var seen = new System.Collections.Generic.HashSet<uint>();
            for (int s = 0; s <= ScatterId.MaxSlot; s++)
                Assert.IsTrue(seen.Add(ScatterHash.Slot(node, s)), $"slot {s} seed collided");
        }
    }
}
