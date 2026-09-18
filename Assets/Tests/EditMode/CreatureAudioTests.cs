using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureAudioTests
    {
        [Test]
        public void SnapshotCopiesClipsAndRejectsInvalidRanges()
        {
            var settings = ScriptableObject.CreateInstance<CreatureAudioSettings>();
            var clip = AudioClip.Create("call", 32, 1, 8000, false);
            try
            {
                settings.Calls = new[] { clip };
                CreatureAudioDto snapshot = settings.Snapshot();
                settings.Calls[0] = null;
                Assert.That(snapshot.Calls[0], Is.SameAs(clip));
                settings.MaximumCallSeconds = settings.MinimumCallSeconds - 1;
                Assert.Throws<ArgumentException>(() => settings.Snapshot());
                settings.MaximumCallSeconds = float.NaN;
                Assert.Throws<ArgumentException>(() => settings.Snapshot());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void DistantAndSleepingActorsStaySilentAndForgetReleasesState()
        {
            var settings = ScriptableObject.CreateInstance<CreatureAudioSettings>();
            var clip = AudioClip.Create("call", 32, 1, 8000, false);
            var body = new GameObject("audio test");
            using var playback = new CreatureAudioPlayback(body.transform);
            try
            {
                settings.Calls = new[] { clip };
                settings.MinimumCallSeconds = settings.MaximumCallSeconds = 1;
                CreatureAudioDto snapshot = settings.Snapshot();
                playback.Tick(1, body.transform, snapshot, CreatureBehaviour.Wander, Vector3.one * 1000, 2);
                playback.Tick(2, body.transform, snapshot, CreatureBehaviour.Sleep, Vector3.zero, 2);
                Assert.That(playback.PlayedCount, Is.Zero);
                Assert.That(playback.TrackedCount, Is.EqualTo(2));
                playback.Forget(1);
                playback.Forget(2);
                Assert.That(playback.TrackedCount, Is.Zero);
                Assert.That(body.GetComponentsInChildren<AudioSource>(), Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(body);
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }
    }
}
