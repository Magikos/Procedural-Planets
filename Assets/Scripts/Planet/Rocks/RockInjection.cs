using System;
using System.Collections.Generic;
using UnityEngine;

// Replaces the scatter library's ROCK prototypes with generated boulders, the same way TreeInjection replaces
// the tree prototypes. A library rock is one mesh, so every rock in a biome is the identical stone rotated;
// generating Variants meshes per prototype gives each biome that many genuinely different stones, and each is
// a pure function of its seed so a world regrows the same rocks every load.
//
// ponytail: apparent variety is capped at Variants because instanced draw is one mesh per batch. The cheap next
// lever is PER-INSTANCE NON-UNIFORM scale — a squashed boulder and a stretched one do not read as the same rock,
// where a uniformly scaled one always does. That needs a 3-axis scale on ScatterInstance and matching CPU/Burst
// packing, so it is deliberately not in this first pass.
[CommandPrefix("rock", Group = "Vegetation and wildlife", ReleasePolicy = ConsoleReleasePolicy.DevelopmentOnly)]
public static class RockInjection
{
    public static bool Enabled = true;

    // Generated shapes per source rock prototype. Each costs one scatter slot beyond the first.
    public static int Variants = 3;

    static readonly Dictionary<BiomeType, Material> _mats = new();

    public static ScatterLibraryDto Apply(ScatterLibraryDto lib)
    {
        if (!Enabled || lib?.Prototypes == null) return lib;
        try
        {
            int k = Mathf.Clamp(Variants, 1, 8);
            int nextSlot = MaxSlot(lib) + 1;

            var protos = new List<ScatterPrototypeDto>(lib.Prototypes.Length + lib.Prototypes.Length * (k - 1));
            var extra = new List<ScatterPrototypeDto>();
            int replaced = 0, outOfSlots = 0;

            foreach (ScatterPrototypeDto p in lib.Prototypes)
            {
                ScatterPrototypeDto gen = IsRock(p) ? TryReplace(p, 0, p.SlotId) : null;
                protos.Add(gen ?? p);
                if (gen == null) continue;
                replaced++;
                for (int v = 1; v < k; v++)
                {
                    if (nextSlot > ScatterId.MaxSlot) { outOfSlots++; continue; }
                    ScatterPrototypeDto variant = TryReplace(p, v, nextSlot);
                    if (variant == null) continue;
                    extra.Add(variant);
                    nextSlot++;
                }
            }
            protos.AddRange(extra);

            LoggerProvider.Log(LogLevel.Info, "RockInject",
                $"Replaced {replaced} rock prototype(s) with generated rocks + {extra.Count} variant(s) " +
                $"(x{k}, slots through {nextSlot - 1}/{ScatterId.MaxSlot}).");
            if (outOfSlots > 0)
                LoggerProvider.Log(LogLevel.Warning, "RockInject",
                    $"Out of scatter slots: {outOfSlots} rock variant(s) dropped. Lower rock.variants or free slots.");
            return new ScatterLibraryDto(protos.ToArray());
        }
        catch (Exception e)
        {
            LoggerProvider.LogException("RockInject", e);
            return lib; // never break scatter
        }
    }

    [ConsoleCommand("inject", "Replace scatter rocks with generated rocks (on/off), then run `planet.generate`.", MonoTargetType.Static)]
    static string InjectCmd(string state = "on")
    {
        Enabled = state == "on" || state == "true" || state == "1";
        return Reapply($"generated-rock injection {(Enabled ? "ON" : "OFF")}");
    }

    [ConsoleCommand("variants", "Generated rock shapes per prototype (1-8). Each costs one scatter slot.", MonoTargetType.Static)]
    static string VariantsCmd(int count)
    {
        Variants = Mathf.Clamp(count, 1, 8);
        return Reapply($"rock variants = {Variants} per prototype");
    }

    // The DTO is snapshotted at boot, so a runtime change only lands if the library is re-registered.
    // TreeInjection.Rebuild applies trees THEN rocks, so it is the correct rebuild for either toggle.
    static string Reapply(string status)
    {
        if (SettingsProvider.IsRegistered<ScatterLibraryDto>())
        {
            ScatterLibraryDto dto = TreeInjection.Rebuild();
            if (dto != null)
            {
                SettingsProvider.Update(dto);
                return $"{status} — scatter library updated. Run `planet.generate` to rebuild the world.";
            }
        }
        return $"{status} — run `planet.generate` to apply.";
    }

    // Name-matched, like the fern path: the library has no "rock" interaction to key off (rocks are
    // Interaction.None, same as bushes and grass), and matching on the name keeps that rest of the library
    // on its source meshes.
    static bool IsRock(ScatterPrototypeDto p) =>
        p != null && p.Interaction == ScatterInteraction.None && p.Parts != null && p.Parts.Length > 0
        && (p.DisplayName ?? "").IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0;

    static ScatterPrototypeDto TryReplace(ScatterPrototypeDto p, int variant, int slot)
    {
        try
        {
            RockDef def = DefFor(p.Biome, variant);
            int seed = (int)(StableHash(p.DisplayName ?? "rock", variant) % 900000) + 1;
            GeneratedRock rock = RockGenerator.Generate(def, seed);
            if (rock.Lod0 == null || rock.Lod0.vertexCount == 0) return null; // keep the source prop

            float cull = p.Parts[0].MaxCullDistance;
            if (cull < 20f) cull = 140f;
            // MeshCullDistance still chooses the existing screen-size-based billboard handover.
            float[] dist = { cull };

            var gen = p with
            {
                DisplayName = variant == 0 ? p.DisplayName : $"{p.DisplayName} v{variant}",
                SlotId = slot,
                Parts = new[] { new ScatterPartDto(MatFor(p.Biome, def.Color), rock.Lods, dist, true, true) },
                // The source atlas CANNOT be kept. Reusing it was wrong: the far card then shows the source
                // rock's colour while the near mesh is our generated stone, so a rock visibly changes shade as
                // you walk up to it and the mesh takes over - and at dusk the two lighting paths diverge enough
                // that the stale card reads as glowing. Bake our own; the disk cache means it costs bake time,
                // not load time.
                BakedImpostorAtlas = null,
                BakedImpostorNormal = null,
                SpeciesKey = "rock-" + p.Biome,
            };
            return UseBakedImpostors ? GeneratedImpostorManifest.WithCachedAtlas(gen) : gen;
        }
        catch (Exception e)
        {
            LoggerProvider.LogException("RockInject", e);
            return null;
        }
    }

    // Per-biome stone: colour, size and proportion. Variant shifts the proportions as well as the seed, so the
    // three stones in a biome are a squat boulder, a blockier one and an upright slab rather than three
    // rolls of the same dice.
    static RockDef DefFor(BiomeType biome, int variant)
    {
        (Color col, float size) = biome switch
        {
            BiomeType.Desert or BiomeType.Beach => (new Color(0.72f, 0.62f, 0.45f), 2.6f),
            BiomeType.Savanna or BiomeType.Scrub => (new Color(0.60f, 0.45f, 0.33f), 2.4f),
            BiomeType.Snow or BiomeType.IceBog => (new Color(0.72f, 0.74f, 0.78f), 2.8f),
            BiomeType.Mountain => (new Color(0.52f, 0.52f, 0.55f), 4.2f),
            BiomeType.Swamp or BiomeType.Taiga => (new Color(0.40f, 0.43f, 0.38f), 2.4f),
            BiomeType.Tundra or BiomeType.Steppe => (new Color(0.55f, 0.53f, 0.47f), 2.6f),
            BiomeType.Tropical => (new Color(0.45f, 0.46f, 0.40f), 2.6f),
            _ => (new Color(0.50f, 0.50f, 0.49f), 2.6f), // Forest / Grassland / Lake
        };

        (Vector3 axis, float rough, float buried) = (variant % 3) switch
        {
            0 => (new Vector3(1f, 0.62f, 0.92f), 0.46f, 0.22f),   // squat boulder
            1 => (new Vector3(0.9f, 0.85f, 0.86f), 0.55f, 0.18f), // blocky, more broken
            _ => (new Vector3(0.72f, 1.35f, 0.68f), 0.40f, 0.28f), // upright slab, set deeper
        };

        return new RockDef
        {
            Name = biome.ToString(),
            Size = size * (0.85f + 0.1f * (variant % 3)),
            AxisBias = axis,
            Roughness = rough,
            // 80 faces, not 320: on a 3 m stone the finer shell's facets are too small to read and it just
            // looks like a smooth egg. Fewer, bigger planes are the low-poly look, and cost a quarter as much.
            Subdivisions = 1,
            Buried = buried,
            Color = col,
        };
    }

    // Cleared by the impostor bake tool so it bakes from a library carrying no cards. See TreeInjection.
    public static bool UseBakedImpostors = true;


    static Material MatFor(BiomeType biome, Color c)
    {
        if (_mats.TryGetValue(biome, out Material hit) && hit != null) return hit;
        Shader sh = Shader.Find("Scatter/VertexColorLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh) { name = $"Gen {biome} rock" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        // Flat-tinted facets read as painted cardboard; a mottled greyscale multiplied by the biome tint gives
        // the surface something to catch the light. Same reason every trunk now carries a bark texture.
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", GeneratedSurfaceTexture.Stone());
        m.enableInstancing = true;
        _mats[biome] = m;
        return m;
    }

    static int MaxSlot(ScatterLibraryDto lib)
    {
        int max = -1;
        foreach (ScatterPrototypeDto p in lib.Prototypes)
            if (p != null && p.SlotId > max) max = p.SlotId;
        return max;
    }

    // FNV-1a: string.GetHashCode is randomised per process, which would regrow every rock differently each run.
    static uint StableHash(string s, int salt)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in s) { h ^= c; h *= 16777619u; }
            h ^= (uint)salt; h *= 16777619u;
            return h;
        }
    }
}
