using System;
using UnityEngine;

// The one verb choke point for harvesting (POC seam — see plans/003-harvest-vertical-slice.md §3b). Every
// harvest routes through TryHarvest, which today one-shots the node: persist + remove-from-draw + grant yield
// + raise events. Multi-hit chopping, axe-quality damage, fall animation, and log entities all slot in HERE
// without touching callers. Dependencies are injected as delegates so this stays a pure orchestrator that the
// player controller and tests wire independently.
public sealed class HarvestService
{
    readonly Func<ulong, int, Vector3, bool> _persistHarvest; // record fell (id, proto, pos); false if already
    readonly Action<int, ulong> _removeFromDraw;     // drop the instance from the draw this frame
    readonly Action<string, int> _grantItem;         // credit the inventory
    readonly Func<int, ProtoHarvestInfo> _protoInfo; // prototype interaction + display name

    // ponytail: node HP defaulted so one hit fells it. Multi-hit chopping adds a per-ScatterId hit accumulator
    // consulted here — compare tool.Damage against remaining HP, raise HarvestHitEvent until it reaches 0.
    const int DefaultNodeHp = 1;

    public HarvestService(Func<ulong, int, Vector3, bool> persistHarvest, Action<int, ulong> removeFromDraw,
        Action<string, int> grantItem, Func<int, ProtoHarvestInfo> protoInfo)
    {
        _persistHarvest = persistHarvest;
        _removeFromDraw = removeFromDraw;
        _grantItem = grantItem;
        _protoInfo = protoInfo;
    }

    public HarvestResult TryHarvest(ulong id, int protoIndex, in ToolTier tool, Vector3 worldPos)
    {
        ProtoHarvestInfo info = _protoInfo(protoIndex);
        if (info.Interaction == ScatterInteraction.None)
            return HarvestResult.NotHarvestable;

        if (tool.Damage < DefaultNodeHp)
        {
            EventBus<HarvestHitEvent>.Raise(new HarvestHitEvent(id, protoIndex, worldPos, DefaultNodeHp - tool.Damage));
            return HarvestResult.Hit;
        }

        if (!_persistHarvest(id, protoIndex, worldPos))
            return HarvestResult.AlreadyHarvested;

        _removeFromDraw(protoIndex, id);
        HarvestYield yield = ResolveYield(info);
        if (yield.Count > 0) _grantItem(yield.ItemId, yield.Count);
        EventBus<ScatterHarvestedEvent>.Raise(new ScatterHarvestedEvent(id, protoIndex, worldPos, yield));
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
