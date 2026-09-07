using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ScatterLodFadeTests
    {
        Mesh _quad;

        [SetUp]
        public void SetUp() => _quad = new Mesh();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_quad);

        ScatterLodBatcher.Impostor MakeImpostor(float start) =>
            new ScatterLodBatcher.Impostor(default, _quad, start, start * 4.5f);

        [Test]
        public void MeshAndCard_ShareTheSameTransitionInterval()
        {
            var card = MakeImpostor(500f);
            Assert.AreEqual(425f, ScatterLodBatcher.ImpostorNearFor(card));
            Assert.AreEqual(425f, ScatterLodBatcher.FadeStartFor(1, 2, 500f, card));
            Assert.AreEqual(500f, ScatterLodBatcher.FadeEndFor(500f, card));
        }

        [Test]
        public void SharedSilhouette_HasExactlyOneOwnerAtEveryDitherThreshold()
        {
            var card = MakeImpostor(500f);
            float start = ScatterLodBatcher.ImpostorNearFor(card);
            float end = ScatterLodBatcher.FadeEndFor(500f, card);
            for (int step = 0; step <= 100; step++)
            {
                float distance = Mathf.Lerp(start, end, step / 100f);
                float progress = Mathf.Clamp01((distance - start) / (end - start));
                for (int pixel = 1; pixel < 100; pixel++)
                {
                    float threshold = pixel / 100f;
                    bool meshVisible = threshold > progress;
                    bool cardVisible = progress >= threshold;
                    Assert.AreNotEqual(meshVisible, cardVisible, $"distance={distance}, threshold={threshold}");
                }
            }
        }

        [Test]
        public void MeshLodBoundaryInsideTransition_DoesNotRestartTheFade()
        {
            var card = MakeImpostor(500f);
            Assert.AreEqual(425f, ScatterLodBatcher.FadeStartFor(0, 2, 450f, card));
            Assert.AreEqual(500f, ScatterLodBatcher.FadeEndFor(450f, card));
            Assert.AreEqual(425f, ScatterLodBatcher.FadeStartFor(1, 2, 500f, card));
        }

        [Test]
        public void IntermediateMeshBeforeCard_DoesNotFadeIntoAnAbsentSuccessor()
        {
            var card = MakeImpostor(500f);
            Assert.AreEqual(300f, ScatterLodBatcher.FadeStartFor(0, 2, 300f, card));
            Assert.AreEqual(300f, ScatterLodBatcher.FadeEndFor(300f, card));
        }

        [Test]
        public void FinalMeshWithoutCard_FadesBeforeItsCull()
        {
            Assert.AreEqual(425f, ScatterLodBatcher.FadeStartFor(0, 1, 500f, default));
            Assert.AreEqual(500f, ScatterLodBatcher.FadeEndFor(500f, default));
        }

        [Test]
        public void PartEndingBeforeCard_FadesIntoBackground()
        {
            Assert.AreEqual(255f, ScatterLodBatcher.FadeStartFor(0, 1, 300f, MakeImpostor(500f)));
        }

        [Test]
        public void MeshBands_PartitionWithoutGaps()
        {
            float[] ends = { 120f, 300f, 500f };
            Assert.AreEqual(0f, ScatterLodBatcher.BandNearFor(0, ends));
            for (int lod = 1; lod < ends.Length; lod++)
                Assert.AreEqual(ends[lod - 1], ScatterLodBatcher.BandNearFor(lod, ends));
        }

        [Test]
        public void MeshBandsBeyondHandover_AreSkipped()
        {
            float[] ends = { 120f, 300f, 500f };
            Assert.AreEqual(200f, ScatterLodBatcher.BandFarFor(1, ends, 200f));
            Assert.LessOrEqual(ScatterLodBatcher.BandFarFor(2, ends, 200f),
                ScatterLodBatcher.BandNearFor(2, ends));
        }
    }
}
