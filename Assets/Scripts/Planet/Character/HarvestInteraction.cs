using System;
using UnityEngine;

/// <summary>Connects validated interaction impacts to the harvest service.</summary>
public sealed class HarvestInteraction
{
    readonly HarvestService _service;
    readonly Func<bool> _available;
    readonly ScatterPick _pick;
    readonly ToolTier _tool;
    public HarvestResult LastResult { get; private set; }
    public int Impacts { get; private set; }

    public HarvestInteraction(HarvestService service, ScatterPick pick, ToolTier tool, Func<bool> available)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _available = available ?? throw new ArgumentNullException(nameof(available));
        if (tool.Damage <= 0) throw new ArgumentOutOfRangeException(nameof(tool));
        _pick = pick; _tool = tool;
    }

    public HarvestResult Strike()
    {
        if (!_available()) return LastResult = HarvestResult.NotHarvestable;
        LastResult = _service.TryHarvest(_pick, _tool);
        if (LastResult.Outcome == HarvestOutcome.Hit || LastResult.Outcome == HarvestOutcome.Felled) Impacts++;
        return LastResult;
    }
}
