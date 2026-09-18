using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Creature Perception")]
public sealed class CreaturePerceptionSettings : ScriptableObject
{
    public enum Setting { DaySight, NightSight, ViewAngle, HearingRange, HearingThreshold, SmellRange,
        SmellThreshold, SightResolution, HearingResolution, SmellResolution, MemorySeconds, UncertaintyGrowth }
    [Serializable] public struct Override { public Setting Setting; public float Value; }
    [Tooltip("Optional shared defaults. Only listed values override the parent. Cycles are invalid.")]
    public CreaturePerceptionSettings Parent;
    public Override[] Overrides = Array.Empty<Override>();
    public ActorPerceptionProfile Snapshot() => Snapshot(new HashSet<CreaturePerceptionSettings>());
    ActorPerceptionProfile Snapshot(HashSet<CreaturePerceptionSettings> visited)
    {
        if (!visited.Add(this)) throw new InvalidOperationException("Perception profile inheritance contains a cycle.");
        var profile = Parent != null ? Parent.Snapshot(visited) : new ActorPerceptionProfile();
        var applied = new HashSet<Setting>();
        foreach (var item in Overrides ?? Array.Empty<Override>())
        {
            if (!applied.Add(item.Setting)) throw new InvalidOperationException("Duplicate perception override: " + item.Setting);
            profile = item.Setting switch
            {
                Setting.DaySight => profile with { DaySight = item.Value },
                Setting.NightSight => profile with { NightSight = item.Value },
                Setting.ViewAngle => profile with { ViewAngle = item.Value },
                Setting.HearingRange => profile with { HearingRange = item.Value },
                Setting.HearingThreshold => profile with { HearingThreshold = item.Value },
                Setting.SmellRange => profile with { SmellRange = item.Value },
                Setting.SmellThreshold => profile with { SmellThreshold = item.Value },
                Setting.SightResolution => profile with { SightResolution = item.Value },
                Setting.HearingResolution => profile with { HearingResolution = item.Value },
                Setting.SmellResolution => profile with { SmellResolution = item.Value },
                Setting.MemorySeconds => profile with { MemorySeconds = item.Value },
                Setting.UncertaintyGrowth => profile with { UncertaintyGrowth = item.Value },
                _ => throw new ArgumentOutOfRangeException(nameof(item.Setting))
            };
        }
        return profile.Validate();
    }
}
