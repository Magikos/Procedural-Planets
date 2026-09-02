using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // A screen-door dissolve only hides a swap when the BACKGROUND is what should replace the clipped
    // pixels. Neither successor tier qualifies: a coarser mesh LOD is a decimation with a thinner canopy,
    // and the card is a square billboard whose baked silhouette does not agree with the mesh's per pixel.
    // Dither either against the other and the sky shows through as a lattice of holes — or, when the card
    // ramps in early, as a second half-transparent canopy standing beside the solid one. These tests pin
    // the rule that removes both: exactly one tier draws at any distance, and it draws at full coverage.
    public sealed class ScatterLodFadeTests
    {
        const float Far = 500f;        // last mesh LOD's cull = ScatterPrototypeDto.MaxCullDistance
        const float CardTakeover = Far; // ScatterPrototypeDto.ImpostorStartDistance

        Mesh _quad;

        [SetUp]
        public void SetUp() => _quad = new Mesh();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_quad);

        ScatterLodBatcher.Impostor MakeImpostor(float start) =>
            new ScatterLodBatcher.Impostor(default, _quad, start, start * 4.5f);

        // Both tiers ramp linearly and saturate, exactly as the shaders do.
        static float Ramp(float d, float start, float end) =>
            Mathf.Clamp01((d - start) / Mathf.Max(1e-3f, end - start));

        [Test]
        public void LastMeshLod_WithAnImpostor_DiscardsNothingBeforeItsCull()
        {
            float fadeStart = ScatterLodBatcher.FadeStartFor(lod: 1, lodCount: 2, Far, MakeImpostor(CardTakeover));

            for (float d = 0f; d < Far; d += 1f)
                Assert.AreEqual(0f, Ramp(d, fadeStart, Far), 1e-4f,
                    $"at {d} m the mesh discards thresholds the card cannot be trusted to fill");
        }

        // The card is opaque on the first frame it draws. A ramp that reached full coverage over the band
        // BEHIND a still-solid mesh looks safe and is not: the mesh only covers the middle of the card's
        // square, so every threshold the ramp clips outside the tree's silhouette is a visible ghost.
        [Test]
        public void ImpostorCard_IsOpaque_AcrossItsWholeBand()
        {
            ScatterLodBatcher.Impostor imp = MakeImpostor(CardTakeover);
            float bandStart = ScatterLodBatcher.ImpostorNearFor(imp);

            // ScatterImpostorFactory bakes _FadeIn* as [takeover - 1, takeover].
            for (float d = bandStart; d <= imp.EndDistance; d += 25f)
                Assert.AreEqual(1f, Ramp(d, CardTakeover - 1f, CardTakeover), 1e-4f,
                    $"the card is only {Ramp(d, CardTakeover - 1f, CardTakeover):0.00} covered at {d} m");
        }

        // With no card behind it the prop is SUPPOSED to be gone past its cull, so the background is a valid
        // successor and the last band dissolves into it. Without this a flower at 45 m appears whole in one
        // frame as you walk toward it.
        [Test]
        public void LastMeshLod_WithNoImpostor_DissolvesIntoTheBackground()
        {
            float fadeStart = ScatterLodBatcher.FadeStartFor(lod: 0, lodCount: 1, Far, default);

            Assert.Less(fadeStart, Far);
            Assert.AreEqual(0f, Ramp(fadeStart, fadeStart, Far), 1e-4f);
            Assert.AreEqual(1f, Ramp(Far, fadeStart, Far), 1e-4f);
        }

        // The dissolve window is a FRACTION of the cull, not a fixed width: a 45 m flower and a 250 m rock
        // must both be solid for the same share of their range.
        [Test]
        public void NoImpostorFadeWindow_ScalesWithTheCullDistance()
        {
            float shortWindow = 45f - ScatterLodBatcher.FadeStartFor(lod: 0, lodCount: 1, far: 45f, default);
            float longWindow = 250f - ScatterLodBatcher.FadeStartFor(lod: 0, lodCount: 1, far: 250f, default);

            Assert.Less(shortWindow, longWindow);
            Assert.AreEqual(shortWindow / 45f, longWindow / 250f, 1e-4f);
        }

        // A part that culls before the prototype's card takes over has no successor but the background, so
        // it dissolves. This is the case the boundary test has to get right: the card covers a band that
        // starts exactly AT the cull, so "covered" has to include equality or every part dithers out.
        [Test]
        public void PartCullingBeforeTheCardTakesOver_FallsBackToTheBackgroundDissolve()
        {
            float early = 300f;
            float fadeStart = ScatterLodBatcher.FadeStartFor(lod: 0, lodCount: 1, early, MakeImpostor(CardTakeover));

            Assert.AreEqual(early * ScatterPrototypeDto.MeshFadeFraction, fadeStart, 1e-3f);
        }

        // The next mesh LOD is drawn solid across the overlap, but "solid" is not "covering": it is a
        // decimation of this one and its canopy is thinner. Dithering this LOD out exposed those gaps as a
        // lattice of tiny holes on every tree in the transition band, which is the artifact this pins.
        [Test]
        public void IntermediateMeshLod_DoesNotFade_BecauseTheCoarserMeshCannotCoverIt()
        {
            float fadeStart = ScatterLodBatcher.FadeStartFor(lod: 0, lodCount: 2, far: 300f, MakeImpostor(CardTakeover));

            Assert.AreEqual(300f, fadeStart, 1e-3f);
            Assert.AreEqual(0f, Ramp(300f - 0.001f, fadeStart, 300f), 1e-4f);
        }

        // Both band tests are half-open [near, far), so starting each band exactly where the previous one
        // culls leaves no gap at the seam and draws no instance twice. A band that started early would only
        // pay a double draw: mesh -> mesh does not dither, so there is nothing for an overlap to hide.
        [Test]
        public void MeshBands_PartitionExactly_WithNoGapAndNoDoubleDraw()
        {
            float[] ends = { 120f, 300f, Far };

            Assert.AreEqual(0f, ScatterLodBatcher.BandNearFor(0, ends), 1e-3f);
            for (int lod = 1; lod < ends.Length; lod++)
                Assert.AreEqual(ends[lod - 1], ScatterLodBatcher.BandNearFor(lod, ends), 1e-3f);
        }

        // The card's band and the last mesh band partition too: the card starts on the frame the mesh culls,
        // so the tiers never overlap and the swap is a single-frame handover, not a blend.
        [Test]
        public void ImpostorBand_StartsWhereTheLastMeshBandCulls()
        {
            ScatterLodBatcher.Impostor imp = MakeImpostor(CardTakeover);

            Assert.AreEqual(Far, ScatterLodBatcher.ImpostorNearFor(imp), 1e-3f);
        }

        // The handover is decided by on-screen size, so a prototype's mesh cull lands mid-ladder and the
        // bands past it must not draw at all. Without the clip the mesh and the card both draw between the
        // handover and the authored cull - two solid tiers of the same prop, one inside the other.
        [Test]
        public void MeshBandsPastTheHandover_AreSkipped_SoTheCardNeverDoublesWithTheMesh()
        {
            float[] ends = { 120f, 300f, Far };
            float meshCull = 200f;

            Assert.AreEqual(120f, ScatterLodBatcher.BandFarFor(0, ends, meshCull), 1e-3f);
            Assert.AreEqual(meshCull, ScatterLodBatcher.BandFarFor(1, ends, meshCull), 1e-3f);
            Assert.LessOrEqual(ScatterLodBatcher.BandFarFor(2, ends, meshCull),
                               ScatterLodBatcher.BandNearFor(2, ends));
        }

        // A prototype whose card takes over past its authored ladder keeps every authored band intact -
        // clipping can only ever pull the mesh tier in, never push it out.
        [Test]
        public void AHandoverBeyondTheLadder_LeavesTheAuthoredBandsUntouched()
        {
            float[] ends = { 120f, 300f, Far };

            for (int lod = 0; lod < ends.Length; lod++)
                Assert.AreEqual(ends[lod], ScatterLodBatcher.BandFarFor(lod, ends, Far * 2f), 1e-3f);
        }
    }
}
