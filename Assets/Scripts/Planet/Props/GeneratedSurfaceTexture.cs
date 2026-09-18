using System.Collections.Generic;
using UnityEngine;

// Small tiling surface textures for generated props. These are LUMINANCE patterns, near-white with darker
// detail, because the material multiplies them by a per-species base colour — so one texture serves every tint
// of a style and the colour stays authored in the TreeDef.
//
// Why generated rather than borrowed from a pack: the birch bark proved the point. A flat-tinted trunk reads as
// plastic, and the one tree that looked right was the only one with a texture. Generating them keeps every
// species consistent, avoids the palette-atlas problem that bites the source leaf textures, and costs almost
// nothing — a handful of small textures, built once per session and shared by every instance.
public static class GeneratedSurfaceTexture
{
    public enum BarkStyle { Smooth, Furrowed, Plated, Birch, Ribbed, Fibrous }

    static readonly Dictionary<string, Texture2D> _cache = new();

    public static Texture2D CutWood()
    {
        const string key = "cut-wood";
        if (_cache.TryGetValue(key, out Texture2D hit) && hit != null) return hit;
        const int size = 64;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float radius = new Vector2((x + .5f) / size - .5f, (y + .5f) / size - .5f).magnitude;
            float ring = Mathf.Pow(.5f + .5f * Mathf.Sin(radius * 100f), 6f);
            float value = 1f - ring * .18f;
            pixels[y * size + x] = new Color(value, value, value, 1f);
        }
        return Store(key, pixels, size, size, "Wood growth rings");
    }

    public static Texture2D Bark(BarkStyle style, int seed = 1)
    {
        string key = "bark-" + style + "-" + seed;
        if (_cache.TryGetValue(key, out Texture2D hit) && hit != null) return hit;

        const int w = 64, h = 128;
        var px = new Color[w * h];
        var rng = new Rng((uint)(seed * 2654435761u) ^ ((uint)style * 40503u));

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float l = Luma(style, x, y, w, h);
            px[y * w + x] = new Color(l, l, l, 1f);
        }

        if (style == BarkStyle.Birch) AddDashes(px, w, h, ref rng);
        else if (style == BarkStyle.Plated) AddPlateCracks(px, w, h, ref rng);
        else if (style == BarkStyle.Furrowed) AddHorizontalCracks(px, w, h, ref rng);

        return Store(key, px, w, h, "Gen bark " + style);
    }

    // Mottled stone for generated rocks. Rock UVs are per-FACE (see RockGenerator.FlatShade), and each face
    // samples a different cell of the texture, so this only has to look like stone within a cell — nothing has
    // to line up across an edge.
    public static Texture2D Stone(int seed = 1)
    {
        string key = "stone-" + seed;
        if (_cache.TryGetValue(key, out Texture2D hit) && hit != null) return hit;

        const int n = 128;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            // Three octaves: broad blotches, grain, then a fine speckle that keeps a big flat facet alive.
            float v = 0.55f
                    + 0.30f * Mathf.PerlinNoise(x * 0.045f + seed, y * 0.045f + seed)
                    + 0.14f * Mathf.PerlinNoise(x * 0.17f + seed * 3f, y * 0.17f + seed * 3f)
                    + 0.08f * Mathf.PerlinNoise(x * 0.6f, y * 0.6f);
            px[y * n + x] = new Color(v, v, v, 1f);
        }
        return Store(key, px, n, n, "Gen stone");
    }

    static Texture2D Store(string key, Color[] px, int w, int h, string name)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, true)
        {
            name = name,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
        };
        t.SetPixels(px);
        t.Apply();
        _cache[key] = t;
        return t;
    }

    static float Luma(BarkStyle style, int x, int y, int w, int h)
    {
        float u = x / (float)w, v = y / (float)h;
        switch (style)
        {
            // Oak/broadleaf: STRAIGHT vertical ridges broken into blocks by horizontal cracks.
            //
            // The ridge offset must depend on u ONLY. A v term here shifts each row sideways, so the ridge
            // snakes up the trunk — that is the "wavy" look, and no real bark does it. Ridge WIDTH varies from
            // column to column instead, which gives irregularity without wander. The blocky character comes
            // from AddHorizontalCracks, not from bending the ridges.
            case BarkStyle.Furrowed:
            {
                // FOUR ridges per tile, not seven: the trunk mesher tiles u roughly three times around a
                // metre-thick trunk, so seven became twenty-odd pinstripes instead of the handful of broad
                // ridges an oak actually has.
                float colJitter = Mathf.PerlinNoise(u * 9f, 0.37f) * 0.55f;
                float ridge = Mathf.Abs(Mathf.Sin((u * 4f + colJitter) * Mathf.PI));
                float grain = Mathf.PerlinNoise(u * 30f, v * 3f) * 0.12f;
                return Mathf.Clamp01(Mathf.Lerp(0.42f, 1f, Mathf.Pow(ridge, 0.5f)) - grain);
            }
            // Conifer/pine: irregular scaly flakes. Quantised so the plates have edges rather than reading as a
            // soft blob field, then AddPlateCracks cuts the seams between them.
            case BarkStyle.Plated:
            {
                float plate = Mathf.PerlinNoise(u * 9f, v * 4.5f);
                plate = Mathf.Floor(plate * 5f) / 5f;
                return 0.64f + 0.32f * plate;
            }
            // Palm/cedar: long straight fibrous strips. v barely participates for the same reason the furrows
            // ignore it — a fibre that drifts sideways reads as a wave, not a fibre.
            case BarkStyle.Fibrous:
                return 0.68f + 0.28f * Mathf.PerlinNoise(u * 46f, v * 0.5f);
            // Hard vertical ribs. A saguaro is defined by them, so they run the full height with little noise.
            case BarkStyle.Ribbed:
                return Mathf.Lerp(0.62f, 1f, Mathf.Pow(Mathf.Abs(Mathf.Sin(u * 8f * Mathf.PI)), 0.6f));
            case BarkStyle.Birch:
                return 0.94f + 0.06f * Mathf.PerlinNoise(u * 22f, v * 8f);
            default:
                return 0.86f + 0.14f * Mathf.PerlinNoise(u * 14f, v * 6f);
        }
    }

    // Birch's horizontal lenticel dashes: short, dark, scattered. These are the marking that makes a pale
    // trunk read as a birch rather than a dead tree.
    static void AddDashes(Color[] px, int w, int h, ref Rng rng)
    {
        for (int band = 0; band < 26; band++)
        {
            int y = (int)(rng.Next() * h), x = (int)(rng.Next() * w);
            int len = 3 + (int)(rng.Next() * 11), thick = 1 + (int)(rng.Next() * 3);
            float dark = 0.10f + rng.Next() * 0.22f;
            for (int dy = 0; dy < thick; dy++)
            for (int dx = 0; dx < len; dx++)
            {
                int xx = (x + dx) % w, yy = (y + dy) % h;
                float taper = 1f - Mathf.Abs(dx / (float)len - 0.5f) * 0.7f;
                px[yy * w + xx] = Color.Lerp(px[yy * w + xx], new Color(dark, dark, dark, 1f), taper);
            }
        }
    }

    // Short horizontal cracks that chop the straight vertical ridges into blocks — the oak/ash look. Each crack
    // spans only a couple of ridges, so the trunk never gets a band running all the way round it.
    static void AddHorizontalCracks(Color[] px, int w, int h, ref Rng rng)
    {
        for (int c = 0; c < 90; c++)
        {
            int y = (int)(rng.Next() * h);
            int x = (int)(rng.Next() * w);
            int len = 6 + (int)(rng.Next() * 14);
            float dark = 0.30f + rng.Next() * 0.22f;
            for (int d = 0; d < len; d++)
            {
                int xx = (x + d) % w;
                // Wander by a pixel vertically so the crack is not a ruled line.
                int yy = (y + (rng.Next() < 0.3f ? 1 : 0)) % h;
                px[yy * w + xx] *= dark;
            }
        }
    }

    // Dark seams between conifer bark plates.
    static void AddPlateCracks(Color[] px, int w, int h, ref Rng rng)
    {
        for (int c = 0; c < 40; c++)
        {
            int x = (int)(rng.Next() * w), y = (int)(rng.Next() * h);
            int len = 4 + (int)(rng.Next() * 14);
            for (int d = 0; d < len; d++)
            {
                int yy = (y + d) % h;
                int xx = (x + (int)(rng.Next() * 2.4f) - 1 + w) % w;
                px[yy * w + xx] *= 0.62f;
            }
        }
    }

    // xorshift: deterministic and independent of UnityEngine.Random's global state, which a texture built
    // during world generation must not disturb.
    struct Rng
    {
        uint _s;
        public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }
        public float Next()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return (_s & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}
