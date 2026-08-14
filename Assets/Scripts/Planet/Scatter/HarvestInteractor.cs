using System.Collections.Generic;
using UnityEngine;

// The one call the player host makes on Interact. Picks the nearest harvest target along the look ray — a
// standing harvestable tree OR a stump — and runs the matching verb: axe fells a tree (→ stump), shovel digs
// a stump (→ gone). The tool is chosen by what you aim at (no equip UI yet). Logs every attempt so a press
// always gives feedback.
public sealed class HarvestInteractor
{
    readonly ScatterPicker _picker;
    readonly HarvestService _harvest;
    readonly ScatterHarvestStore _store;
    readonly ILogger _log;
    readonly List<ScatterHarvestStore.HarvestNode> _stumpScratch = new();
    readonly List<Vector3> _stumpPositions = new();

    public HarvestInteractor(ScatterPicker picker, HarvestService harvest, ScatterHarvestStore store, ILogger log = null)
    {
        _picker = picker;
        _harvest = harvest;
        _store = store;
        _log = log;
    }

    // Returns true if something was harvested (felled or dug) this call.
    public bool TryHarvestLookedAt(Ray ray, float reachMeters, float maxPerpMeters)
    {
        if (_harvest == null)
        {
            _log?.Log(LogLevel.Info, "Harvest", "harvesting unavailable (interactor not wired)");
            return false;
        }

        ulong treeId = 0;
        int treeProto = -1;
        Vector3 treePos = default;
        bool hasTree = _picker != null &&
            _picker.TryPick(ray, reachMeters, maxPerpMeters, out treeId, out treeProto, out treePos);
        bool hasStump = TryPickStump(ray, reachMeters, maxPerpMeters, out ulong stumpId, out Vector3 stumpPos);

        if (!hasTree && !hasStump)
        {
            _log?.Log(LogLevel.Info, "Harvest", "nothing harvestable in aim — put the crosshair on a tree or stump base");
            return false;
        }

        // The nearer target (to the ray origin) wins when both a tree and a stump are in aim.
        bool dig = hasStump && (!hasTree ||
            (stumpPos - ray.origin).sqrMagnitude < (treePos - ray.origin).sqrMagnitude);

        HarvestResult r = dig
            ? _harvest.TryDig(stumpId, stumpPos, ToolTier.Shovel)
            : _harvest.TryHarvest(treeId, treeProto, ToolTier.BasicAxe, treePos);

        switch (r.Outcome)
        {
            case HarvestOutcome.Felled:
                _log?.Log(LogLevel.Info, "Harvest",
                    dig ? $"Dug up the stump (+{r.Yield.Count} {r.Yield.ItemId})" : $"Chopped {r.Yield.Count}x {r.Yield.ItemId}");
                break;
            case HarvestOutcome.AlreadyHarvested:
                _log?.Log(LogLevel.Info, "Harvest", dig ? "stump already gone" : "already harvested");
                break;
            case HarvestOutcome.NotHarvestable:
                _log?.Log(LogLevel.Info, "Harvest", "aimed instance is not harvestable");
                break;
        }
        return r.Outcome == HarvestOutcome.Felled;
    }

    bool TryPickStump(Ray ray, float reach, float perp, out ulong id, out Vector3 pos)
    {
        id = 0;
        pos = default;
        if (_store == null) return false;
        _store.CollectStumps(_stumpScratch);
        if (_stumpScratch.Count == 0) return false;

        _stumpPositions.Clear();
        for (int i = 0; i < _stumpScratch.Count; i++) _stumpPositions.Add(_stumpScratch[i].Position);
        int idx = ScatterPickMath.NearestAlongRay(ray, _stumpPositions, reach, perp, out _);
        if (idx < 0) return false;

        id = _stumpScratch[idx].Id;
        pos = _stumpScratch[idx].Position;
        return true;
    }
}
