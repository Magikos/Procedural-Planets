#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Collections;
using UnityEngine;

// Runtime self-test for BiomeLookupData / BiomeLookupEvaluator (Phase B step 1). Runs at
// BeforeSceneLoad; failures surface as Debug.LogError.
//
// Coverage:
    //   - Snapshot reproduces BiomeRegistry.Resolve()'s primary biome id, secondary biome id,
    //     and blend weight for random (temperature, moisture, elevation) samples covering
    //     ocean / beach / grid / mountain / snowy mountain regimes.
//   - GetSliceIdForBiomeType round-trips with GetDefinitionByIndex.
//   - Dispose pattern releases the native grid array.
//
// The test loads any BiomeRegistry assets it finds in Resources or via AssetDatabase. If none
// exist (e.g. test runner with no project assets), it builds a small synthetic registry in
// code so coverage runs anyway.
public static class BiomeLookupSelfTest
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RunAtStartup()
    {
        if (!TryRun(out string error))
            Debug.LogError($"[BiomeLookup] Self-test FAILED: {error}");
    }

    public static bool TryRun(out string error)
    {
        error = null;

        BiomeRegistry registry = LoadOrSynthesizeRegistry();
        if (registry == null)
        {
            error = "Unable to load or synthesize a BiomeRegistry for the self-test.";
            return false;
        }

        BiomeLookupData lookup = registry.BuildLookupData(Allocator.TempJob);
        try
        {
            // GetSliceIdForBiomeType contract: it returns SOME slot whose definition has the
            // requested type. Types may legitimately appear in multiple slots (e.g. Snow can
            // exist in the temp/moisture grid AND as the SnowyMountain elevation override) —
            // we don't require a unique mapping, only that the returned slot is consistent.
            int biomeCount = registry.BiomeCount;
            for (int i = 0; i < biomeCount; i++)
            {
                BiomeDefinition def = registry.GetDefinitionByIndex(i);
                if (def == null) continue;
                byte sliceId = registry.GetSliceIdForBiomeType(def.Type);
                BiomeDefinition resolvedDef = registry.GetDefinitionByIndex(sliceId);
                if (resolvedDef == null || resolvedDef.Type != def.Type)
                {
                    error = $"GetSliceIdForBiomeType({def.Type}) returned slot {sliceId}, which resolves to {(resolvedDef == null ? "null" : resolvedDef.Type.ToString())} — type mismatch";
                    return false;
                }
            }

            // Random fuzz: 200 samples covering each elevation regime. Validates that the
            // evaluator's chosen slice id points to a biome definition matching the BiomeType
            // that the legacy Resolve() returned. We compare by TYPE, not slot, because slots
            // are not unique-per-type (see comment above).
            var rng = new Unity.Mathematics.Random(0x42C0FFEEu);
            for (int s = 0; s < 200; s++)
            {
                float t = rng.NextFloat();
                float m = rng.NextFloat();
                float e = SampleElevationAcrossRegimes(registry, rng.NextFloat(), s);

                BiomeResult expected = registry.Resolve(t, m, e);
                BiomeLookupEvaluator.Resolve(lookup, t, m, e,
                    out byte primaryId, out byte secondaryId, out float blendWeight);

                BiomeDefinition primaryDef = registry.GetDefinitionByIndex(primaryId);
                if (primaryDef == null)
                {
                    error = $"Sample {s} (t={t:F3} m={m:F3} e={e:F4}): snapshot returned slot {primaryId}, which has no BiomeDefinition";
                    return false;
                }
                if (primaryDef.Type != expected.PrimaryBiome)
                {
                    error = $"Sample {s} (t={t:F3} m={m:F3} e={e:F4}): snapshot slot {primaryId} has type {primaryDef.Type}, but Resolve() returned {expected.PrimaryBiome}";
                    return false;
                }

                BiomeDefinition secondaryDef = registry.GetDefinitionByIndex(secondaryId);
                if (secondaryDef == null)
                {
                    error = $"Sample {s} (t={t:F3} m={m:F3} e={e:F4}): snapshot returned secondary slot {secondaryId}, which has no BiomeDefinition";
                    return false;
                }
                if (secondaryDef.Type != expected.SecondaryBiome)
                {
                    error = $"Sample {s} (t={t:F3} m={m:F3} e={e:F4}): snapshot secondary slot {secondaryId} has type {secondaryDef.Type}, but Resolve() returned {expected.SecondaryBiome}";
                    return false;
                }
                if (Mathf.Abs(blendWeight - expected.BlendWeight) > 0.0001f)
                {
                    error = $"Sample {s} (t={t:F3} m={m:F3} e={e:F4}): snapshot blend {blendWeight:F5}, Resolve() blend {expected.BlendWeight:F5}";
                    return false;
                }
            }
        }
        finally
        {
            lookup.Dispose();
        }

        return true;
    }

    // Spread synthetic elevations across the registry's regime boundaries so the test hits
    // ocean / beach / grid / mountain / snowy mountain cases instead of just one regime.
    static float SampleElevationAcrossRegimes(BiomeRegistry registry, float r, int seed)
    {
        switch (seed % 5)
        {
            case 0: return registry.OceanThreshold - 0.05f - r * 0.10f;            // deep ocean
            case 1: return registry.OceanThreshold + r * registry.BeachWidth;       // beach
            case 2: return registry.MountainThreshold + 0.02f + r * 0.05f;          // mountain region
            default:
                // Grid region: anywhere strictly between beach and mountain bands.
                float lo = registry.OceanThreshold + registry.BeachWidth + 0.001f;
                float hi = registry.MountainThreshold - 0.001f;
                if (hi <= lo) return (lo + hi) * 0.5f;
                return Mathf.Lerp(lo, hi, r);
        }
    }

    static BiomeRegistry LoadOrSynthesizeRegistry()
    {
        BiomeRegistry registry = Resources.Load<BiomeRegistry>("BiomeRegistry");
        if (registry != null) return registry;

#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:BiomeRegistry");
        if (guids != null && guids.Length > 0)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            BiomeRegistry asset = UnityEditor.AssetDatabase.LoadAssetAtPath<BiomeRegistry>(path);
            if (asset != null) return asset;
        }
#endif

        // Synthesize a minimal registry so the test always has coverage even in pure
        // playmode tests with no project assets indexed.
        registry = ScriptableObject.CreateInstance<BiomeRegistry>();
        registry.TemperatureSteps = 3;
        registry.MoistureSteps = 3;
        registry.OceanThreshold = 0f;
        registry.BeachWidth = 0.003f;
        registry.MountainThreshold = 0.08f;
        registry.BlendWidth = 0.005f;
        registry.ElevationBlendWidth = 0.001f;

        BiomeDefinition Make(BiomeType type)
        {
            var def = ScriptableObject.CreateInstance<BiomeDefinition>();
            def.Type = type;
            return def;
        }

        registry.OceanBiome = Make(BiomeType.Ocean);
        registry.BeachBiome = Make(BiomeType.Beach);
        registry.MountainBiome = Make(BiomeType.Mountain);
        registry.SnowyMountainBiome = Make(BiomeType.Snow);

        registry.GridEntries = new BiomeDefinition[9];
        BiomeType[] gridTypes = {
            BiomeType.Tundra, BiomeType.Steppe, BiomeType.Taiga,
            BiomeType.Grassland, BiomeType.Forest, BiomeType.Swamp,
            BiomeType.Desert, BiomeType.Savanna, BiomeType.Tropical,
        };
        for (int i = 0; i < 9; i++) registry.GridEntries[i] = Make(gridTypes[i]);

        return registry;
    }
}
#endif
