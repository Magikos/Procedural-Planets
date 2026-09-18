using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class CreatureResidencyService
{
    sealed class EcologyState
    {
        public CreatureSpeciesDto Species;
        public ActorAttackDefinition Attack;
        public CreatureResourceTarget Food;
        public EntityId Prey, BlockedTarget, Threat;
        public Vector3 PreyPosition, ProgressPosition, ThreatPosition;
        public float PreyUncertainty;
        public bool DirectPrey, PreyAlert, DirectThreat;
        public float ThreatUncertainty;
        public double NextScan, BlockedUntil, ProgressSeconds, FeedingSeconds;
    }

    readonly List<(EntityId attacker, EntityId target, ActorAttackDefinition attack)> _ecologyHits = new();

    static double MeatCapacity(CreatureSpeciesDto species) => (species?.EffectiveBodyMassKg ?? 20d) / 20d;

    void PrepareEcologySenses(Resident r, CreatureSpeciesDto species, long now, ref CreatureSenses senses)
    {
        if (r.Perception == null) return;
        var state = r.Ecology ??= new EcologyState { ProgressPosition = r.Position };
        if (!ReferenceEquals(state.Species, species))
        {
            state.Species = species;
            state.Attack = new ActorAttackDefinition(1d, Mathf.Max(.8f, species.BodyHeightMeters * 1.5f),
                50f, .25f, .2f, 1.1f, .1d, 0d, 0d);
            state.NextScan = 0;
        }
        if (_resourceTime >= state.NextScan)
        {
            // Stable phases spread later scans across frames after a group promotes together.
            double phase = (r.Id.Value % 97UL) / 194d;
            double interval = CreatureSimulationPolicy.PerceptionInterval(r.Detail);
            state.NextScan = Math.Floor((_resourceTime - phase) / interval + 1d) * interval + phase;
            ScanEcology(r, species, state, now, senses.Up);
        }
        if (r.Flight != null)
        {
            senses.Carrion = state.Food;
            if (state.Food.Available) senses.Food = state.Food;
            return;
        }
        senses.PerceptionLimited = true;
        senses.HasThreat = !state.Threat.IsNone;
        senses.ThreatId = state.Threat;
        senses.ThreatPosition = state.ThreatPosition;
        senses.ThreatDistance = Vector3.Distance(r.Position, state.ThreatPosition);
        senses.ThreatUncertainty = state.ThreatUncertainty;
        senses.DirectThreat = state.DirectThreat;
        if (now < r.AlarmUntilUnix)
        {
            senses.HasThreat = true;
            senses.ThreatPosition = r.AlarmFrom;
            senses.ThreatDistance = Vector3.Distance(r.Position, r.AlarmFrom);
        }
        if ((species.Diet & ResourceKind.Meat) == 0) return;
        senses.Attack = state.Attack;
        senses.CanAttack = true;
        senses.AttackDuration = state.Attack.Duration;
        senses.AttackHitTime = state.Attack.Windup;
        senses.HasPrey = !state.Prey.IsNone;
        senses.PreyId = state.Prey;
        senses.PreyPosition = state.PreyPosition;
        senses.PreyUncertainty = state.PreyUncertainty;
        senses.DirectPrey = state.DirectPrey;
        senses.PreyAlert = state.PreyAlert;
        if (state.Food.Available) senses.Food = state.Food;
    }

    void ScanEcology(Resident r, CreatureSpeciesDto species, EcologyState state, long now, Vector3 up)
    {
        EntityId previous = state.Prey;
        state.Prey = default;
        state.DirectPrey = false;
        state.Food = default;
        float light = _celestial == null ? 1f : Mathf.Clamp01(Vector3.Dot(up, _celestial.SunDirection) * 4f + .5f);
        state.Threat = default;
        float bestThreat = float.MaxValue;
        foreach (var source in _threats.Sources)
        {
            if (r.Flight != null) break;
            if (source.Id == r.Id) continue;
            bool largerPredator = species.Faction == CreatureFaction.Predator
                && source.Faction == CreatureFaction.Predator
                && _threats.SeenAs(source, now) == CreatureFaction.Predator
                && TryLiveEcologyResident(source.Id, out var predator)
                && predator.SpeciesIndex != r.SpeciesIndex
                && _library.At(predator.SpeciesIndex) is { } predatorSpecies
                && predatorSpecies.EffectiveBodyMassKg > species.EffectiveBodyMassKg * 2d;
            if (!largerPredator && !_threats.IsFeared(species.Faction, source, now)) continue;
            bool moving = TryLiveEcologyResident(source.Id, out var other)
                && other.Behaviour is CreatureBehaviour.Wander or CreatureBehaviour.Flee or CreatureBehaviour.Chase;
            if (!ObserveEcology(r, source.Id, ActorObservationKind.Threat, source.Position, up, light, now,
                moving, out var observation, out bool direct)) continue;
            float distance = Vector3.Distance(r.Position, observation.Position);
            if (distance >= bestThreat) continue;
            bestThreat = distance;
            state.Threat = source.Id; state.ThreatPosition = observation.Position;
            state.ThreatUncertainty = observation.Uncertainty; state.DirectThreat = direct;
        }
        if ((species.Diet & ResourceKind.Meat) == 0) return;
        float bestFood = float.MaxValue;
        if (_corpses != null)
        {
            foreach (var corpse in _corpses.All)
            {
                if (_corpses.RemainingMeat(corpse.Id, now, MeatCapacity(_library.At(corpse.SpeciesIndex))) <= 0) continue;
                if (!ObserveEcology(r, corpse.Id, ActorObservationKind.Food, corpse.Position, up, light, now, false, out var observation, out _)) continue;
                float distance = Vector3.Distance(r.Position, observation.Position);
                if (distance >= bestFood || r.Flight == null && _resources != null && !_resources.CanApproach(r.Position, observation.Position)) continue;
                bestFood = distance;
                state.Food = new CreatureResourceTarget { Id = corpse.Id, SourceKey = corpse.Id.Value,
                    Kind = ResourceKind.Meat, Available = true, Position = observation.Position, Uncertainty = observation.Uncertainty };
            }
        }
        // A discovered meal costs less than a hunt. Do not acquire live prey while that meal remains reachable.
        if (state.Food.Available || r.Flight != null) return;
        float bestPrey = float.MaxValue;
        foreach (var source in _threats.Sources)
        {
            if (source.Id == r.Id || !_threats.IsHostile(species.Faction, source, now)
                || source.Id == state.BlockedTarget && _resourceTime < state.BlockedUntil
                || !TryLiveEcologyResident(source.Id, out var prey)) continue;
            var preySpecies = _library.At(prey.SpeciesIndex);
            if (preySpecies == null || preySpecies.CruiseAltitudeMeters > 0f
                || (species.MaximumPreyMassRatio > 0f
                    ? preySpecies.EffectiveBodyMassKg > species.EffectiveBodyMassKg * species.MaximumPreyMassRatio
                    : preySpecies.BodyHeightMeters > species.BodyHeightMeters * 2f)) continue;
            if (!ObserveEcology(r, prey.Id, ActorObservationKind.Actor, prey.Position, up, light, now,
                prey.Behaviour is CreatureBehaviour.Wander or CreatureBehaviour.Flee or CreatureBehaviour.Chase,
                out var observation, out bool direct)) continue;
            float distance = Vector3.Distance(r.Position, observation.Position);
            float score = distance * (prey.Id == previous ? .8f : 1f);
            if (distance > 20f || score >= bestPrey
                || _resources != null && !_resources.CanApproach(r.Position, observation.Position)) continue;
            // Unidentified noise can guide a search, but cannot identify edible prey by its hidden registry entry.
            if (!direct && observation.IdentityConfidence < .5f) continue;
            bestPrey = score;
            state.Prey = prey.Id; state.PreyPosition = observation.Position;
            state.PreyUncertainty = observation.Uncertainty; state.DirectPrey = direct;
            state.PreyAlert = direct && prey.Behaviour == CreatureBehaviour.Flee;
        }
    }

    bool ObserveEcology(Resident r, EntityId id, ActorObservationKind kind, Vector3 position,
        Vector3 up, float light, long now, bool moving, out ActorKnowledge.Observation observation, out bool direct)
    {
        direct = false;
        var sight = new ActorStimulus(id.Value, kind, ActorSense.Sight, position, 1f, now, now + 1d);
        if (r.Perception.WithinRange(r.Position, r.Forward, up, light, sight))
            direct = r.Perception.Observe(r.Id.Value, r.Position, r.Forward, up, light,
                !_threats.HasTerrainSight(r.Position, position), sight, now, r.ResourceMemory);
        if (!direct)
        {
            var smell = new ActorStimulus(id.Value, kind, ActorSense.Smell, position, 1f, now, now + 2d);
            r.Perception.Observe(r.Id.Value, r.Position, r.Forward, up, light, false, smell, now, r.ResourceMemory);
            if (moving)
            {
                var sound = new ActorStimulus(id.Value, kind, ActorSense.Hearing, position, .5f, now, now + 1d);
                if (r.Perception.WithinRange(r.Position, r.Forward, up, light, sound))
                    r.Perception.Observe(r.Id.Value, r.Position, r.Forward, up, light,
                        !_threats.HasTerrainSight(r.Position, position), sound, now, r.ResourceMemory);
            }
        }
        return r.ResourceMemory.TryGet(id.Value, kind, now, out observation);
    }

    bool TryLiveEcologyResident(EntityId id, out Resident resident)
    {
        resident = null;
        return CreatureKey.IsCreature(id) && _bySlot.TryGetValue(CreatureKey.SlotOf(id).Value, out resident)
            && resident.Id == id && resident.IsLive && resident.Health > 0;
    }

    void FinishEcologyStep(Resident r, CreatureSpeciesDto species, CreatureSenses senses, float dt, long now)
    {
        var state = r.Ecology;
        if (state == null) return;
        if (!r.Driver.Swimming && r.Behaviour == CreatureBehaviour.Feed && state.Food.Available && _corpses != null
            && (r.Flight == null || r.Flight.HasSupport && r.Flight.AltitudeMeters <= .01f)
            && _corpses.TryGet(state.Food.Id, out var corpse)
            && Vector3.Distance(r.Position, corpse.Position) <= CreatureConsumeState.Reach + species.BodyHeightMeters * .5f
            && _threats.HasTerrainSight(r.Position, corpse.Position)
            && (_resources == null || _resources.CanApproach(r.Position, corpse.Position)))
        {
            state.FeedingSeconds += dt;
            if (state.FeedingSeconds >= 1d)
            {
                double amount = _corpses.ConsumeMeat(corpse.Id, now, MeatCapacity(_library.At(corpse.SpeciesIndex)),
                    Math.Min(r.Needs.Hunger, species.ConsumeUnitsPerSecond * state.FeedingSeconds));
                state.FeedingSeconds = 0;
                r.Needs = new ActorNeeds(Math.Max(0d, r.Needs.Hunger - amount), r.Needs.Thirst);
                if (amount <= 0) { state.Food = default; state.NextScan = 0; }
            }
        }
        else state.FeedingSeconds = 0;
        if (r.Brain.HitRequested && !senses.PreyId.IsNone)
        {
            r.Brain.ConfirmHit();
            if (!r.Driver.Swimming) _ecologyHits.Add((r.Id, senses.PreyId, state.Attack));
        }
        if (r.Behaviour is CreatureBehaviour.Chase or CreatureBehaviour.Stalk)
        {
            state.ProgressSeconds += dt;
            if (state.ProgressSeconds >= 3d)
            {
                if ((r.Position - state.ProgressPosition).sqrMagnitude < .25f)
                {
                    r.Brain.ReportHuntFailure(15d);
                    state.BlockedTarget = state.Prey; state.BlockedUntil = _resourceTime + 15d;
                    state.Prey = default; state.NextScan = 0;
                }
                state.ProgressPosition = r.Position; state.ProgressSeconds = 0;
            }
        }
        else { state.ProgressSeconds = 0; state.ProgressPosition = r.Position; }
    }

    void ApplyEcologyHits()
    {
        long now = NowUnixSeconds();
        foreach (var hit in _ecologyHits)
        {
            if (!TryLiveEcologyResident(hit.attacker, out var attacker) || !TryLiveEcologyResident(hit.target, out var target)) continue;
            var species = _library.At(attacker.SpeciesIndex);
            var targetSpecies = _library.At(target.SpeciesIndex);
            if (species == null || targetSpecies == null || !_threats.IsHostile(species.Faction,
                new ThreatSource(target.Id, target.Position, targetSpecies.Faction), now)) continue;
            Vector3 up = attacker.Driver.Pose.Up;
            float bearing = CharacterMath.TangentBearing(attacker.Position, attacker.Forward, up, target.Position);
            if (!hit.attack.InContact(Vector3.Distance(attacker.Position, target.Position), bearing)
                || !_threats.HasTerrainSight(attacker.Position, target.Position)
                || _resources != null && !_resources.CanApproach(attacker.Position, target.Position)) continue;
            if (Strike(target.Id, (int)hit.attack.Damage, attacker.Position).Killed)
                for (int i = _live.Count - 1; i >= 0; i--)
                    if (_live[i].Id == target.Id) _live.RemoveAt(i);
        }
        _ecologyHits.Clear();
    }
}
