using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class SoundAuditionTests
    {
        [Test]
        public void DecisionsNotesAndPlaybackSurviveReloadAndReplacement()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SoundAudition-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "reviews.json");
            try
            {
                var store = new SoundReviewStore(path);
                var first = store.Get("ocean-a", true);
                first.Decision = SoundReviewDecision.Keep;
                first.Notes = "Gentle waves\nNo voices";
                first.Volume = .2f;
                first.Loop = false;
                store.Save(first);
                store.Save(store.Get("crickets-b", true));
                first.Decision = SoundReviewDecision.Reject;
                store.Save(first);
                var reloaded = new SoundReviewStore(path);
                var actual = reloaded.Get("ocean-a", true);
                Assert.That(actual.Decision, Is.EqualTo(SoundReviewDecision.Reject));
                Assert.That(actual.Notes, Is.EqualTo(first.Notes));
                Assert.That(actual.Volume, Is.EqualTo(.2f));
                Assert.That(actual.Loop, Is.False);
                Assert.That(reloaded.Get("crickets-b", false).Loop, Is.True);
                Assert.That(File.Exists(path + ".bak"), Is.True);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [TestCase("{broken")]
        [TestCase("{\"Version\":99,\"Reviews\":[]}")]
        [TestCase("{\"Version\":1,\"Reviews\":[{\"Id\":\"a\"},{\"Id\":\"a\"}]}")]
        public void InvalidSavedDataRemainsUntouched(string original)
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, original);
                Assert.Catch(() => new SoundReviewStore(path));
                Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void InvalidGainCannotReplaceASavedDecision()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new SoundReviewStore(path);
                var review = store.Get("wind", true);
                store.Save(review);
                string original = File.ReadAllText(path);
                review.Volume = float.NaN;
                Assert.Throws<InvalidDataException>(() => store.Save(review));
                Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void MixRequiresKeptClipAndMatchingEnvironment()
        {
            var clip = AudioClip.Create("review", 32, 1, 8000, false);
            try
            {
                var candidate = new SoundAuditionCandidate { Clip = clip, Environments = SoundAuditionEnvironment.Coast };
                foreach (SoundReviewDecision decision in Enum.GetValues(typeof(SoundReviewDecision)))
                {
                    var review = new SoundReview { Decision = decision };
                    Assert.That(SoundAudition.CanMix(candidate, review, SoundAuditionEnvironment.Coast),
                        Is.EqualTo(decision == SoundReviewDecision.Keep));
                    Assert.That(SoundAudition.CanMix(candidate, review, SoundAuditionEnvironment.ForestDay), Is.False);
                }
                candidate.Clip = null;
                Assert.That(SoundAudition.CanMix(candidate, new SoundReview { Decision = SoundReviewDecision.Keep },
                    SoundAuditionEnvironment.Coast), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
        }
    }
}
