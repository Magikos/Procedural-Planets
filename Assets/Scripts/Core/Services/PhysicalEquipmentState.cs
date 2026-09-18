using System;

public enum EquipmentLocation { World, Stowed, Hand }

/// <summary>Item identity and physical custody, independent of animation and rendering.</summary>
public sealed class PhysicalEquipmentState
{
    public EntityId Item { get; }
    public EntityId Owner { get; private set; }
    public EquipmentLocation Location { get; private set; }
    public string Slot { get; private set; }
    public bool Transferring { get; private set; }
    public EquipmentLocation Destination { get; private set; }
    public string DestinationSlot { get; private set; }

    public PhysicalEquipmentState(EntityId item, EntityId owner, EquipmentLocation location, string slot)
    {
        if (item.IsNone) throw new ArgumentException("Physical gear requires an item identity.", nameof(item));
        Validate(owner, location, slot);
        Item = item; Owner = owner; Location = location; Slot = slot;
    }

    public bool Begin(EquipmentLocation destination, string slot)
    {
        Validate(Owner, destination, slot);
        if (Transferring || destination == Location && slot == Slot) return false;
        Destination = destination; DestinationSlot = slot; Transferring = true; return true;
    }

    public bool Contact(bool contactEstablished)
    {
        if (!Transferring || !contactEstablished) return false;
        Location = Destination; Slot = DestinationSlot; return true;
    }

    public void Finish() { Transferring = false; DestinationSlot = null; }

    public bool Acquire(EntityId owner, string hand, bool contactEstablished)
    {
        Validate(owner, EquipmentLocation.Hand, hand);
        if (!contactEstablished || Location != EquipmentLocation.World || Transferring) return false;
        Owner = owner; Location = EquipmentLocation.Hand; Slot = hand;
        return true;
    }

    public void Release()
    {
        Location = EquipmentLocation.World; Slot = "World"; Owner = EntityId.None; Finish();
    }

    static void Validate(EntityId owner, EquipmentLocation location, string slot)
    {
        if (!Enum.IsDefined(typeof(EquipmentLocation), location) || string.IsNullOrWhiteSpace(slot) ||
            location != EquipmentLocation.World && owner.IsNone)
            throw new ArgumentException("Equipment location requires a valid owner and slot.");
    }
}
