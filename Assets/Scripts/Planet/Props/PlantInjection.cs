using System;
using System.Collections.Generic;
using UnityEngine;

// Replaces the scatter library's non-tree PLANT prototypes with generated geometry, the way TreeInjection
// replaces the trees. Separate class rather than more branches in TreeInjection: that one already carries
// trees and ferns, and this covers eight more kinds with their own defs and sizing rules.
//
// Every kind is built from primitives that already existed — petals are leaf cards, a mushroom cap is the
// conifer cone with two tiers, coral is the branching skeleton with no leaf tier, kelp is fern blades on a long
// stipe, and a lily pad is the rock generator flattened. No new mesher code was needed for any of them.
//
// ponytail: the FERN path still lives in TreeInjection. It belongs here, but it works and moving it is a pure
// refactor — do it when ferns next need a change, not as a drive-by.
[CommandPrefix("plant")]
public static class PlantInjection
{
    public static bool Enabled = true;

    // Cleared by the impostor bake tool so it bakes from a library carrying no cards. See TreeInjection.
    public static bool UseBakedImpostors = true;

    public enum Kind { None, Bush, Grass, Flower, Mushroom, Reed, Cattail, Lily, Coral, Kelp }

    // Variants per kind, scaled by how big the thing reads on screen. A chest-high bush earns a second shape;
    // a 20 cm flower drawn 4000 times per hectare does not, and each extra costs a scatter slot in every biome
    // that has one.
    static int VariantsFor(Kind k) => k switch
    {
        Kind.Bush => 2,
        Kind.Reed => 2,
        Kind.Coral => 2,
        Kind.Kelp => 2,
        _ => 1,
    };

    public static ScatterLibraryDto Apply(ScatterLibraryDto lib)
    {
        if (!Enabled || lib?.Prototypes == null) return lib;
        try
        {
            int nextSlot = MaxSlot(lib) + 1;
            var protos = new List<ScatterPrototypeDto>(lib.Prototypes.Length);
            var extra = new List<ScatterPrototypeDto>();
            var counts = new Dictionary<Kind, int>();
            int outOfSlots = 0;

            foreach (ScatterPrototypeDto p in lib.Prototypes)
            {
                Kind kind = Classify(p);
                if (kind == Kind.None) { protos.Add(p); continue; }

                int k = Mathf.Clamp(VariantsFor(kind), 1, 8);
                // K interleaved variants at sqrt(K) spacing keeps the original density, as TreeInjection does.
                float spacingScale = Mathf.Sqrt(k);

                ScatterPrototypeDto gen = TryReplace(p, kind, 0, p.SlotId, spacingScale);
                protos.Add(gen ?? p);
                if (gen == null) continue;
                counts.TryGetValue(kind, out int c);
                counts[kind] = c + 1;

                for (int v = 1; v < k; v++)
                {
                    if (nextSlot > ScatterId.MaxSlot) { outOfSlots++; continue; }
                    ScatterPrototypeDto variant = TryReplace(p, kind, v, nextSlot, spacingScale);
                    if (variant == null) continue;
                    extra.Add(variant);
                    nextSlot++;
                }
            }
            protos.AddRange(extra);

            var summary = new List<string>();
            foreach (KeyValuePair<Kind, int> kv in counts) summary.Add($"{kv.Value} {kv.Key.ToString().ToLowerInvariant()}");
            LoggerProvider.Log(LogLevel.Info, "PlantInject",
                $"Replaced {string.Join(", ", summary)} prototype(s) with generated plants + {extra.Count} " +
                $"variant(s) (slots through {nextSlot - 1}/{ScatterId.MaxSlot}).");
            if (outOfSlots > 0)
                LoggerProvider.Log(LogLevel.Warning, "PlantInject",
                    $"Out of scatter slots: {outOfSlots} plant variant(s) dropped.");
            return new ScatterLibraryDto(protos.ToArray());
        }
        catch (Exception e)
        {
            LoggerProvider.LogException("PlantInject", e);
            return lib; // never break scatter
        }
    }

    // Order matters: "FlowerBush" is a bush, not a flower, and "Grassland Rock" must not become a grass clump.
    // Bush and Grass match on the name's END for that reason; the rest are distinctive enough as substrings.
    public static Kind Classify(ScatterPrototypeDto p)
    {
        if (p == null || p.Interaction != ScatterInteraction.None || p.Parts == null || p.Parts.Length == 0)
            return Kind.None;
        string n = p.DisplayName ?? "";
        if (n.EndsWith("Bush", StringComparison.OrdinalIgnoreCase)) return Kind.Bush;
        if (n.EndsWith("Grass", StringComparison.OrdinalIgnoreCase)) return Kind.Grass;
        if (Has(n, "Cattail")) return Kind.Cattail;
        if (Has(n, "Reed")) return Kind.Reed;
        if (Has(n, "Lily")) return Kind.Lily;
        if (Has(n, "Coral")) return Kind.Coral;
        if (Has(n, "Kelp") || Has(n, "Seaweed")) return Kind.Kelp;
        if (Has(n, "Mushroom")) return Kind.Mushroom;
        if (Has(n, "Flower")) return Kind.Flower; // includes "Wildflowers"
        return Kind.None;
    }

    static bool Has(string s, string term) => s.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

    static ScatterPrototypeDto TryReplace(ScatterPrototypeDto p, Kind kind, int variant, int slot, float spacingScale)
    {
        try
        {
            string name = p.DisplayName ?? kind.ToString();
            string key = ShareKey(p, kind);
            int seed = (int)(StableHash(name, variant) % 900000) + 1;
            float age = VariantsFor(kind) <= 1 ? 0.85f : Mathf.Lerp(0.6f, 1f, variant / (float)(VariantsFor(kind) - 1));

            Mesh stemMesh, foliageMesh, accentMesh = null;
            Color stemTint, foliageTint;

            if (kind == Kind.Lily)
            {
                // A pad is a flat disc, which the ROCK generator already makes if you squash one axis. Building
                // a disc primitive in the leaf mesher for a single prototype would be the wrong trade.
                var pad = RockGenerator.Generate(LilyPadDef(), seed);
                if (pad.Lod0 == null || pad.Lod0.vertexCount == 0) return null;
                stemMesh = pad.Lod0;
                foliageMesh = null;
                stemTint = new Color(0.24f, 0.42f, 0.20f);
                foliageTint = stemTint;
            }
            else
            {
                TreeDef def = DefFor(kind, p, age);
                GeneratedTree t = TreeGenerator.Generate(def, seed);
                if (t.Bark == null || t.Bark.vertexCount == 0) return null; // keep Synty
                stemMesh = t.Bark;
                foliageMesh = t.Foliage != null && t.Foliage.vertexCount > 0 ? t.Foliage : null;
                accentMesh = t.Accent != null && t.Accent.vertexCount > 0 ? t.Accent : null;
                stemTint = def.BarkColor;
                foliageTint = def.LeafColor;
                // Coral and lily are the only kinds with no foliage tier by design; anything else arriving
                // bare means the generator failed, and a bare twig is worse than the Synty prop.
                if (foliageMesh == null && kind != Kind.Coral) return null;
            }

            Material stem = GeneratedFoliage.Solid(key + " stem", stemTint);
            Material foliage = null;
            if (foliageMesh != null)
            {
                foliage = GeneratedFoliage.Leaf(key, foliageTint, WindFor(kind), lift: 2.2f);
                if (foliage == null) return null; // no clean leaf material to tint — keep the Synty prop
            }

            float cull = p.Parts[0].MaxCullDistance;
            if (cull < 10f) cull = 60f;
            float[] dist = { cull };

            var parts = new List<ScatterPartDto> { new ScatterPartDto(stem, new[] { stemMesh }, dist, false, true) };
            if (foliageMesh != null) parts.Add(new ScatterPartDto(foliage, new[] { foliageMesh }, dist, false, true));
            // Blooms are a third part in their own colour, which is what makes a flowering bush read as a green
            // shrub speckled with flowers instead of a solid coloured blob.
            if (accentMesh != null)
            {
                Material bloom = GeneratedFoliage.Leaf(key + " bloom", BloomColor(p.Biome), WindFor(kind), lift: 2.2f);
                if (bloom != null) parts.Add(new ScatterPartDto(bloom, new[] { accentMesh }, dist, false, true));
            }

            var gen = p with
            {
                DisplayName = variant == 0 ? p.DisplayName : $"{p.DisplayName} v{variant}",
                SlotId = slot,
                SpacingMeters = p.SpacingMeters * spacingScale,
                Parts = parts.ToArray(),
                BakedImpostorAtlas = null,
                BakedImpostorNormal = null,
                SpeciesKey = key,
            };
            return UseBakedImpostors ? GeneratedImpostorManifest.WithCachedAtlas(gen) : gen;
        }
        catch (Exception e)
        {
            LoggerProvider.LogException("PlantInject", e);
            return null;
        }
    }

    static TreeDef DefFor(Kind kind, ScatterPrototypeDto p, float age)
    {
        string n = p.DisplayName ?? "";
        return kind switch
        {
            // A flowering bush stays GREEN and grows a separate bloom tier; the flower colour rides on that
            // tier's own material rather than the whole canopy.
            Kind.Bush => TreeDefLibrary.Bush(age, LeafTint(p.Biome, true, false), blooms: Has(n, "FlowerBush")),
            Kind.Grass => TreeDefLibrary.GrassTuft(age, LeafTint(p.Biome, false, false)),
            Kind.Flower => TreeDefLibrary.Flower(age, FlowerColor(n), Has(n, "Wildflower") ? 0.28f : 0.36f),
            Kind.Mushroom => TreeDefLibrary.Mushroom(age, MushroomCap(n)),
            Kind.Reed => TreeDefLibrary.Reed(age, LeafTint(p.Biome, false, false)),
            Kind.Cattail => TreeDefLibrary.Cattail(age),
            Kind.Kelp => TreeDefLibrary.Kelp(age),
            Kind.Coral => TreeDefLibrary.Coral(age, CoralColor(n), Has(n, "Deep") || Has(n, "Shallow")),
            _ => TreeDefLibrary.Bush(age),
        };
    }

    // Blades and petals catch the wind; a coral is rigid, and swaying it with the land wind field would read
    // as wrong more clearly than not moving at all.
    static float WindFor(Kind kind) => kind switch
    {
        Kind.Coral => 0f,
        Kind.Lily => 0f,
        Kind.Grass or Kind.Reed or Kind.Cattail => 0.22f,
        Kind.Kelp => 0.3f,
        _ => 0.14f,
    };

    // The Synty flower prototypes name their colour, which is the only thing distinguishing them — so read it
    // rather than inventing a palette that would not match the biome each was placed for.
    static Color FlowerColor(string n)
    {
        if (Has(n, "Blue")) return new Color(0.42f, 0.55f, 0.86f);
        if (Has(n, "Red")) return new Color(0.80f, 0.24f, 0.24f);
        if (Has(n, "Yellow")) return new Color(0.90f, 0.78f, 0.24f);
        if (Has(n, "White")) return new Color(0.90f, 0.90f, 0.86f);
        if (Has(n, "Purple")) return new Color(0.60f, 0.40f, 0.78f);
        if (Has(n, "Pink")) return new Color(0.88f, 0.52f, 0.66f);
        return new Color(0.86f, 0.80f, 0.42f); // "Wildflowers" — mixed meadow, leans warm
    }

    // Bloom colour for a flowering bush, kept warm and distinct from the canopy so the speckle actually reads.
    static Color BloomColor(BiomeType biome) => biome switch
    {
        BiomeType.Forest or BiomeType.Taiga => new Color(0.86f, 0.74f, 0.80f), // woodland pink-white
        BiomeType.Tropical => new Color(0.90f, 0.52f, 0.62f),
        BiomeType.Swamp => new Color(0.82f, 0.80f, 0.52f),
        _ => new Color(0.92f, 0.82f, 0.34f),                                   // meadow yellow
    };

    static Color MushroomCap(string n) =>
        Has(n, "Red") ? new Color(0.66f, 0.16f, 0.13f)
        : Has(n, "Swamp") ? new Color(0.44f, 0.36f, 0.24f)
        : new Color(0.56f, 0.42f, 0.28f);

    static Color CoralColor(string n) =>
        Has(n, "Deep") ? new Color(0.52f, 0.36f, 0.62f)
        : Has(n, "Plate") ? new Color(0.82f, 0.56f, 0.32f)
        : Has(n, "Shallow") ? new Color(0.86f, 0.50f, 0.52f)
        : new Color(0.72f, 0.42f, 0.44f);

    // Biome-appropriate foliage. Dry biomes get olive/straw, wet ones a deeper green, and a flower bush gets a
    // muted bloom colour over the whole canopy.
    // ponytail: whole-canopy tint, because a TreeDef carries ONE LeafColor and every leaf tier merges into one
    // foliage mesh. Green leaves with flowering tips need per-tier vertex colour in the leaf mesher.
    // Biome-appropriate foliage. GREEN CHANNEL MUST LEAD: the first version gave the dry biomes R == G, which
    // is yellow by definition, and the steppe filled with neon-yellow bushes. A dry plant is a muted SAGE
    // green — desaturated and darker, never shifted toward red. Every value here keeps G above R by a clear
    // margin, which is what the reference art does too.
    static Color LeafTint(BiomeType biome, bool isBush, bool flowering)
    {
        Color green = biome switch
        {
            BiomeType.Desert or BiomeType.Scrub or BiomeType.Steppe => new Color(0.30f, 0.42f, 0.22f),
            BiomeType.Savanna => new Color(0.33f, 0.44f, 0.22f),
            BiomeType.Swamp => new Color(0.20f, 0.36f, 0.17f),
            BiomeType.Taiga or BiomeType.Snow => new Color(0.19f, 0.35f, 0.20f),
            BiomeType.Tundra or BiomeType.IceBog => new Color(0.26f, 0.38f, 0.25f),
            BiomeType.Tropical => new Color(0.17f, 0.43f, 0.18f),
            _ => new Color(0.23f, 0.42f, 0.19f),
        };
        // A flowering bush stays GREEN. Tinting the whole canopy pink or yellow made a solid coloured blob,
        // where the reference is a green shrub SPECKLED with blooms — and speckling needs per-tier vertex
        // colour in the leaf mesher, which does not exist yet. Until it does, a slightly lighter green is
        // closer to right than a wrong colour.
        if (flowering) green *= 1.12f;
        return isBush ? green : green * 1.12f; // blades catch more light than a bush interior
    }

    // Kinds whose look is driven by a per-prototype colour get their own card; the rest share per biome.
    static string ShareKey(ScatterPrototypeDto p, Kind kind)
    {
        string n = p.DisplayName ?? "";
        return kind switch
        {
            Kind.Bush => (Has(n, "FlowerBush") ? "flowerbush-" : "bush-") + p.Biome,
            Kind.Grass => "grass-" + p.Biome,
            Kind.Flower => "flower-" + ColorWord(n),
            Kind.Mushroom => "mushroom-" + ColorWord(n),
            Kind.Coral => "coral-" + ColorWord(n),
            _ => kind.ToString().ToLowerInvariant() + "-" + p.Biome,
        };
    }

    static string ColorWord(string n)
    {
        foreach (string w in new[] { "Blue", "Red", "Yellow", "White", "Purple", "Pink", "Deep", "Plate", "Shallow", "Swamp" })
            if (Has(n, w)) return w;
        return "default";
    }

    static RockDef LilyPadDef() => new RockDef
    {
        Name = "Lily Pad", Size = 1.05f, AxisBias = new Vector3(1f, 0.035f, 1f),
        Roughness = 0.22f, Subdivisions = 1, Buried = 0f,
        Color = new Color(0.24f, 0.42f, 0.20f),
    };


    static GameObject _gallery;

    // Every generated plant kind in one row in front of the camera, at real scale against each other. This is
    // the review surface — a flower that looks fine alone can be twice the height of the bush beside it, and
    // that only shows up side by side.
    [ConsoleCommand("gallery", "Spawn one of every generated plant kind in a row in front of the camera. Optional seed.", MonoTargetType.Static)]
    static string GalleryCmd(int? seed = null)
    {
        Camera cam = Camera.main;
        if (cam == null) return "plant.gallery: no main camera";
        if (_gallery != null) UnityEngine.Object.DestroyImmediate(_gallery);

        int s = seed ?? 4242;
        var entries = new List<(string label, TreeDef def)>
        {
            ("Bush", TreeDefLibrary.Bush(1f)),
            ("FlowerBush", TreeDefLibrary.Bush(1f, LeafTint(BiomeType.Grassland, true, true))),
            ("Grass", TreeDefLibrary.GrassTuft(1f)),
            ("Reed", TreeDefLibrary.Reed(1f)),
            ("Cattail", TreeDefLibrary.Cattail(1f)),
            ("Kelp", TreeDefLibrary.Kelp(1f)),
            ("Mushroom", TreeDefLibrary.Mushroom(1f)),
            ("Coral Head", TreeDefLibrary.Coral(1f, CoralColor(""), false)),
            ("Coral Finger", TreeDefLibrary.Coral(1f, CoralColor("Shallow"), true)),
            ("Flower Yellow", TreeDefLibrary.Flower(1f, FlowerColor("Yellow"))),
            ("Flower Blue", TreeDefLibrary.Flower(1f, FlowerColor("Blue"))),
            ("Flower Red", TreeDefLibrary.Flower(1f, FlowerColor("Red"))),
        };

        _gallery = new GameObject("PlantGallery");
        Vector3 origin = cam.transform.position + cam.transform.forward * 6f;
        Vector3 up = origin.normalized;
        Vector3 right = Vector3.Cross(up, cam.transform.forward).normalized;
        if (right.sqrMagnitude < 0.5f) right = Vector3.Cross(up, Vector3.right).normalized;

        int built = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            (string label, TreeDef def) = entries[i];
            GeneratedTree t = TreeGenerator.Generate(def, s + i);
            if (t.Bark == null || t.Bark.vertexCount == 0) continue;

            var go = new GameObject(label);
            go.transform.SetParent(_gallery.transform, false);
            go.transform.SetPositionAndRotation(
                origin + right * ((i - entries.Count * 0.5f) * 1.6f),
                Quaternion.FromToRotation(Vector3.up, up));

            AddMesh(go.transform, "stem", t.Bark, GeneratedFoliage.Solid("gallery " + label + " stem", def.BarkColor));
            if (t.Foliage != null && t.Foliage.vertexCount > 0)
                AddMesh(go.transform, "foliage", t.Foliage, GeneratedFoliage.Leaf("gallery " + label, def.LeafColor));
            built++;
        }

        // The lily pad is the odd one out: it comes from the rock generator, not a TreeDef.
        var padRock = RockGenerator.Generate(new RockDef
        {
            Name = "Lily Pad", Size = 1.05f, AxisBias = new Vector3(1f, 0.035f, 1f),
            Roughness = 0.22f, Subdivisions = 1, Buried = 0f, Color = new Color(0.24f, 0.42f, 0.20f),
        }, s);
        if (padRock.Lod0 != null)
        {
            var go = new GameObject("Lily Pad");
            go.transform.SetParent(_gallery.transform, false);
            go.transform.SetPositionAndRotation(
                origin + right * ((entries.Count - entries.Count * 0.5f) * 1.6f),
                Quaternion.FromToRotation(Vector3.up, up));
            AddMesh(go.transform, "pad", padRock.Lod0, GeneratedFoliage.Solid("gallery lily", new Color(0.24f, 0.42f, 0.20f)));
            built++;
        }

        return $"plant.gallery: {built} plant(s) at {origin}, seed {s}. Kinds: " +
               string.Join(", ", entries.ConvertAll(e => e.label)) + ", Lily Pad";
    }

    static void AddMesh(Transform parent, string name, Mesh mesh, Material mat)
    {
        if (mesh == null || mat == null) return;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    [ConsoleCommand("inject", "Replace scatter bushes/grass/flowers/mushrooms/reeds/coral with generated plants (on/off), then run `planet.generate`.", MonoTargetType.Static)]
    static string InjectCmd(string state = "on")
    {
        Enabled = state == "on" || state == "true" || state == "1";
        return Reapply($"generated-plant injection {(Enabled ? "ON" : "OFF")}");
    }

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

    static int MaxSlot(ScatterLibraryDto lib)
    {
        int max = -1;
        foreach (ScatterPrototypeDto p in lib.Prototypes)
            if (p != null && p.SlotId > max) max = p.SlotId;
        return max;
    }
}
