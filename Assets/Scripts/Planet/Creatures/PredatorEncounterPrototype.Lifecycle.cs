using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public sealed partial class PredatorEncounterPrototype
{
    [Header("Habitat and life cycle (restart to apply profiles)")]
    public CreatureLifeSettings DeerLife, WolfLife;
    public bool LifeCycleEnabled, RenewableSources, HabitatMode;
    [Min(0)] public float PlantUnitsPerDay = 6, WaterUnitsPerDay = 20;
    [Min(2)] public int HabitatPopulationLimit = 64;
    [Min(1)] public float MateRange = 8;
    public int Births { get; private set; }
    public double FoodConsumed { get; private set; }
    public double WaterConsumed { get; private set; }
    readonly Dictionary<ActorDeathCause, int> _deathCounts = new();
    readonly List<(Actor mother, int count)> _birthQueue = new();
    uint _nextBirthId = 10000;
    float _nextMateScan;

    [ContextMenu("Start Renewable Habitat")]
    public void StartHabitat()
    {
        Scenario = PerceptionScenario.Ecosystem; Ecosystem = true;
        HabitatMode = true;
        LifeCycleEnabled = RenewableSources = true;
        // The encounter's 80-second hunger cycle is a combat stress test, not a population baseline.
        HungerSeconds = 320; ThirstSeconds = 240;
        DeerCount = 12; PackWolfCount = 3; IncludeSoloWolf = true;
        FoodAvailable = WaterAvailable = true;
        ResetScenario();
    }

    void CreateHabitatSources()
    {
        if (!HabitatMode) return;
        uint id = 300;
        // Spread resources over the existing navigable course, including retreat destinations.
        for (int x = -30; x <= 30; x += 20)
            for (int z = -30; z <= 30; z += 20)
            {
                AddSource(id++, ResourceKind.Plants, new Vector3(x, 0, z), 4);
                AddSource(id++, ResourceKind.FreshWater, new Vector3(x + 4, 0, z + 4), 10);
            }
        foreach (var actor in _actors) actor.Needs = new ActorNeeds(.2, .2);
        WolfHunger = DeerHunger = WolfThirst = DeerThirst = .2f;
    }

    void InitializeLife(Actor actor, double? age = null, ulong mother = 0, ulong father = 0)
    {
        actor.LifeProfile = actor.Predator ? _wolfLifeSnapshot : _deerLifeSnapshot;
        double variation = (actor.Id * 2654435761ul % 10007) / 10007d;
        actor.Life = new ActorLifeCycle(CreatureAnimationView.IsMale(actor.Id) ? ActorSex.Male : ActorSex.Female,
            age ?? actor.LifeProfile.AdultDays + variation * (actor.LifeProfile.ElderDays - actor.LifeProfile.AdultDays) * .5,
            actor.LifeProfile.MinimumDeathDays + variation * (actor.LifeProfile.MaximumDeathDays - actor.LifeProfile.MinimumDeathDays), mother, father);
    }

    void AdvanceHabitat(float dt, double gameStep)
    {
        foreach (var source in _sources)
            if (source.Owner == null && RenewableSources && (source.Stock.ThirstPerUnit > 0 ? WaterAvailable : FoodAvailable))
                source.Stock.Advance(gameStep);
        if (!LifeCycleEnabled) return;
        _birthQueue.Clear();
        foreach (var actor in _actors)
        {
            if (!Available(actor)) continue;
            int births = actor.Life.Advance(gameStep / 86400d, actor.LifeProfile);
            if (actor.Life.DiedOfAge)
            { actor.DeathCause = ActorDeathCause.OldAge; Damage(actor, actor.Health, false); continue; }
            if (births > 0) _birthQueue.Add((actor, births));
            float scale = actor.Life.Stage(actor.LifeProfile) == ActorLifeStage.Juvenile ? (float)actor.Life.PhysicalScale(actor.LifeProfile) : 1;
            actor.Height = (actor.Predator ? 1 : 1.84f) * scale;
            actor.View.Root.localScale = Vector3.one * scale;
        }
        foreach (var birth in _birthQueue)
            for (int i = 0; i < birth.count; i++)
            {
                var mother = birth.mother;
                var child = AddActor(_nextBirthId++, mother.Predator, Feet(mother) + Vector3.right * (.8f + i), .2, .2);
                InitializeLife(child, 0, mother.Id, mother.Life.MateId);
                child.Group = mother.Group; child.Home = mother.Home;
                Births++;
            }
        if (_elapsed < _nextMateScan) return;
        _nextMateScan = _elapsed + 1;
        int reservations = LivingCount;
        foreach (var actor in _actors) if (Available(actor) && actor.Life.Pregnant) reservations += actor.LifeProfile.LitterSize;
        foreach (var mother in _actors)
        {
            if (!Available(mother) || mother.Life.Sex != ActorSex.Female) continue;
            bool capacity = reservations + mother.LifeProfile.LitterSize <= HabitatPopulationLimit && SupportsBirth(mother);
            bool safe = !mother.Senses.HasThreat && mother.Brain.Objective != CreatureObjective.Hunt;
            if (!mother.Life.CanReproduce(mother.LifeProfile, mother.Needs, mother.Vitals.Fraction, mother.Endurance.Fatigue, safe, capacity)) continue;
            foreach (var father in _actors)
            {
                if (father == mother || !Available(father) || father.Predator != mother.Predator || father.Life.Sex != ActorSex.Male ||
                    father.Group != mother.Group || mother.Life.MotherId == father.Id || father.Life.MotherId == mother.Id ||
                    mother.Life.FatherId == father.Id || father.Life.FatherId == mother.Id ||
                    mother.Life.MotherId != 0 && mother.Life.MotherId == father.Life.MotherId ||
                    mother.Life.FatherId != 0 && mother.Life.FatherId == father.Life.FatherId ||
                    FlatDistance(Feet(mother), Feet(father)) > MateRange || !Visible(mother, father) ||
                    !father.Life.CanReproduce(father.LifeProfile, father.Needs, father.Vitals.Fraction, father.Endurance.Fatigue,
                        !father.Senses.HasThreat && father.Brain.Objective != CreatureObjective.Hunt, true)) continue;
                if (mother.Life.TryConceive(father.Id, mother.LifeProfile, mother.Needs, mother.Vitals.Fraction,
                    mother.Endurance.Fatigue, safe, capacity)) reservations += mother.LifeProfile.LitterSize;
                break;
            }
        }
    }

    bool SupportsBirth(Actor mother)
    {
        if (!FoodAvailable || !WaterAvailable || !RenewableSources) return false;
        int population = 0;
        double food = 0, water = 0;
        foreach (var source in _sources)
        {
            if (source.Owner != null || !RenewableSources || FlatDistance(Feet(mother), source.Position) > 35 ||
                _navigationCourse != null && !mother.Navigation.TryApproach(Feet(mother), HomeAnchor(source.Position), HomeAnchor(source.Position), out _)) continue;
            if (FoodAvailable && source.Stock.Kind == ResourceKind.Plants) food += source.Stock.RenewalPerSecond * 86400 * source.Stock.HungerPerUnit;
            if (WaterAvailable && source.Stock.Kind == ResourceKind.FreshWater) water += source.Stock.RenewalPerSecond * 86400 * source.Stock.ThirstPerUnit;
        }
        if (mother.Predator)
        {
            food = 0;
            // Budget only half of potential prey recruitment for predators. Existing meat is not renewable production.
            foreach (var prey in _actors)
                if (Available(prey) && !prey.Predator && prey.Life.Sex == ActorSex.Female && prey.Life.Stage(prey.LifeProfile) == ActorLifeStage.Adult)
                    food += prey.Combat.MeatYield * prey.LifeProfile.LitterSize / (prey.LifeProfile.GestationDays + prey.LifeProfile.BirthCooldownDays) * .5;
        }
        foreach (var actor in _actors)
            if (Available(actor) && actor.Predator == mother.Predator)
                population += 1 + (actor.Life.Pregnant ? actor.LifeProfile.LitterSize : 0);
        return population + mother.LifeProfile.LitterSize <= new ActorHabitatCapacity(food, water)
            .SupportedPopulation(Mathf.Max(1, DayLengthSeconds) / Mathf.Max(1, HungerSeconds),
                Mathf.Max(1, DayLengthSeconds) / Mathf.Max(1, ThirstSeconds));
    }

    void ObserveParent(Actor child)
    {
        if (!LifeCycleEnabled || child.Life.Stage(child.LifeProfile) != ActorLifeStage.Juvenile) return;
        foreach (var adult in _actors)
        {
            if (!Available(adult) || adult.Id != child.Life.MotherId || !Observation(child, adult, out var known)) continue;
            child.Senses.HasHomeSite = true; child.Senses.HomeSafe = !child.Senses.HasThreat;
            child.Senses.RestPosition = known.Position; child.Senses.HomeId = new EntityId(EntityId.HostOwner, adult.Id);
            child.Senses.AtHome = FlatDistance(Feet(child), known.Position) < 3;
            child.Senses.SeparatedFromGroup = !child.Senses.AtHome;
            break;
        }
    }

    void HabitatStatus(StringBuilder text)
    {
        if (!LifeCycleEnabled && !RenewableSources) return;
        text.Append($"Habitat | Births {Births} | Renewal {(RenewableSources ? "on" : "off")} | Deaths:");
        foreach (var entry in _deathCounts) text.Append($" {entry.Key} {entry.Value}");
        text.AppendLine();
        int hungry = 0, thirsty = 0, foodUnknown = 0, waterUnknown = 0;
        foreach (var actor in _actors)
        {
            if (!Available(actor)) continue;
            if (actor.Needs.Hunger >= .9) { hungry++; if (actor.Food == null) foodUnknown++; }
            if (actor.Needs.Thirst >= .9) { thirsty++; if (actor.Water == null) waterUnknown++; }
        }
        text.AppendLine($"Consumed: food {FoodConsumed:F1}, water {WaterConsumed:F1} | Critical hunger {hungry} ({foodUnknown} no known food) | Critical thirst {thirsty} ({waterUnknown} no known water)");
    }
}
