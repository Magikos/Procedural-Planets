using System;
using NUnit.Framework;

namespace ProceduralPlanets.Tests
{
    public sealed class PhysicalEquipmentStateTests
    {
        readonly EntityId _item = new(0, 2), _owner = new(0, 1);
        PhysicalEquipmentState Create() => new(_item, _owner, EquipmentLocation.Stowed, "LeftHip");

        [Test] public void TransferDoesNotChangeLocationBeforeContact()
        {
            var gear = Create(); Assert.IsTrue(gear.Begin(EquipmentLocation.Hand, "RightHand"));
            Assert.IsFalse(gear.Contact(false)); Assert.AreEqual(EquipmentLocation.Stowed, gear.Location);
            Assert.AreEqual(_item, gear.Item); Assert.AreEqual(_owner, gear.Owner);
            Assert.IsTrue(gear.Contact(true)); Assert.AreEqual(EquipmentLocation.Hand, gear.Location);
            Assert.AreEqual("RightHand", gear.Slot); Assert.IsTrue(gear.Transferring);
            gear.Finish(); Assert.IsFalse(gear.Transferring);
        }

        [Test] public void CancellationBeforeContactRetainsStorage()
        {
            var gear = Create(); gear.Begin(EquipmentLocation.Hand, "RightHand"); gear.Finish();
            Assert.AreEqual(EquipmentLocation.Stowed, gear.Location); Assert.AreEqual("LeftHip", gear.Slot);
        }

        [Test] public void EndingAfterContactDoesNotTeleportBack()
        {
            var gear = Create(); gear.Begin(EquipmentLocation.Hand, "RightHand"); gear.Contact(true); gear.Finish();
            Assert.AreEqual(EquipmentLocation.Hand, gear.Location);
        }

        [Test] public void DropPreservesIdentityAndClearsAttachmentAndOwner()
        {
            var gear = Create(); gear.Begin(EquipmentLocation.Hand, "RightHand"); gear.Contact(true); gear.Release();
            Assert.AreEqual(_item, gear.Item); Assert.IsTrue(gear.Owner.IsNone);
            Assert.AreEqual(EquipmentLocation.World, gear.Location); Assert.IsFalse(gear.Transferring);
            Assert.AreEqual("World", gear.Slot);
        }

        [Test] public void ConcurrentTransferCannotOverwriteDestination()
        {
            var gear = Create(); gear.Begin(EquipmentLocation.Hand, "RightHand");
            Assert.IsFalse(gear.Begin(EquipmentLocation.Hand, "LeftHand"));
            Assert.AreEqual("RightHand", gear.DestinationSlot);
        }

        [Test] public void PickupRequiresContactAndRestoresCustodyWithoutReplacingIdentity()
        {
            var gear = Create(); gear.Release();
            Assert.IsFalse(gear.Acquire(_owner, "RightHand", false));
            Assert.AreEqual(EquipmentLocation.World, gear.Location); Assert.IsTrue(gear.Owner.IsNone);
            Assert.IsTrue(gear.Acquire(_owner, "RightHand", true));
            Assert.AreEqual(_item, gear.Item); Assert.AreEqual(_owner, gear.Owner);
            Assert.AreEqual(EquipmentLocation.Hand, gear.Location);
            Assert.IsFalse(gear.Acquire(new EntityId(0, 3), "RightHand", true));
            Assert.AreEqual(_owner, gear.Owner);
        }

        [Test] public void InvalidIdentityOwnerAndSlotAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new PhysicalEquipmentState(EntityId.None, _owner, EquipmentLocation.Stowed, "Hip"));
            Assert.Throws<ArgumentException>(() => new PhysicalEquipmentState(_item, EntityId.None, EquipmentLocation.Hand, "RightHand"));
            Assert.Throws<ArgumentException>(() => Create().Begin(EquipmentLocation.Hand, ""));
        }
    }
}
