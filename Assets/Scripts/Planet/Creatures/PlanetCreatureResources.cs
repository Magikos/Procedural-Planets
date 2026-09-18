using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded resource queries over real scatter and water. No presentation objects or camera input.</summary>
public sealed class PlanetCreatureResources
{
    public const float MaximumWadingDepth = .2f;

    public static bool CanSettleHome(IWaterQueryService water, Vector3 groundPosition, CreatureSpeciesDto species)
    {
        if (species == null) throw new ArgumentNullException(nameof(species));
        // Negative altitude bands explicitly allow aquatic habitat. Fliers obtain their water floor from flight grounding.
        if (species.CruiseAltitudeMeters > 0f || species.MinAltitudeMeters < 0f || water == null) return true;
        return !water.TryGetWaterSurface(groundPosition, out var sample) ||
            float.IsFinite(sample.BodyDepth) && sample.BodyDepth <= MaximumWadingDepth;
    }

    public struct Target
    {
        public CreatureResourceTarget View;
        public Vector3 Contact;
        public ScatterPrototypeDto Foliage;
    }
    readonly ScatterField _scatter;
    readonly ScatterHarvestStore _harvest;
    readonly IWaterQueryService _water;
    readonly IPlanetSurfaceSampler _ground;
    readonly Vector3 _center;
    readonly List<ScatterInstance> _candidates = new();
    public FoliageFoodStore Food { get; }
    public double FoodConsumed { get; private set; }
    public double WaterConsumed { get; private set; }
    public double LastSearchMilliseconds { get; private set; }

    public async Awaitable<(Target food, Target water)> SearchAsync(ulong actor, Vector3 position, Vector3 forward,
        CreatureSpeciesDto species, ActorPerception perception, ActorKnowledge memory, float light, long now)
    {
        List<ScatterInstance> candidates = _scatter != null && (species.Diet & ResourceKind.Plants) != 0
            ? await _scatter.GatherFoodAsync(position, SearchRange(perception)) : new List<ScatterInstance>();
        Search(actor, position, forward, species, perception, memory, light, now, out var food, out var water, candidates);
        return (food, water);
    }

    static float SearchRange(ActorPerception perception) => Mathf.Min(24f,
        Mathf.Max(Mathf.Max(perception.Profile.DaySight, perception.Profile.NightSight), perception.Profile.SmellRange));

    public PlanetCreatureResources(ScatterField scatter, ScatterHarvestStore harvest, IWaterQueryService water,
        IPlanetSurfaceSampler ground, Vector3 center, IWorldDeltaLog delta)
    {
        _scatter = scatter; _harvest = harvest; _water = water; _ground = ground; _center = center;
        Food = new FoliageFoodStore(delta);
    }

    public void Search(ulong actor, Vector3 position, Vector3 forward, CreatureSpeciesDto species,
        ActorPerception perception, ActorKnowledge memory, float light, long now, out Target food, out Target water,
        List<ScatterInstance> candidates = null)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        food = water = default;
        Vector3 up = (position - _center).normalized;
        float range = SearchRange(perception);
        if (range <= 0) return;
        if ((species.Diet & ResourceKind.Plants) != 0 && _scatter != null &&
            _scatter.TryCaptureGatherContext(out var context))
        {
            if (candidates == null)
            {
                _candidates.Clear();
                _scatter.Gather(position, range, ScatterId.MaxLevel, _candidates, foodOnly: true);
                candidates = _candidates;
            }
            float best = float.PositiveInfinity;
            foreach (var candidate in candidates)
            {
                var prototype = context.Library.Prototypes[candidate.PrototypeIndex];
                float distance = (candidate.PositionWS - position).sqrMagnitude;
                if (distance >= best || _harvest?.Contains(candidate.Id) == true ||
                    Food.Remaining(candidate.Id, prototype, now) <= 0 ||
                    !Detect(candidate.Id, ActorObservationKind.Food, candidate.PositionWS, out var observation) ||
                    !CanApproach(position, candidate.PositionWS)) continue;
                best = distance;
                food = new Target { Contact = candidate.PositionWS, Foliage = prototype,
                    View = new CreatureResourceTarget { SourceKey = candidate.Id, Kind = ResourceKind.Plants,
                        Position = observation.Position, Uncertainty = observation.Uncertainty, Available = true } };
            }
        }
        if (_water != null && (species.Diet & (ResourceKind.FreshWater | ResourceKind.SaltWater)) != 0)
        {
            float best = float.PositiveInfinity;
            Vector3 tangent = CharacterMath.ArbitraryTangent(up);
            for (int ray = 0; ray < 16; ray++)
            {
                Vector3 direction = Quaternion.AngleAxis(ray * 22.5f, up) * tangent;
                Vector3 previous = position - up * species.BodyHeightMeters * .5f;
                for (float distance = 2f; distance <= range; distance += 2f)
                {
                    if (!Ground(position + direction * distance, out var point)) break;
                    if (_water.TryGetWaterSurface(point, out var sample) && sample.BodyDepth > .05f)
                    {
                        ResourceKind kind = sample.IsOcean ? ResourceKind.SaltWater : ResourceKind.FreshWater;
                        if ((species.Diet & kind) == 0) break;
                        // Refine the dry/wet edge. The goal stays on the bank, not at the lake centre.
                        Vector3 dry = previous, wet = point;
                        for (int i = 0; i < 5; i++)
                        {
                            if (!Ground((dry + wet) * .5f, out var middle)) break;
                            if (_water.TryGetWaterSurface(middle, out var edge) && edge.BodyDepth > .05f) wet = middle;
                            else dry = middle;
                        }
                        float score = (dry - position).sqrMagnitude;
                        if (score < best && CanApproach(position, dry) &&
                            Detect(sample.BodyId, ActorObservationKind.Water, wet, out _))
                        {
                            best = score;
                            water = new Target { Contact = wet, View = new CreatureResourceTarget {
                                SourceKey = sample.BodyId, Kind = kind, Position = dry, Available = true } };
                        }
                        break;
                    }
                    previous = point;
                }
            }
        }
        LastSearchMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d /
            System.Diagnostics.Stopwatch.Frequency;

        bool Detect(ulong id, ActorObservationKind kind, Vector3 point, out ActorKnowledge.Observation observation)
        {
            observation = default;
            if (id == 0) return false;
            var sight = new ActorStimulus(id, kind, ActorSense.Sight, point + up * .1f, 1, now, now + 1);
            bool detected = perception.WithinRange(position, forward, up, light, sight) &&
                perception.Observe(actor, position, forward, up, light, !ClearSight(position, sight.Position), sight, now, memory);
            if (!detected)
            {
                var scent = new ActorStimulus(id, kind, ActorSense.Smell, point, .5f, now, now + 1);
                detected = perception.Observe(actor, position, forward, up, light, false, scent, now, memory);
            }
            return detected && memory.TryGet(id, kind, now, out observation);
        }
    }

    bool Ground(Vector3 position, out Vector3 point)
    {
        Vector3 direction = (position - _center).normalized;
        point = default;
        if (!_ground.TryGetSurfaceRadius(direction, out float radius)) return false;
        point = _center + direction * radius; return true;
    }

    bool ClearSight(Vector3 from, Vector3 to)
    {
        for (int i = 1; i < 8; i++)
        {
            Vector3 point = Vector3.Lerp(from, to, i / 8f);
            if (!Ground(point, out var ground) || (point - _center).magnitude < (ground - _center).magnitude) return false;
        }
        return true;
    }

    // Conservative direct terrain corridor. A blocked corridor yields no goal; it never promises a detour.
    public bool CanApproach(Vector3 from, Vector3 to)
    {
        if (!Ground(from, out var previous)) return false;
        int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from, to)), 1, 48);
        for (int i = 1; i <= steps; i++)
        {
            if (!Ground(Vector3.Lerp(from, to, i / (float)steps), out var point)) return false;
            Vector3 up = (point - _center).normalized, delta = point - previous;
            if (Mathf.Abs(Vector3.Dot(delta, up)) > Vector3.ProjectOnPlane(delta, up).magnitude + .05f ||
                _water != null && _water.TryGetWaterSurface(point, out var water) && water.BodyDepth > MaximumWadingDepth) return false;
            previous = point;
        }
        return true;
    }

    public double Consume(in Target target, Vector3 position, CreatureSpeciesDto species, long now,
        float dt, ref ActorNeeds needs)
    {
        if (!target.View.Available || (species.Diet & target.View.Kind) == 0 ||
            !Ground(position, out var feet) || Vector3.Distance(feet, target.Contact) > CreatureConsumeState.Reach + .1f) return 0;
        double amount;
        if (target.Foliage != null)
        {
            if (_harvest?.Contains(target.View.SourceKey) == true) return 0;
            amount = Food.Consume(target.View.SourceKey, target.Foliage, now, ref needs, species.Diet,
                species.ConsumeUnitsPerSecond * dt);
            FoodConsumed += amount;
        }
        else
        {
            if (_water == null || !_water.TryGetWaterSurface(target.Contact, out var water) || water.BodyDepth <= .05f ||
                water.BodyId != target.View.SourceKey ||
                (water.IsOcean ? ResourceKind.SaltWater : ResourceKind.FreshWater) != target.View.Kind) return 0;
            // Lakes and oceans are effectively inexhaustible at animal drinking volumes.
            amount = Math.Min(needs.Thirst, species.ConsumeUnitsPerSecond * dt);
            needs = new ActorNeeds(needs.Hunger, needs.Thirst - amount);
            WaterConsumed += amount;
        }
        return amount;
    }
}
