using System.Collections.Generic;
using UnityEngine;

// The one call the player host makes on Interact. Picks the nearest harvest target along the look ray — a
// standing harvestable tree, a stump, OR a creature — and runs the matching verb: axe fells a tree (→ stump),
// shovel digs a stump (→ gone), club wounds an animal until it drops. The tool is chosen by what you aim at
// (no equip UI yet). Logs every attempt so a press always gives feedback.
public sealed class HarvestInteractor
{
    readonly ScatterPicker _picker;
    readonly HarvestService _harvest;
    readonly ScatterHarvestStore _store;
    readonly CreatureResidencyService _creatures;
    readonly ILogger _log;
    readonly List<ScatterHarvestStore.HarvestNode> _stumpScratch = new();
    readonly List<Vector3> _positionScratch = new();

    public HarvestInteractor(ScatterPicker picker, HarvestService harvest, ScatterHarvestStore store,
        ILogger log = null, CreatureResidencyService creatures = null)
    {
        _picker = picker;
        _harvest = harvest;
        _store = store;
        _log = log;
        _creatures = creatures;
    }

    // Returns true if something was harvested (felled, dug, or killed) this call.
    public bool TryHarvestLookedAt(Ray ray, float reachMeters, float maxPerpMeters)
    {
        if (_harvest == null)
        {
            _log?.Log(LogLevel.Info, "Harvest", "harvesting unavailable (interactor not wired)");
            return false;
        }

        ScatterPick tree = default;
        bool hasTree = _picker != null &&
            _picker.TryPick(ray, reachMeters, maxPerpMeters, out tree);
        bool hasStump = TryPickStump(ray, reachMeters, maxPerpMeters, out ulong stumpId, out Vector3 stumpPos);
        bool hasCreature = TryPickCreature(ray, reachMeters, maxPerpMeters,
            out EntityId creatureId, out Vector3 creaturePos);

        if (!hasTree && !hasStump && !hasCreature)
        {
            _log?.Log(LogLevel.Info, "Harvest",
                "nothing harvestable in aim — put the crosshair on a tree, a stump base, or an animal");
            return false;
        }

        // Nearest to the ray ORIGIN wins, so a deer standing in front of a tree takes the blow. Ties go to the
        // animal: it is the thing that walks away while you swing at the scenery behind it.
        float treeD = hasTree ? (tree.Position - ray.origin).sqrMagnitude : float.MaxValue;
        float stumpD = hasStump ? (stumpPos - ray.origin).sqrMagnitude : float.MaxValue;
        float creatureD = hasCreature ? (creaturePos - ray.origin).sqrMagnitude : float.MaxValue;

        if (creatureD <= treeD && creatureD <= stumpD)
            return Strike(creatureId, ray.origin);

        bool dig = stumpD < treeD;
        HarvestResult r = dig
            ? _harvest.TryDig(stumpId, stumpPos, ToolTier.Shovel)
            : _harvest.TryHarvest(tree, ToolTier.BasicAxe);

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

    bool Strike(EntityId id, Vector3 from)
    {
        HarvestResult r = _harvest.TryStrike(id, from, ToolTier.Club);
        switch (r.Outcome)
        {
            case HarvestOutcome.Felled:
                _log?.Log(LogLevel.Info, "Harvest", $"Killed it (+{r.Yield.Count} {r.Yield.ItemId})");
                return true;
            case HarvestOutcome.Hit:
                _log?.Log(LogLevel.Info, "Harvest", "Hit it — it bolts");
                return false;
            default:
                _log?.Log(LogLevel.Info, "Harvest", "the animal is already gone");
                return false;
        }
    }

    bool TryPickStump(Ray ray, float reach, float perp, out ulong id, out Vector3 pos)
    {
        id = 0;
        pos = default;
        if (_store == null) return false;
        _store.CollectStumps(_stumpScratch);
        if (_stumpScratch.Count == 0) return false;

        _positionScratch.Clear();
        for (int i = 0; i < _stumpScratch.Count; i++) _positionScratch.Add(_stumpScratch[i].Position);
        int idx = ScatterPickMath.NearestAlongRay(ray, _positionScratch, reach, perp, out _);
        if (idx < 0) return false;

        id = _stumpScratch[idx].Id;
        pos = _stumpScratch[idx].Position;
        return true;
    }

    bool TryPickCreature(Ray ray, float reach, float perp, out EntityId id, out Vector3 pos)
    {
        id = EntityId.None;
        pos = default;
        if (_creatures == null) return false;

        IReadOnlyList<CreatureResidencyService.LiveCreature> live = _creatures.Live;
        if (live.Count == 0) return false;

        // Aim at the BODY, not at the feet. A creature's position is its ground contact, and a crosshair on a
        // deer's flank sits most of a body height above that — far enough to miss at the perpendicular
        // tolerance a tree trunk is picked with.
        CreatureLibraryDto library = _creatures.Library;
        _positionScratch.Clear();
        for (int i = 0; i < live.Count; i++)
        {
            float height = library?.At(live[i].SpeciesIndex)?.BodyHeightMeters ?? 1f;
            _positionScratch.Add(live[i].Position + live[i].Up * height * 0.5f);
        }

        int idx = ScatterPickMath.NearestAlongRay(ray, _positionScratch, reach, perp, out _);
        if (idx < 0) return false;

        id = live[idx].Id;
        pos = _positionScratch[idx];
        return true;
    }
}
