using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Commits harvest damage and resource effects independently of animation playback.</summary>
public sealed class HarvestService
{
    readonly Func<ScatterPick, bool> _persistHarvest; // record the fell; false if already harvested
    readonly Action<int, ulong> _removeFromDraw;     // drop the instance from the draw this frame
    readonly Action<string, int> _grantItem;         // credit the inventory
    readonly Func<int, ProtoHarvestInfo> _protoInfo; // prototype interaction + display name
    readonly Func<ulong, bool> _digStump;            // dig a stump (Stump -> Dug); false if no stump there
    readonly Func<EntityId, int, Vector3, CreatureStrike> _strikeCreature; // wound or kill an animal
    readonly Func<ulong, int?> _readHealth;
    readonly Action<ulong, int> _writeHealth;

    // Partial damage is session-local. Hosts that reuse a service after world reset must clear it.
    readonly Dictionary<ulong, int> _remainingHealth = new();
    public void ResetProgress() => _remainingHealth.Clear();

    public HarvestService(Func<ScatterPick, bool> persistHarvest, Action<int, ulong> removeFromDraw,
        Action<string, int> grantItem, Func<int, ProtoHarvestInfo> protoInfo, Func<ulong, bool> digStump,
        Func<EntityId, int, Vector3, CreatureStrike> strikeCreature = null,
        Func<ulong, int?> readHealth = null, Action<ulong, int> writeHealth = null)
    {
        _persistHarvest = persistHarvest;
        _removeFromDraw = removeFromDraw;
        _grantItem = grantItem;
        _protoInfo = protoInfo;
        _digStump = digStump;
        _strikeCreature = strikeCreature;
        _readHealth = readHealth; _writeHealth = writeHealth;
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
        if (!s.Hit || s.Outcome == CreatureStrikeOutcome.NothingLeft) return HarvestResult.AlreadyHarvested;

        // Granted on the outcome that CARRIES a yield, not on the kill. A killing blow leaves the hide on the
        // body; taking it off the carcass is the separate action that credits it.
        if (s.Yield.Count > 0) _grantItem(s.Yield.ItemId, s.Yield.Count);
        EventBus<CreatureStruckEvent>.Raise(new CreatureStruckEvent(
            creatureId.Value, s.Position, s.DisplayName, s.RemainingHealth, s.Killed, s.Yield));
        return s.Outcome == CreatureStrikeOutcome.Wounded
            ? HarvestResult.Hit
            : HarvestResult.Felled(s.Yield);
    }

    public HarvestResult TryHarvest(in ScatterPick pick, in ToolTier tool)
    {
        if (tool.Damage <= 0) throw new ArgumentOutOfRangeException(nameof(tool), "Harvest damage must be positive.");
        ProtoHarvestInfo info = _protoInfo(pick.ProtoIndex);
        if (info.Interaction == ScatterInteraction.None)
            return HarvestResult.NotHarvestable;

        float scale = ScatterHarvestStore.StoredScaleOr(pick.Scale);
        int initialHp = info.ScaleWithInstance ? Math.Max(1, Mathf.CeilToInt(info.HitPoints * scale * scale)) : info.HitPoints;
        int health;
        if (_readHealth != null) health = _readHealth(pick.Id) ?? initialHp;
        else if (!_remainingHealth.TryGetValue(pick.Id, out health)) health = initialHp;
        if (health == 0) return HarvestResult.AlreadyHarvested;
        int remaining = Math.Max(0, health - tool.Damage);
        if (remaining > 0)
        {
            if (_readHealth == null) _remainingHealth[pick.Id] = remaining;
            _writeHealth?.Invoke(pick.Id, remaining);
            EventBus<HarvestHitEvent>.Raise(
                new HarvestHitEvent(pick.Id, pick.ProtoIndex, pick.ImpactPoint ?? pick.Position, remaining, pick.ImpactPoint.HasValue));
            return HarvestResult.Hit;
        }

        // Persist BEFORE the event: the fall and stump systems read the felled instance's transform back out
        // of the record, so the record has to exist by the time they run.
        if (!_persistHarvest(pick))
        {
            return HarvestResult.AlreadyHarvested;
        }

        if (_readHealth == null) _remainingHealth[pick.Id] = 0;

        _removeFromDraw(pick.ProtoIndex, pick.Id);
        HarvestYield yield = info.FellYield ?? ResolveYield(info);
        if (info.ScaleWithInstance && yield.Count > 0)
            yield = new HarvestYield(yield.ItemId, Math.Max(1, Mathf.RoundToInt(yield.Count * scale * scale * scale)));
        if (yield.Count > 0) _grantItem(yield.ItemId, yield.Count);
        EventBus<ScatterHarvestedEvent>.Raise(new ScatterHarvestedEvent(pick.Id, pick.ProtoIndex, pick.Position, yield));
        return HarvestResult.Felled(yield);
    }

    // ponytail: one hardcoded mapping for the POC. Data-drive a per-prototype yield table later, and split
    // Chop (tree → logs → wood) from Collect (the plant itself) into distinct yields.
    static HarvestYield ResolveYield(in ProtoHarvestInfo info) => info.Interaction switch
    {
        ScatterInteraction.Chop => new HarvestYield("Wood", 3),
        ScatterInteraction.Mine => new HarvestYield("Stone", 3),
        ScatterInteraction.Collect => new HarvestYield(string.IsNullOrEmpty(info.DisplayName) ? "Plant" : info.DisplayName, 1),
        _ => default,
    };
}

// The prototype facts the harvest verb needs, looked up from ScatterLibraryDto by the caller.
public readonly struct ProtoHarvestInfo
{
    public readonly ScatterInteraction Interaction;
    public readonly string DisplayName;
    public readonly int HitPoints;
    public readonly HarvestYield? FellYield;
    public readonly bool ScaleWithInstance;
    public ProtoHarvestInfo(ScatterInteraction interaction, string displayName, int hitPoints = 1, HarvestYield? fellYield = null, bool scaleWithInstance = false)
    {
        if (hitPoints < 1) throw new ArgumentOutOfRangeException(nameof(hitPoints));
        Interaction = interaction; DisplayName = displayName; HitPoints = hitPoints;
        FellYield = fellYield;
        ScaleWithInstance = scaleWithInstance;
    }
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
