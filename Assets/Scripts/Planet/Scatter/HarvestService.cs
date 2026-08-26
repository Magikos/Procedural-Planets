using System;
using UnityEngine;

// The one verb choke point for harvesting (POC seam — see plans/003-harvest-vertical-slice.md §3b). Every
// harvest routes through TryHarvest, which today one-shots the node: persist + remove-from-draw + grant yield
// + raise events. Multi-hit chopping, axe-quality damage, fall animation, and log entities all slot in HERE
// without touching callers. Dependencies are injected as delegates so this stays a pure orchestrator that the
// player controller and tests wire independently.
public sealed class HarvestService
{
    readonly Func<ScatterPick, bool> _persistHarvest; // record the fell; false if already harvested
    readonly Action<int, ulong> _removeFromDraw;     // drop the instance from the draw this frame
    readonly Action<string, int> _grantItem;         // credit the inventory
    readonly Func<int, ProtoHarvestInfo> _protoInfo; // prototype interaction + display name
    readonly Func<ulong, bool> _digStump;            // dig a stump (Stump -> Dug); false if no stump there
    readonly Func<EntityId, int, Vector3, CreatureStrike> _strikeCreature; // wound or kill an animal

    // ponytail: node HP defaulted so one hit fells it. Multi-hit chopping adds a per-ScatterId hit accumulator
    // consulted here — compare tool.Damage against remaining HP, raise HarvestHitEvent until it reaches 0.
    const int DefaultNodeHp = 1;

    public HarvestService(Func<ScatterPick, bool> persistHarvest, Action<int, ulong> removeFromDraw,
        Action<string, int> grantItem, Func<int, ProtoHarvestInfo> protoInfo, Func<ulong, bool> digStump,
        Func<EntityId, int, Vector3, CreatureStrike> strikeCreature = null)
    {
        _persistHarvest = persistHarvest;
        _removeFromDraw = removeFromDraw;
        _grantItem = grantItem;
        _protoInfo = protoInfo;
        _digStump = digStump;
        _strikeCreature = strikeCreature;
    }

    // Dig up a stump (Stump -> Dug): it stops rendering and yields a little more wood. `tool` is the shovel
    // (not gated yet — a "do you have a shovel" check waits for inventory/equip).
    public HarvestResult TryDig(ulong id, Vector3 worldPos, in ToolTier tool)
    {
        if (_digStump == null || !_digStump(id))
            return HarvestResult.AlreadyHarvested;
        var yield = new HarvestYield("Wood", 1);
        _grantItem(yield.ItemId, yield.Count);
        EventBus<ScatterHarvestedEvent>.Raise(new ScatterHarvestedEvent(id, -1, worldPos, yield));
        return HarvestResult.Felled(yield);
    }

    // A creature is a harvest node with legs: same choke point, same inventory, same one blow per press. The
    // creature system owns its health and its death record; this owns the credit and the announcement.
    public HarvestResult TryStrike(EntityId creatureId, Vector3 fromWorldPos, in ToolTier tool)
    {
        if (_strikeCreature == null) return HarvestResult.NotHarvestable;

        CreatureStrike s = _strikeCreature(creatureId, tool.Damage, fromWorldPos);
        if (!s.Hit) return HarvestResult.AlreadyHarvested;

        if (s.Killed && s.Yield.Count > 0) _grantItem(s.Yield.ItemId, s.Yield.Count);
        EventBus<CreatureStruckEvent>.Raise(new CreatureStruckEvent(
            creatureId.Value, s.Position, s.DisplayName, s.RemainingHealth, s.Killed, s.Yield));
        return s.Killed ? HarvestResult.Felled(s.Yield) : HarvestResult.Hit;
    }

    public HarvestResult TryHarvest(in ScatterPick pick, in ToolTier tool)
    {
        ProtoHarvestInfo info = _protoInfo(pick.ProtoIndex);
        if (info.Interaction == ScatterInteraction.None)
            return HarvestResult.NotHarvestable;

        if (tool.Damage < DefaultNodeHp)
        {
            EventBus<HarvestHitEvent>.Raise(
                new HarvestHitEvent(pick.Id, pick.ProtoIndex, pick.Position, DefaultNodeHp - tool.Damage));
            return HarvestResult.Hit;
        }

        // Persist BEFORE the event: the fall and stump systems read the felled instance's transform back out
        // of the record, so the record has to exist by the time they run.
        if (!_persistHarvest(pick))
            return HarvestResult.AlreadyHarvested;

        _removeFromDraw(pick.ProtoIndex, pick.Id);
        HarvestYield yield = ResolveYield(info);
        if (yield.Count > 0) _grantItem(yield.ItemId, yield.Count);
        EventBus<ScatterHarvestedEvent>.Raise(new ScatterHarvestedEvent(pick.Id, pick.ProtoIndex, pick.Position, yield));
        return HarvestResult.Felled(yield);
    }

    // ponytail: one hardcoded mapping for the POC. Data-drive a per-prototype yield table later, and split
    // Chop (tree → logs → wood) from Collect (the plant itself) into distinct yields.
    static HarvestYield ResolveYield(in ProtoHarvestInfo info) => info.Interaction switch
    {
        ScatterInteraction.Chop => new HarvestYield("Wood", 3),
        ScatterInteraction.Collect => new HarvestYield(string.IsNullOrEmpty(info.DisplayName) ? "Plant" : info.DisplayName, 1),
        _ => default,
    };
}

// The prototype facts the harvest verb needs, looked up from ScatterLibraryDto by the caller.
public readonly struct ProtoHarvestInfo
{
    public readonly ScatterInteraction Interaction;
    public readonly string DisplayName;
    public ProtoHarvestInfo(ScatterInteraction interaction, string displayName)
    { Interaction = interaction; DisplayName = displayName; }
}

public enum HarvestOutcome { NotHarvestable, Hit, AlreadyHarvested, Felled }

public readonly struct HarvestResult
{
    public readonly HarvestOutcome Outcome;
    public readonly HarvestYield Yield;
    HarvestResult(HarvestOutcome outcome, HarvestYield yield) { Outcome = outcome; Yield = yield; }

    public static readonly HarvestResult NotHarvestable = new HarvestResult(HarvestOutcome.NotHarvestable, default);
    public static readonly HarvestResult Hit = new HarvestResult(HarvestOutcome.Hit, default);
    public static readonly HarvestResult AlreadyHarvested = new HarvestResult(HarvestOutcome.AlreadyHarvested, default);
    public static HarvestResult Felled(HarvestYield yield) => new HarvestResult(HarvestOutcome.Felled, yield);
}
