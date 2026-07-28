using System;
using NUnit.Framework;

namespace ProceduralPlanets.Tests
{
    // ScatterId packs a scatter instance's stable identity into a u64 that SP5 writes chop/collect
    // overrides against. A packing or validation bug silently rebinds a saved edit to the wrong object,
    // so pack/unpack must round-trip exactly and Pack must reject every out-of-range field. Pure logic,
    // no Unity runtime — the ideal first data structure to lock down under test.
    public sealed class ScatterIdTests
    {
        static int MaxCoord => (1 << ScatterId.MaxLevel) - 1;

        [Test]
        public void PackUnpack_RoundTripsAllFields()
        {
            ulong id = ScatterId.Pack(3, 12, 1234567, 7654321, 42);
            ScatterId.Unpack(id, out int face, out int level, out int x, out int y, out int slot);

            Assert.AreEqual(3, face);
            Assert.AreEqual(12, level);
            Assert.AreEqual(1234567, x);
            Assert.AreEqual(7654321, y);
            Assert.AreEqual(42, slot);
        }

        [Test]
        public void PackUnpack_MaxBoundaryValues()
        {
            ulong id = ScatterId.Pack(ScatterId.FaceCount - 1, ScatterId.MaxLevel, MaxCoord, MaxCoord, ScatterId.MaxSlot);
            ScatterId.Unpack(id, out int face, out int level, out int x, out int y, out int slot);

            Assert.AreEqual(ScatterId.FaceCount - 1, face);
            Assert.AreEqual(ScatterId.MaxLevel, level);
            Assert.AreEqual(MaxCoord, x);
            Assert.AreEqual(MaxCoord, y);
            Assert.AreEqual(ScatterId.MaxSlot, slot);
        }

        [Test]
        public void PackUnpack_AllZeroFieldsPackToZero()
        {
            ulong id = ScatterId.Pack(0, 0, 0, 0, 0, player: false);
            Assert.AreEqual(0UL, id);

            ScatterId.Unpack(id, out int face, out int level, out int x, out int y, out int slot);
            Assert.AreEqual(0, face);
            Assert.AreEqual(0, level);
            Assert.AreEqual(0, x);
            Assert.AreEqual(0, y);
            Assert.AreEqual(0, slot);
        }

        [Test]
        public void PackUnpack_RoundTripsAcrossSpread()
        {
            int[] faces = { 0, 1, 3, 5 };
            int[] levels = { 0, 1, 10, ScatterId.MaxLevel };
            int[] coords = { 0, 1, 255, 65535, MaxCoord };
            int[] slots = { 0, 1, 31, ScatterId.MaxSlot };

            foreach (int f in faces)
            foreach (int l in levels)
            foreach (int cx in coords)
            foreach (int cy in coords)
            foreach (int s in slots)
            {
                ulong id = ScatterId.Pack(f, l, cx, cy, s);
                ScatterId.Unpack(id, out int of, out int ol, out int ox, out int oy, out int os);
                Assert.AreEqual(f, of, "face");
                Assert.AreEqual(l, ol, "level");
                Assert.AreEqual(cx, ox, "x");
                Assert.AreEqual(cy, oy, "y");
                Assert.AreEqual(s, os, "slot");
            }
        }

        [Test]
        public void PlayerBit_SetAndReadIndependently()
        {
            ulong notPlayer = ScatterId.Pack(1, 2, 3, 4, 5, player: false);
            ulong player = ScatterId.Pack(1, 2, 3, 4, 5, player: true);

            Assert.IsFalse(ScatterId.IsPlayer(notPlayer));
            Assert.IsTrue(ScatterId.IsPlayer(player));

            // The player bit is the only difference between the two ids.
            Assert.AreEqual(notPlayer | (1UL << 62), player);

            // ...and it does not corrupt the other fields on unpack.
            ScatterId.Unpack(player, out int f, out int l, out int x, out int y, out int s);
            Assert.AreEqual(1, f);
            Assert.AreEqual(2, l);
            Assert.AreEqual(3, x);
            Assert.AreEqual(4, y);
            Assert.AreEqual(5, s);
        }

        [Test]
        public void Bit63_StaysSpareEvenAtMaxFieldsWithPlayer()
        {
            ulong id = ScatterId.Pack(ScatterId.FaceCount - 1, ScatterId.MaxLevel, MaxCoord, MaxCoord,
                                      ScatterId.MaxSlot, player: true);
            Assert.AreEqual(0UL, id & (1UL << 63), "bit 63 must remain unused (spare)");
        }

        [Test]
        public void ChangingOneField_LeavesOthersIntact()
        {
            ulong a = ScatterId.Pack(2, 5, 100, 200, 7);
            ulong b = ScatterId.Pack(2, 5, 100, 200, 8); // only slot differs

            Assert.AreNotEqual(a, b);
            ScatterId.Unpack(b, out int f, out int l, out int x, out int y, out int s);
            Assert.AreEqual(2, f);
            Assert.AreEqual(5, l);
            Assert.AreEqual(100, x);
            Assert.AreEqual(200, y);
            Assert.AreEqual(8, s);
        }

        [Test]
        public void DistinctInputs_ProduceDistinctIds()
        {
            ulong baseId = ScatterId.Pack(2, 5, 100, 200, 7);
            Assert.AreNotEqual(baseId, ScatterId.Pack(3, 5, 100, 200, 7), "face must move bits");
            Assert.AreNotEqual(baseId, ScatterId.Pack(2, 6, 100, 200, 7), "level must move bits");
            Assert.AreNotEqual(baseId, ScatterId.Pack(2, 5, 101, 200, 7), "x must move bits");
            Assert.AreNotEqual(baseId, ScatterId.Pack(2, 5, 100, 201, 7), "y must move bits");
            Assert.AreNotEqual(baseId, ScatterId.Pack(2, 5, 100, 200, 8), "slot must move bits");
        }

        [Test]
        public void Pack_RejectsFaceOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ScatterId.Pack(ScatterId.FaceCount, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScatterId.Pack(-1, 0, 0, 0, 0));
        }

        [Test]
        public void Pack_RejectsLevelOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ScatterId.Pack(0, -1, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScatterId.Pack(0, ScatterId.MaxLevel + 1, 0, 0, 0));
        }

        [Test]
        public void Pack_RejectsCoordOverflow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ScatterId.Pack(0, 0, MaxCoord + 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScatterId.Pack(0, 0, 0, MaxCoord + 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ScatterId.Pack(0, 0, -1, 0, 0));
        }

        [Test]
        public void Pack_RejectsSlotOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ScatterId.Pack(0, 0, 0, 0, ScatterId.MaxSlot + 1));
        }
    }
}
