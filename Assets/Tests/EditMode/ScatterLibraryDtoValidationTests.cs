using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // ScatterLibraryDto.EnsureValid is the sole authority on scatter DTO invariants, and it runs on the
    // final (possibly world-overridden) DTO at boot. The duplicate-SlotId case in particular is the bug
    // that previously only surfaced in a play-test — a reused SlotId collides two prototypes' persistence
    // keys. These tests exercise every invariant against plain records (no ScriptableObjects, no assets),
    // starting from a known-valid prototype and flipping one field wrong via `with`.
    public sealed class ScatterLibraryDtoValidationTests
    {
        readonly List<Mesh> _meshes = new List<Mesh>();

        [TearDown]
        public void Cleanup()
        {
            foreach (Mesh m in _meshes)
                if (m != null) UnityEngine.Object.DestroyImmediate(m);
            _meshes.Clear();
        }

        Mesh NewMesh()
        {
            var m = new Mesh();
            _meshes.Add(m);
            return m;
        }

        static ScatterPrototypeDto ValidProto(int slot, string name = "P") => new ScatterPrototypeDto(
            DisplayName: name,
            SlotId: slot,
            SpacingMeters: 8f,
            Biome: BiomeType.Grassland,
            BiomeBlendPower: 1f,
            Weight: 1f,
            MaxSlopeDegrees: 35f,
            SlopeFadeDegrees: 5f,
            HasMinAltitude: false, MinAltitudeMeters: 0f,
            HasMaxAltitude: false, MaxAltitudeMeters: 0f,
            MinWaterClearanceMeters: 0.05f,
            ScaleRange: new Vector2(0.85f, 1.2f),
            RandomYaw: true,
            Interaction: ScatterInteraction.None,
            Parts: Array.Empty<ScatterPartDto>());

        static ScatterLibraryDto Lib(params ScatterPrototypeDto[] protos) => new ScatterLibraryDto(protos);

        static void AssertInvalid(ScatterLibraryDto lib, string expectedFragment = null)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => lib.EnsureValid());
            if (expectedFragment != null) StringAssert.Contains(expectedFragment, ex.Message);
        }

        [Test]
        public void EnsureValid_AcceptsDistinctValidPrototypes()
        {
            Assert.DoesNotThrow(() => Lib(ValidProto(0), ValidProto(1), ValidProto(ScatterId.MaxSlot)).EnsureValid());
        }

        [Test]
        public void EnsureValid_AcceptsEmptyLibrary()
        {
            Assert.DoesNotThrow(() => Lib().EnsureValid());
        }

        [Test]
        public void EnsureValid_RejectsDuplicateSlotId()
        {
            AssertInvalid(Lib(ValidProto(3, "A"), ValidProto(3, "B")), "duplicate SlotId 3");
        }

        [Test]
        public void EnsureValid_RejectsSlotIdAboveMax()
        {
            AssertInvalid(Lib(ValidProto(ScatterId.MaxSlot + 1)), "out of range");
        }

        [Test]
        public void EnsureValid_RejectsNegativeSlotId()
        {
            AssertInvalid(Lib(ValidProto(-1)), "out of range");
        }

        [Test]
        public void EnsureValid_RejectsNonPositiveSpacing()
        {
            AssertInvalid(Lib(ValidProto(0) with { SpacingMeters = 0f }));
            AssertInvalid(Lib(ValidProto(0) with { SpacingMeters = -1f }));
            AssertInvalid(Lib(ValidProto(0) with { SpacingMeters = float.NaN }));
            AssertInvalid(Lib(ValidProto(0) with { SpacingMeters = float.PositiveInfinity }));
        }

        [Test]
        public void EnsureValid_RejectsNonPositiveBiomeBlendPower()
        {
            AssertInvalid(Lib(ValidProto(0) with { BiomeBlendPower = 0f }));
            AssertInvalid(Lib(ValidProto(0) with { BiomeBlendPower = float.NaN }));
        }

        [Test]
        public void EnsureValid_RejectsNegativeWeight()
        {
            AssertInvalid(Lib(ValidProto(0) with { Weight = -0.01f }));
            Assert.DoesNotThrow(() => Lib(ValidProto(0) with { Weight = 0f }).EnsureValid(), "zero weight is allowed");
        }

        [Test]
        public void EnsureValid_RejectsSlopePlusFadeOver90()
        {
            AssertInvalid(Lib(ValidProto(0) with { MaxSlopeDegrees = 88f, SlopeFadeDegrees = 5f }), "exceeds 90");
            Assert.DoesNotThrow(() => Lib(ValidProto(0) with { MaxSlopeDegrees = 85f, SlopeFadeDegrees = 5f }).EnsureValid());
        }

        [Test]
        public void EnsureValid_RejectsInvertedAltitudeBounds()
        {
            AssertInvalid(Lib(ValidProto(0) with
            {
                HasMinAltitude = true, MinAltitudeMeters = 100f,
                HasMaxAltitude = true, MaxAltitudeMeters = 10f,
            }));
        }

        [Test]
        public void EnsureValid_RejectsInvertedOrNonPositiveScaleRange()
        {
            AssertInvalid(Lib(ValidProto(0) with { ScaleRange = new Vector2(2f, 1f) }));   // inverted
            AssertInvalid(Lib(ValidProto(0) with { ScaleRange = new Vector2(0f, 1f) }));   // non-positive min
        }

        [Test]
        public void EnsureValid_RejectsUndefinedBiome()
        {
            AssertInvalid(Lib(ValidProto(0) with { Biome = (BiomeType)9999 }), "undefined biome");
        }

        [Test]
        public void EnsureValid_RejectsUndefinedInteraction()
        {
            AssertInvalid(Lib(ValidProto(0) with { Interaction = (ScatterInteraction)9999 }), "undefined interaction");
        }

        [Test]
        public void EnsureValid_RejectsNullPartsArray()
        {
            AssertInvalid(Lib(ValidProto(0) with { Parts = null }), "Parts array is null");
        }

        [Test]
        public void EnsureValid_RejectsLodMeshDistanceCountMismatch()
        {
            var part = new ScatterPartDto(null, new[] { NewMesh() }, new[] { 50f, 150f }, true, true);
            AssertInvalid(Lib(ValidProto(0) with { Parts = new[] { part } }), "length mismatch");
        }

        [Test]
        public void EnsureValid_RejectsNonAscendingLodDistances()
        {
            var part = new ScatterPartDto(null, new[] { NewMesh(), NewMesh() }, new[] { 50f, 50f }, true, true);
            AssertInvalid(Lib(ValidProto(0) with { Parts = new[] { part } }), "ascending");
        }

        [Test]
        public void EnsureValid_AcceptsWellFormedLodChain()
        {
            var part = new ScatterPartDto(null, new[] { NewMesh(), NewMesh() }, new[] { 60f, 150f }, true, true);
            Assert.DoesNotThrow(() => Lib(ValidProto(0) with { Parts = new[] { part } }).EnsureValid());
        }
    }
}
