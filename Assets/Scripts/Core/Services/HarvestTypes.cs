using UnityEngine;

// Shared value types + events for the harvesting loop (POC seams — see plans/003-harvest-vertical-slice.md).
// Kept in Core so the Planet-side HarvestService (which raises the events) and Core-side consumers (HUD toast,
// inventory) can share them without Core depending on Planet.

// A tool's harvest damage tier. The POC uses one default; axe-quality tiers plug in later.
public readonly struct ToolTier
{
    public readonly string Name;
    public readonly int Damage;
    public ToolTier(string name, int damage) { Name = name; Damage = damage; }

    // ponytail: hardcoded tools for the POC, chosen by what you aim at; replace with an equipped-tool lookup
    // + real tiers (and a "do you have this tool" gate) once inventory/equip exists.
    public static readonly ToolTier BasicAxe = new ToolTier("Basic Axe", 1);
    public static readonly ToolTier Shovel = new ToolTier("Shovel", 1);
}

// What harvesting a node yields. The POC derives one mapping from the prototype; data-drive it later.
public readonly struct HarvestYield
{
    public readonly string ItemId;
    public readonly int Count;
    public HarvestYield(string itemId, int count) { ItemId = itemId; Count = count; }
}

// A node took harvest damage but is not yet felled (multi-hit chopping). The POC never raises it (one-shot
// fell), but future hit-particle / chop-SFX systems subscribe here.
public readonly struct HarvestHitEvent : IGameEvent
{
    public readonly ulong Id;
    public readonly int ProtoIndex;
    public readonly Vector3 WorldPos;
    public readonly int RemainingHp;
    public HarvestHitEvent(ulong id, int protoIndex, Vector3 worldPos, int remainingHp)
    { Id = id; ProtoIndex = protoIndex; WorldPos = worldPos; RemainingHp = remainingHp; }
}

// A node was harvested (felled). The HUD toast subscribes here, and future fall-animation / sound / VFX
// attach the same way — without touching harvest logic.
public readonly struct ScatterHarvestedEvent : IGameEvent
{
    public readonly ulong Id;
    public readonly int ProtoIndex;
    public readonly Vector3 WorldPos;
    public readonly HarvestYield Yield;
    public ScatterHarvestedEvent(ulong id, int protoIndex, Vector3 worldPos, HarvestYield yield)
    { Id = id; ProtoIndex = protoIndex; WorldPos = worldPos; Yield = yield; }
}
