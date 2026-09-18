using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureGrassInteractionTests
    {
        CreatureGrassInteraction _animals;
        IGrassInteractor[] _before;
        static CreatureSpeciesDto Species => CreatureLibraryDto.Placeholder.At(0) with { BodyHeightMeters = 2f };

        [SetUp]
        public void SetUp()
        {
            _before = GrassInteractorRegistry.Interactors.ToArray();
            _animals = new CreatureGrassInteraction();
        }

        [TearDown]
        public void TearDown()
        {
            _animals.Dispose();
            foreach (var source in Added()) GrassInteractorRegistry.Unregister(source);
        }

        IGrassInteractor[] Added() => GrassInteractorRegistry.Interactors.Except(_before).ToArray();
        static CreatureResidencyService.LiveCreature Actor(int slot, float distance, bool swimming = false, float altitude = 0f, bool grounded = true) =>
            new(CreatureKey.Slot(2, CreatureTerritory.Level, 8, 8, slot), 0,
                new Vector3(distance, 1f, 0f), Vector3.up, Vector3.forward, altitudeMeters: altitude, swimming: swimming, grounded: grounded);

        [Test]
        public void SelectsThreeNearestAndRejectsSwimmingAirborneAndDistantActors()
        {
            _animals.Begin(Vector3.zero, true);
            _animals.Consider(Actor(0, 12), Species);
            _animals.Consider(Actor(1, 8), Species);
            _animals.Consider(Actor(2, 4), Species);
            _animals.Consider(Actor(3, 2), Species);
            _animals.Consider(Actor(4, 1, swimming: true), Species);
            _animals.Consider(Actor(5, 1, altitude: 2), Species);
            _animals.Consider(Actor(6, 40), Species);
            _animals.Consider(Actor(7, 1, grounded: false), Species);
            _animals.End();
            Assert.That(Added().Select(x => x.WorldPosition.x).OrderBy(x => x), Is.EqualTo(new[] { 2f, 4f, 8f }));
            Assert.That(Added().All(x => x.WorldPosition.y == 0f), Is.True);
        }

        [Test]
        public void KeepsSourceIdentityWhenNearestOrderChangesAndRemovesMissingActors()
        {
            _animals.Begin(Vector3.zero, true);
            _animals.Consider(Actor(0, 5), Species);
            _animals.Consider(Actor(1, 10), Species);
            _animals.End();
            var retained = Added().Single(x => x.WorldPosition.x == 10f);
            _animals.Begin(Vector3.zero, true);
            _animals.Consider(Actor(1, 3), Species);
            _animals.End();
            Assert.That(Added(), Is.EqualTo(new[] { retained }));
            Assert.That(retained.WorldPosition.x, Is.EqualTo(3f));
        }

        [Test]
        public void HiddenAndDisposedViewsReleaseTheirSources()
        {
            _animals.Begin(Vector3.zero, true);
            _animals.Consider(Actor(0, 2), Species);
            _animals.End();
            _animals.Begin(Vector3.zero, false);
            _animals.Consider(Actor(0, 2), Species);
            _animals.End();
            Assert.That(Added(), Is.Empty);
            _animals.Dispose();
            Assert.That(Added(), Is.Empty);
        }

        [Test]
        public void LaterPlayerRegistrationPrecedesWildlife()
        {
            _animals.Begin(Vector3.zero, true);
            _animals.Consider(Actor(0, 2), Species);
            _animals.End();
            var player = new Source();
            GrassInteractorRegistry.Register(player);
            Assert.That(Added()[0], Is.SameAs(player));
        }

        [Test]
        public void LowerPrioritySourcesLeaveSlotsForReleaseTrails()
        {
            Assert.That(_before, Is.Empty, "Run this test outside a loaded Planet world.");
            try
            {
                GrassInteractorRegistry.Initialize();
                for (int i = 0; i < 7; i++) GrassInteractorRegistry.Register(new Source(), -1, 6);
                var removed = new Source();
                GrassInteractorRegistry.Register(removed);
                GrassInteractorRegistry.Unregister(removed);
                typeof(GrassInteractorRegistry).GetMethod("UploadPerFrame", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                Assert.That(GrassInteractorRegistry.LastActiveCount, Is.EqualTo(7));
                Assert.That(GrassInteractorRegistry.LastReleaseSampleCount, Is.EqualTo(1));
            }
            finally
            {
                typeof(GrassInteractorRegistry).GetMethod("DisposeBuffer", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            }
        }

        sealed class Source : IGrassInteractor
        {
            public Vector3 WorldPosition => Vector3.zero;
            public float Radius => 1f;
            public float Strength => 1f;
            public float ReleaseSeconds => 1f;
            public bool IsActive => true;
        }
    }
}
