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
    public static readonly ToolTier Club = new ToolTier("Club", 1);
}

// What harvesting a node yields. The POC derives one mapping from the prototype; data-drive it later.
public readonly struct HarvestYield
{
    public readonly string ItemId;
    public readonly int Count;
    public HarvestYield(string itemId, int count) { ItemId = itemId; Count = count; }
}

// A node took harvest damage but is not yet felled (multi-hit chopping).
//
// planned: multi-hit chopping and its hit-particle / chop-SFX systems, docs/design/2026-08-12-next-roadmap.md.
// Unreachable today rather than merely unsubscribed: HarvestService raises it only when tool.Damage is below
// a node's HP, and every tool currently does exactly enough damage to fell in one swing.
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

// A creature was struck. Carries both outcomes rather than splitting into hit/killed events, because every
// subscriber so far wants the same line either way and the two differ only in whether Yield is worth reading.
//
// Deliberately NOT ScatterHarvestedEvent: TreeFallSystem and ChopFxSystem subscribe to that one and would
// topple a tree and burst leaves where the deer was standing.
public readonly struct CreatureStruckEvent : IGameEvent
{
    public readonly ulong Id;
    public readonly Vector3 WorldPos;
    public readonly string DisplayName;
    public readonly int RemainingHealth;
    public readonly bool Killed;
    public readonly HarvestYield Yield;

    public CreatureStruckEvent(ulong id, Vector3 worldPos, string displayName, int remainingHealth,
        bool killed, HarvestYield yield)
    { Id = id; WorldPos = worldPos; DisplayName = displayName; RemainingHealth = remainingHealth; Killed = killed; Yield = yield; }
}
