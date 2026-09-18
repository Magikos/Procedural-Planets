using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CreatureEndurancePersistenceTests
    {
        [TestCase(CreatureBehaviour.Feed)]
        [TestCase(CreatureBehaviour.Drink)]
        public void TravelingResourceStateUsesWalkingEndurance(CreatureBehaviour behaviour)
        {
            var type = typeof(CreatureResidencyService).GetNestedType("Resident", BindingFlags.NonPublic);
            var resident = Activator.CreateInstance(type, true);
            var species = CreatureLibraryDto.Placeholder.At(0) with { Endurance = new ActorEnduranceProfile(10d, 3600d, 120d) };
            var endurance = new ActorEndurance(.5d);
            type.GetField("Endurance").SetValue(resident, endurance);
            type.GetField("Needs").SetValue(resident, new ActorNeeds(.2d, .2d));
            type.GetField("Health").SetValue(resident, species.MaxHealth);
            type.GetField("Behaviour").SetValue(resident, behaviour);
            type.GetField("Brain").SetValue(resident, new CreatureBrain(1, species, behaviour));
            typeof(CreatureResidencyService).GetMethod("FinishEnduranceStep", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { resident, species, 1f, 1f });
            var expected = new ActorEndurance(.5d);
            expected.Advance(1d, new ActorNeeds(.2d, .2d), ActorExertion.Walk, species.Endurance.Value);
            Assert.AreEqual(expected.Stamina, endurance.Stamina, 1e-9);
        }

        static CreatureRecord Record(CreatureEnduranceState endurance) => CreatureRecord.Displacement(
            CreatureKey.Slot(0, CreatureTerritory.Level, 0, 0, 1), 0, 0, Vector3.up * 1000f, 100,
            CreatureBehaviour.Rest, 2, new ActorNeeds(.7d, .6d), endurance);

        [Test]
        public void ReservesAndThresholdFlagsSurviveSaveReload()
        {
            var state = new CreatureEnduranceState(.2d, .55d, .8d, true, true);
            var delta = CreatureRecordCodec.Encode(Record(state));
            Assert.That(delta.Payload[0], Is.EqualTo(5));
            Assert.That(CreatureRecordCodec.TryDecode(delta, out var decoded), Is.True);
            Assert.That(decoded.Endurance, Is.EqualTo(state));
            var restored = decoded.Endurance.Restore();
            Assert.That(restored.Stamina, Is.EqualTo(.2d));
            Assert.That(restored.Capacity, Is.EqualTo(.8d));
            Assert.That(restored.Recovering && restored.NeedsSleep, Is.True);
        }

        [Test]
        public void VersionFourLoadsWithoutInventingAnEnduranceState()
        {
            var encoded = CreatureRecordCodec.Encode(Record(default));
            var payload = new byte[38];
            Array.Copy(encoded.Payload, payload, payload.Length); payload[0] = 4;
            var legacy = new WorldDelta(0, encoded.Kind, encoded.Key, encoded.Position,
                typeIndex: encoded.TypeIndex, state: encoded.State, payload: payload);
            Assert.That(CreatureRecordCodec.TryDecode(legacy, out var restored), Is.True);
            Assert.That(restored.Needs.Hunger, Is.EqualTo(.7d));
            Assert.That(restored.Endurance.Enabled, Is.False);
        }

        [TestCase(double.NaN, .5d, .8d, 1)]
        [TestCase(.9d, .5d, .8d, 1)]
        [TestCase(.2d, .5d, .8d, 128)]
        [TestCase(.2d, .5d, .8d, 0)]
        public void InvalidSavedReservesAreRejected(double stamina, double fatigue, double capacity, byte flags)
        {
            var delta = CreatureRecordCodec.Encode(Record(default));
            BitConverter.GetBytes(stamina).CopyTo(delta.Payload, 38);
            BitConverter.GetBytes(fatigue).CopyTo(delta.Payload, 46);
            BitConverter.GetBytes(capacity).CopyTo(delta.Payload, 54);
            delta.Payload[62] = flags;
            Assert.That(CreatureRecordCodec.TryDecode(delta, out _), Is.False);
        }

        [Test]
        public void DormancyRecoversBreathWithoutResettingFatigue()
        {
            var residentType = typeof(CreatureResidencyService).GetNestedType("Resident", BindingFlags.NonPublic);
            var resident = Activator.CreateInstance(residentType, true);
            var species = CreatureLibraryDto.Placeholder.At(0) with
                { Endurance = new ActorEnduranceProfile(20d, 3600d, 120d) };
            residentType.GetField("Health").SetValue(resident, species.MaxHealth);
            residentType.GetField("Needs").SetValue(resident, new ActorNeeds(.3d, .2d));
            residentType.GetField("Behaviour").SetValue(resident, CreatureBehaviour.Rest);
            typeof(CreatureResidencyService).GetMethod("RestoreEndurance", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { resident, species, new CreatureEnduranceState(.2d, .6d, .85d, true, false) });
            var endurance = (ActorEndurance)residentType.GetField("Endurance").GetValue(resident);
            Assert.That(endurance.Stamina, Is.EqualTo(.2d));
            typeof(CreatureResidencyService).GetMethod("AdvanceOfflineEndurance", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { resident, species, 86400d });
            Assert.That(endurance.Stamina, Is.GreaterThan(.2d));
            Assert.That(endurance.Fatigue, Is.GreaterThanOrEqualTo(.6d));
            Assert.That(endurance.Fatigue, Is.LessThan(.7d));
        }
    }
}
