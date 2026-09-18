using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Water-constrained groups. A single member uses the same swimming rules.</summary>
public sealed class FishSchool
{
    readonly IWaterQueryService _water;
    readonly FishSpecies _species;
    readonly EntityId _id;
    internal readonly FishAgentState[] Agents;
    internal FishSchoolState State;
    public IReadOnlyList<Vector3> Positions { get; }
    public IReadOnlyList<Vector3> Headings { get; }
    public IReadOnlyList<Vector3> Normals { get; }
    public ushort BodyId => State.Body;
    public bool IsViable => State.Viable;
    public int FleeingCount => State.Fleeing;

    public FishSchool(IWaterQueryService water, IReadOnlyList<Vector3> positions, Vector3 forward,
        FishSpecies species = null, EntityId id = default)
    {
        if (positions == null || positions.Count < 1 || positions.Count > 64)
            throw new ArgumentException("A fish group needs between one and 64 initial positions.", nameof(positions));
        if (water == null || !CharacterMath.IsFinite(positions[0]) || !water.TryGetWaterSurface(positions[0], out WaterSample sample))
            throw new ArgumentException("Fish must start in water.", nameof(water));
        if (!CharacterMath.IsFinite(forward) || forward.sqrMagnitude < .001f)
            throw new ArgumentException("Fish need a finite initial heading.", nameof(forward));
        _water = water; _species = species; _id = id;
        State = new FishSchoolState { Body = sample.BodyId, Rules = FishRules.From(species), Heading = forward.normalized, Viable = true };
        if (!(State.Rules.Speed > 0f) || float.IsInfinity(State.Rules.Speed))
            throw new ArgumentException("Fish need a finite positive speed.", nameof(species));
        Agents = new FishAgentState[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            if (!FishMovement.TryGetHabitat(water, positions[i], State.Body, State.Rules.Clearance, out var local, species))
                throw new ArgumentException("Every fish must start inside its species habitat and the same water body.", nameof(positions));
            Agents[i] = new FishAgentState { Position = positions[i], Heading = State.Heading, Normal = local.Normal, Depth = local.SignedDepth };
        }
        Positions = new VectorView(Agents, 0); Headings = new VectorView(Agents, 1); Normals = new VectorView(Agents, 2);
    }

    public void Tick(float dt, ThreatRegistry threats, long now)
    {
        if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f || dt > 10f) throw new ArgumentOutOfRangeException(nameof(dt));
        FishSimulation.Tick(ref State, new ManagedBuffer { Agents = Agents }, new ManagedWaterQuery(_water),
            new ThreatQuery { Registry = threats, Species = _species, Id = _id, Now = now }, dt);
    }

    internal ThreatQuery CaptureThreatQuery(ThreatRegistry threats, long now) =>
        new() { Registry = threats, Species = _species, Id = _id, Now = now };

    internal struct ThreatQuery : IFishThreatQuery
    {
        public ThreatRegistry Registry;
        public FishSpecies Species;
        public EntityId Id;
        public long Now;
        public bool TryFind(int index, Vector3 position, out Vector3 threat)
        {
            threat = default;
            if (Registry == null || !Registry.TryFindThreat(position, Id, Species?.Faction ?? CreatureFaction.Wildlife,
                Species?.Awareness ?? 8f, Now, out var found,
                Species?.Skittish == false ? null : Species?.AvoidanceClass ?? "SmallFish")) return false;
            threat = found.Position; return true;
        }
    }
    struct ManagedBuffer : IFishStateBuffer
    {
        public FishAgentState[] Agents;
        public int Count => Agents.Length;
        public FishAgentState this[int index] { get => Agents[index]; set => Agents[index] = value; }
    }
    sealed class VectorView : IReadOnlyList<Vector3>
    {
        readonly FishAgentState[] _agents;
        readonly int _field;
        public VectorView(FishAgentState[] agents, int field) { _agents = agents; _field = field; }
        public int Count => _agents.Length;
        public Vector3 this[int index] => _field == 0 ? _agents[index].Position : _field == 1 ? _agents[index].Heading : _agents[index].Normal;
        public IEnumerator<Vector3> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
