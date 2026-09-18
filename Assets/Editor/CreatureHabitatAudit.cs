using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only sampling of the running world's actual placement path and stable slot allocation.</summary>
public static class CreatureHabitatAudit
{
    [Serializable] public sealed class BiomeCount
    {
        public string biome;
        public int suitableSlots, occupiedSlots, occupiedNearFreshWater;
        public Vector3 representativeHome;
    }
    [Serializable] public sealed class SpeciesCount
    {
        public string species;
        public int speciesIndex, testedSlots, occupiedSlots, territoriesWithAnimals;
        public int[] stableSlots;
        public BiomeCount[] biomes;
        public Vector3 representativeHome;
        public int suitableTerritories;
        public double suitableTerritoryAreaSquareKm, occupiedPerSuitableSquareKm;
    }
    [Serializable] public sealed class Report
    {
        public string meaning = "Potential seeded homes, not current living population. Saved deaths and displacements are not modified or included.";
        public int seed, territoryLevel, territoryCount, lakeCount, oceanCount, testedSlots;
        public float radiusMeters;
        public double elapsedSeconds;
        public SpeciesCount[] species;
    }

    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static CancellationTokenSource _cancel;
    static CancellationTokenSource _forageCancel;
    public static bool Running { get; private set; }
    public static int TestedSlots { get; private set; }
    public static string ResultJson { get; private set; }
    public static string Error { get; private set; }
    public static float FrameBudgetMs { get; private set; } = 6f;
    public static bool ForageRunning { get; private set; }
    public static int ForageCompleted { get; private set; }
    public static string ForageResultJson { get; private set; }
    public static string ForageError { get; private set; }

    [Serializable] public sealed class ForageSite
    {
        public string species, biome;
        public Vector3 home;
        public int edibleCandidates, availableSources, reachableSources;
        public float nearestAvailableMeters = -1f;
        public double availableFoodUnits;
    }
    [Serializable] public sealed class ForageReport
    {
        public int seed;
        public float radiusMeters = 24f;
        public string meaning = "One occupied home per herbivore biome. Counts use actual edible scatter, current harvest and food stock; they do not guarantee long-term carrying capacity.";
        public ForageSite[] sites;
    }

    public static string Start(CreatureResidencyService service = null, float frameBudgetMs = 6f)
    {
        if (Running) return "Habitat audit is already running.";
        SetFrameBudget(frameBudgetMs);
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run the habitat audit during Planet play mode.");
        service = ResolveService(service);
        if (service == null || !Read<bool>(service, "_configured")) throw new InvalidOperationException("Creature residency is not configured.");
        Running = true; TestedSlots = 0; Error = ResultJson = null;
        _cancel = new CancellationTokenSource();
        _ = RunAsync(service, _cancel.Token);
        return "Habitat audit started. Read Running, TestedSlots, Error, and ResultJson for progress and results.";
    }

    public static void Cancel() { _cancel?.Cancel(); _forageCancel?.Cancel(); }

    static CreatureResidencyService ResolveService(CreatureResidencyService service)
    {
        if (service != null) return service;
        var planet = UnityEngine.Object.FindFirstObjectByType<Planet>();
        if (planet == null) throw new InvalidOperationException("No running Planet was found.");
        return (CreatureResidencyService)typeof(Planet).GetField("_creatures", Private).GetValue(planet);
    }

    public static string StartForage(string habitatJson = null, CreatureResidencyService service = null)
    {
        if (ForageRunning) return "Forage audit is already running.";
        if (Running || !EditorApplication.isPlaying) throw new InvalidOperationException("Finish the habitat audit before running forage checks in play mode.");
        service = ResolveService(service);
        var report = JsonUtility.FromJson<Report>(habitatJson ?? ResultJson ?? throw new InvalidOperationException("No completed habitat report is available."));
        if (service?.Resources == null || report?.species == null || report.seed != Read<int>(service, "_seed"))
            throw new InvalidOperationException("A configured world and a matching habitat report are required.");
        ForageRunning = true; ForageCompleted = 0; ForageError = ForageResultJson = null;
        _forageCancel = new CancellationTokenSource();
        _ = RunForageAsync(service, report, _forageCancel.Token);
        return "Forage audit started. Read ForageRunning, ForageCompleted, ForageError, and ForageResultJson.";
    }

    static async Awaitable RunForageAsync(CreatureResidencyService service, Report habitat, CancellationToken cancellation)
    {
        try
        {
            var resources = service.Resources;
            var scatter = (ScatterField)typeof(PlanetCreatureResources).GetField("_scatter", Private).GetValue(resources);
            var harvest = (ScatterHarvestStore)typeof(PlanetCreatureResources).GetField("_harvest", Private).GetValue(resources);
            if (scatter == null || !scatter.TryCaptureGatherContext(out var context)) throw new InvalidOperationException("Edible scatter is not configured.");
            var library = service.Library;
            int epoch = Read<int>(service, "_resourceEpoch");
            var clock = Read<Func<long>>(service, "_unixClock");
            var sites = new List<ForageSite>();
            foreach (var speciesCount in habitat.species)
            {
                var species = library.At(speciesCount.speciesIndex);
                if (species == null || (species.Diet & ResourceKind.Plants) == 0) continue;
                if (species.DisplayName != speciesCount.species) throw new InvalidOperationException("Species changed since the habitat audit.");
                foreach (var biome in speciesCount.biomes ?? Array.Empty<BiomeCount>())
                {
                    if (biome.occupiedSlots == 0) continue;
                    cancellation.ThrowIfCancellationRequested();
                    var site = new ForageSite { species = species.DisplayName, biome = biome.biome, home = biome.representativeHome };
                    var candidates = await scatter.GatherFoodAsync(site.home, 24f);
                    if (!EditorApplication.isPlaying || Read<int>(service, "_resourceEpoch") != epoch || !ReferenceEquals(service.Library, library))
                        throw new OperationCanceledException("World changed during forage sampling.");
                    long now = clock();
                    foreach (var candidate in candidates)
                    {
                        var prototype = context.Library.Prototypes[candidate.PrototypeIndex];
                        float distance = Vector3.Distance(candidate.PositionWS, site.home);
                        if (prototype.FoodUnits <= 0f || distance > 24f) continue;
                        site.edibleCandidates++;
                        if (harvest?.Contains(candidate.Id) == true) continue;
                        double remaining = resources.Food.Remaining(candidate.Id, prototype, now);
                        if (remaining <= 0d) continue;
                        site.availableSources++; site.availableFoodUnits += remaining;
                        if (site.nearestAvailableMeters < 0 || distance < site.nearestAvailableMeters) site.nearestAvailableMeters = distance;
                        if (resources.CanApproach(site.home, candidate.PositionWS)) site.reachableSources++;
                    }
                    sites.Add(site); ForageCompleted++;
                    await Awaitable.NextFrameAsync(cancellation);
                }
            }
            ForageResultJson = JsonUtility.ToJson(new ForageReport { seed = habitat.seed, sites = sites.ToArray() }, true);
        }
        catch (Exception exception) { ForageError = (exception.InnerException ?? exception).ToString(); }
        finally { ForageRunning = false; _forageCancel?.Dispose(); _forageCancel = null; }
    }

    public static void SetFrameBudget(float frameBudgetMs)
    {
        if (!float.IsFinite(frameBudgetMs) || frameBudgetMs < 1f || frameBudgetMs > 50f)
            throw new ArgumentOutOfRangeException(nameof(frameBudgetMs), "Audit frame budget must be between 1 and 50 milliseconds.");
        FrameBudgetMs = frameBudgetMs;
    }

    static T Read<T>(CreatureResidencyService service, string name) =>
        (T)typeof(CreatureResidencyService).GetField(name, Private).GetValue(service);

    static async Awaitable RunAsync(CreatureResidencyService service, CancellationToken cancellation)
    {
        var total = Stopwatch.StartNew();
        try
        {
            var library = service.Library;
            int epoch = Read<int>(service, "_resourceEpoch");
            var transform = Read<Transform>(service, "_planetTransform");
            var planet = PlanetTransformSnapshot.Capture(transform);
            float radius = Read<float>(service, "_planetRadius");
            var biomeProvider = Read<IBiomeProvider>(service, "_biome");
            var slotLists = Read<int[][]>(service, "_speciesSlots").Select(s => (int[])s.Clone()).ToArray();
            var homeMethod = typeof(CreatureResidencyService).GetMethod("TryFindHome", Private);
            int cells = CreatureTerritory.CellsPerFace;
            var report = new Report { seed = Read<int>(service, "_seed"), territoryLevel = CreatureTerritory.Level,
                territoryCount = 6 * cells * cells, radiusMeters = radius * planet.UniformScale,
                lakeCount = WaterBodyMap.Current?.Bodies?.CountOf(WaterBodyKind.Lake) ?? 0,
                oceanCount = WaterBodyMap.Current?.Bodies?.CountOf(WaterBodyKind.Ocean) ?? 0,
                species = new SpeciesCount[library.Count] };
            var chunk = Stopwatch.StartNew();
            for (int index = 0; index < library.Count; index++)
            {
                var species = library.At(index);
                var counts = new SpeciesCount { speciesIndex = index, species = species?.DisplayName ?? "Missing",
                    stableSlots = slotLists[index] };
                report.species[index] = counts;
                var biomes = new Dictionary<BiomeType, BiomeCount>();
                if (species == null) { counts.biomes = Array.Empty<BiomeCount>(); continue; }
                for (int face = 0; face < 6; face++)
                for (int y = 0; y < cells; y++)
                for (int x = 0; x < cells; x++)
                {
                    bool occupiedTerritory = false, suitableTerritory = false;
                    foreach (int slot in slotLists[index])
                    {
                        var args = new object[] { CreatureKey.Slot(face, CreatureTerritory.Level, x, y, slot), species, planet, Vector3.zero };
                        bool found = (bool)homeMethod.Invoke(service, args);
                        Vector3 home = (Vector3)args[3];
                        counts.testedSlots++; TestedSlots++;
                        // TryFindHome retains a suitable candidate when density rejects its occupancy roll.
                        if (home != Vector3.zero)
                        {
                            suitableTerritory = true;
                            Vector3 radial = home - planet.Center;
                            Vector3 localDirection = planet.InverseTransformDirection(radial.normalized);
                            float elevation = radial.magnitude / planet.UniformScale / radius - 1f;
                            var biome = biomeProvider?.EvaluateBiome(localDirection, elevation).PrimaryBiome ?? BiomeType.Grassland;
                            if (!biomes.TryGetValue(biome, out var entry)) biomes[biome] = entry = new BiomeCount { biome = biome.ToString() };
                            entry.suitableSlots++;
                            if (found)
                            {
                                if (counts.occupiedSlots == 0) counts.representativeHome = home;
                                if (entry.occupiedSlots == 0) entry.representativeHome = home;
                                counts.occupiedSlots++; entry.occupiedSlots++; occupiedTerritory = true;
                                if (CreatureHabitatSampling.NearFreshWater(WaterBodyMap.Current, localDirection)) entry.occupiedNearFreshWater++;
                            }
                        }
                        if (chunk.Elapsed.TotalMilliseconds < FrameBudgetMs) continue;
                        await Awaitable.NextFrameAsync(cancellation);
                        if (!EditorApplication.isPlaying || transform == null || !Read<bool>(service, "_configured")
                            || Read<int>(service, "_resourceEpoch") != epoch || !ReferenceEquals(service.Library, library))
                            throw new OperationCanceledException("World or creature settings changed during habitat sampling.");
                        chunk.Restart();
                    }
                    if (occupiedTerritory) counts.territoriesWithAnimals++;
                    if (suitableTerritory)
                    {
                        counts.suitableTerritories++;
                        counts.suitableTerritoryAreaSquareKm += CreatureHabitatSampling.AreaSquareKm(
                            CreatureKey.Slot(face, CreatureTerritory.Level, x, y, slotLists[index][0]), report.radiusMeters);
                    }
                }
                counts.occupiedPerSuitableSquareKm = counts.suitableTerritoryAreaSquareKm > 0d
                    ? counts.occupiedSlots / counts.suitableTerritoryAreaSquareKm : 0d;
                counts.biomes = biomes.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
            }
            report.testedSlots = TestedSlots; report.elapsedSeconds = total.Elapsed.TotalSeconds;
            ResultJson = JsonUtility.ToJson(report, true);
        }
        catch (Exception exception) { Error = (exception.InnerException ?? exception).ToString(); }
        finally { Running = false; _cancel?.Dispose(); _cancel = null; }
    }
}
