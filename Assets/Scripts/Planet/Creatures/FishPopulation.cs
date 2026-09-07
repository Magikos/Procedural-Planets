using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>World-owned aquatic simulation. Observation limits work; it never becomes a threat.</summary>
[CommandPrefix("fish")]
public sealed class FishPopulation : IDisposable
{
    public sealed class Group
    {
        public readonly EntityId Id;
        public readonly FishSpecies Species;
        public readonly FishSchool School;
        public Group(EntityId id, FishSpecies species, FishSchool school) { Id = id; Species = species; School = school; }
    }

    readonly IWaterQueryService _water;
    readonly ThreatRegistry _threats;
    readonly List<Group> _groups = new();
    readonly EntityIdAllocator _ids = new(EntityId.AquaticOwner);
    Vector3 _center;
    uint _seed, _attempt;
    float _scan;
    bool _configured;
    public IReadOnlyList<Group> Groups { get; }

    public FishPopulation(IWaterQueryService water, ThreatRegistry threats)
    {
        _water = water ?? throw new ArgumentNullException(nameof(water));
        _threats = threats;
        Groups = _groups.AsReadOnly();
        ConsoleRegistry.RegisterInstance(this);
    }

    public void Configure(int seed, Vector3 center)
    {
        Clear();
        _center = center;
        _seed = unchecked((uint)seed);
        _attempt = 0;
        _scan = 0;
        _configured = true;
    }

    public void Tick(Vector3 observer, float dt, long now)
    {
        if (!_configured || !CharacterMath.IsFinite(observer) || !CharacterMath.IsFinite(_center)) return;
        if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f || dt > 10f) throw new ArgumentOutOfRangeException(nameof(dt));
        for (int i = _groups.Count - 1; i >= 0; i--)
        {
            Group group = _groups[i];
            if ((group.School.Positions[0] - observer).sqrMagnitude <= 180f * 180f)
                group.School.Tick(dt, _threats, now);
            if (!group.School.IsViable || (group.School.Positions[0] - observer).sqrMagnitude > 180f * 180f)
            {
                _threats?.Withdraw(group.Id);
                _groups.RemoveAt(i);
            }
            else if (group.Species.Faction == CreatureFaction.Predator)
                _threats?.Report(group.Id, group.School.Positions[0], group.Species.Faction);
        }
        _scan -= dt;
        if (_scan > 0f) return;
        _scan = 1f;
        int small = 0, sharks = 0;
        foreach (Group group in _groups)
            if (group.Species.Faction == CreatureFaction.Predator) sharks++; else small++;
        // Bounded search and populations: six small groups plus one solitary shark per observation bubble.
        for (int attempt = 0; attempt < 12 && (small < 6 || sharks < 1); attempt++)
        {
            bool shark = small >= 6 || (sharks < 1 && attempt % 4 == 3);
            if (shark && sharks >= 1) continue;
            if (TrySpawn(observer, shark)) { if (shark) sharks++; else small++; }
        }
    }

    bool TrySpawn(Vector3 observer, bool shark)
    {
        uint hash = ScatterHash.Mix(_seed ^ ++_attempt);
        Vector3 radial = observer - _center;
        if (radial.sqrMagnitude < 1f) return false;
        Vector3 up = radial.normalized;
        Vector3 forward = Quaternion.AngleAxis(ScatterHash.To01(hash) * 360f, up) * CharacterMath.ArbitraryTangent(up);
        float radius = 12f + ScatterHash.To01(ScatterHash.Mix(hash)) * 90f;
        if (!_water.TryGetWaterSurface(observer + forward * radius, out var water)) return false;
        FishSpecies species = shark ? FishSpecies.Shark : water.IsOcean ? FishSpecies.Coastal : FishSpecies.Freshwater;
        if (!species.Accepts(water)) return false;
        float depth = Mathf.Min(water.BodyDepth * 0.45f, shark ? 12f : 4f);
        Vector3 anchor = water.SurfacePoint - water.Normal * depth;
        if (Vector3.Distance(anchor, observer) > 150f) return false;
        foreach (Group group in _groups)
            if (Vector3.Distance(group.School.Positions[0], anchor) < (shark ? 15f : 8f)) return false;
        int count = shark ? 1 : (hash % 3) switch { 0 => 1, 1 => 8, _ => 16 };
        var positions = new Vector3[count];
        Vector3 right = Vector3.Cross(water.Normal, forward).normalized;
        for (int i = 0; i < count; i++)
        {
            positions[i] = anchor + right * ((i % 4 - 1.5f) * 0.8f) + forward * (i / 4 * 0.8f);
            if (!FishMovement.IsHabitat(_water, positions[i], water.BodyId, species.Clearance, species)) return false;
        }
        EntityId id = _ids.Next();
        _groups.Add(new Group(id, species, new FishSchool(_water, positions, forward, species, id)));
        return true;
    }

    public void Clear()
    {
        foreach (Group group in _groups) _threats?.Withdraw(group.Id);
        _groups.Clear();
        _configured = false;
    }

    [ConsoleCommand("status", "Live aquatic group and fish counts.", MonoTargetType.Registry)]
    public string Status()
    {
        int fish = 0, sharks = 0;
        foreach (Group group in _groups) { fish += group.School.Positions.Count; if (group.Species == FishSpecies.Shark) sharks++; }
        return $"groups={_groups.Count} fish={fish} sharks={sharks}";
    }

    public void Dispose() { Clear(); ConsoleRegistry.UnregisterInstance(typeof(FishPopulation)); }
}
