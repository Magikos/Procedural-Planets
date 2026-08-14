using UnityEngine;

// Ties the scatter picker to the harvest verb for the player host: given a look ray, find the nearest
// harvestable instance and harvest it. This is the single call the player controller makes on Interact.
// Logs the outcome so the POC has reliable console feedback alongside the instance vanishing and the
// ScatterHarvestedEvent (which future FX / HUD subscribe to).
public sealed class HarvestInteractor
{
    readonly ScatterPicker _picker;
    readonly HarvestService _harvest;
    readonly ILogger _log;

    public HarvestInteractor(ScatterPicker picker, HarvestService harvest, ILogger log = null)
    {
        _picker = picker;
        _harvest = harvest;
        _log = log;
    }

    // Returns true if an instance was felled this call. Logs every attempt so the POC gives feedback on a
    // press even when nothing is hit (so "F does nothing" can be told apart from "nothing in aim").
    public bool TryHarvestLookedAt(Ray ray, float reachMeters, float maxPerpMeters, in ToolTier tool)
    {
        if (_picker == null || _harvest == null)
        {
            _log?.Log(LogLevel.Info, "Harvest", "harvesting unavailable (interactor not wired)");
            return false;
        }
        if (!_picker.TryPick(ray, reachMeters, maxPerpMeters, out ulong id, out int protoIndex, out Vector3 pos))
        {
            _log?.Log(LogLevel.Info, "Harvest", "nothing harvestable in aim — put the crosshair on a tree base");
            return false;
        }

        HarvestResult r = _harvest.TryHarvest(id, protoIndex, tool, pos);
        switch (r.Outcome)
        {
            case HarvestOutcome.Felled:
                _log?.Log(LogLevel.Info, "Harvest", $"Chopped {r.Yield.Count}x {r.Yield.ItemId}");
                break;
            case HarvestOutcome.AlreadyHarvested:
                _log?.Log(LogLevel.Info, "Harvest", "already harvested");
                break;
            case HarvestOutcome.NotHarvestable:
                _log?.Log(LogLevel.Info, "Harvest", "aimed instance is not harvestable");
                break;
        }
        return r.Outcome == HarvestOutcome.Felled;
    }
}
