# Ocean scatter — populating the water

**Status:** first prototype authored 2026-08-12, **no code changes required**.
**Why:** `Corals - Coral1_1` passed the asset bench and had nowhere to go.

> **Correction (2026-08-12).** An earlier revision of this doc framed ocean scatter as a new
> subsystem: emit a seabed biome, add a below-water placement mode, invent a depth axis. Bryan
> pushed back — *"why is the ocean treated different than any other biome? Shouldn't this be the
> same system everywhere?"* — and he was right. It **is** the same system. The ocean was not
> special-cased, it was simply unpopulated. What follows is the corrected picture.

---

## The system already handles underwater

Three facts, verified in the placement code:

**1. Altitude is signed metres above sea level.**

```csharp
// ScatterField.cs:293  (and ScatterGatherJob.cs:129)
float altitudeMeters = onWater ? 0f : (localRadius - ctx.SeaRadiusLocal) * scale;
```

Below the waterline it is simply negative. `ScatterPrototype`'s own tooltip says so:
*"Altitude gate (metres above sea; negative = underwater)"*.

**2. A depth band is an ordinary altitude gate.**

```csharp
// ScatterGatherBurst.cs:110
if (rules.HasMinAltitude != 0 && altitudeMeters < rules.MinAltitude) return false;
if (rules.HasMaxAltitude != 0 && altitudeMeters > rules.MaxAltitude) return false;
if (hasOcean && rules.MinWaterClearance > 0f && altitudeMeters < rules.MinWaterClearance) return false;
```

`MinAltitude = -30, MaxAltitude = -3` is "the shelf". No new field, no new axis.

**3. `MinWaterClearanceMeters` is a per-prototype opt-out, not a system boundary.** It only
rejects when `> 0`. `Lake Lily` already ships with it at `0`. Set it to zero and the
keep-above-water rule stops applying to that prototype.

**Placement needs nothing new either.** Props already place at `localRadius`, the terrain
surface — which below the waterline *is* the seabed. `OnWater` (the lily-pad path) is the special
case; ordinary terrain placement is the general one, and it works at any depth.

### Proof: `Ocean Coral`

Authored with zero code changes — `Assets/Resources/Settings/Scatter/Ocean Coral Prototype.asset`,
slot 69, added to `ScatterLibrary` (now 70 prototypes):

| Field | Value | Why |
|---|---|---|
| `Biome` | `Ocean` | already emitted by `BiomeRegistryDto` |
| `MinWaterClearanceMeters` | `0` | opt out of the keep-above-water rejection |
| `HasMinAltitude` / `MinAltitudeMeters` | `true` / `-30` | no deeper than 30 m |
| `HasMaxAltitude` / `MaxAltitudeMeters` | `true` / `-3` | at least 3 m under the surface |
| `ConformToSlope` | `0.7` | lies on the seabed rather than standing radially |
| `ScaleRange` | `1.5 – 3.5` | source meshes are sub-metre (0.56 × 0.52 × 0.60) |

Material `Assets/Art/Materials/CoralShelf.mat` is on `Scatter/FoliageLit` carrying the vendor
albedo — the same rule the bench judges by.

**Depth banding is per-prototype, which is better than a biome axis.** Shelf, reef and deep-floor
sets are three prototypes with different altitude gates, competing for placement exactly like
land biomes do. Adding a `depth` dimension to the biome resolver would have duplicated a
mechanism that already exists.

`BiomeType.Underwater` (16) and `Cave` (15) are still declared and emitted nowhere — but nothing
here needs them. `Ocean` plus an altitude gate covers the seabed. They remain dead enum values.

---

## What is genuinely unbuilt

Placement is solved. These are not:

### Underwater rendering — **checked 2026-08-12, no custom shader needed yet**

Reviewed in-world on a reef shelf. Verdict from Bryan: *"the underwater foliage looks fine"* —
**no ocean-specific foliage shader for now.**

What the first look settled:

- **`Scatter/FoliageLit` already receives the underwater fog.** This was the main risk and it is
  not one: distant corals attenuate into the water the same way the terrain and the shoreline do.
  Submerged scatter does not read as "lit for open air".
- **Caustics still do not reach scatter.** The seabed terrain shows the rippled light; the corals
  sitting on it stay flat-lit. Visible on close inspection, not objectionable at swimming distance.
- Corals read slightly warm and saturated against the blue — acceptable, and cheaper to fix by
  tinting the material than by writing a shader.
- Far-field impostors underwater are still unverified; nothing in view was far enough to tier down.

**A custom ocean foliage shader is therefore deferred, not required.** Revisit only if caustics on
props turn out to matter, or if deeper water makes the absorption mismatch obvious — at shelf
depths it is not.

### General underwater effects — separate, and the actual gap

Bryan, same session: *"the underwater effects in general need work (outside the foliage)."* This
is water rendering, not scatter, and it is the thing to fix before any more effort goes into
submerged props. Not yet scoped. It is what the eye notices first down there, so scatter polish
ahead of it would be wasted.

### Density, and the size of the ocean

The largest surface on the planet. Uniform density is neither affordable nor desirable — scatter
should concentrate on shelves and reefs. A reef *is* a colony, so this likely wants the clumping
work in `project_scatter_clumping_direction` rather than a bespoke solution.

Because the player will be down there, density has a **floor as well as a ceiling**: an empty
seabed swum across is worse than one flown over, since the emptiness becomes the experience.

### Content

One coral is not a biome. `Assets/AssetPacks/Corals/` holds `Corals.FBX` (33 meshes) and one
prefab; `CoralGroups.FBX`, `CoralRocks.FBX` and `Seaweeds.FBX` are still in the scratch project
and can go through the bench next.

---

## Decided

**The player will be down there** (Bryan, 2026-08-12). The seabed is real content, not set
dressing — so underwater rendering is required work rather than an open question, and ocean
candidates should be benched at swimming eye height, since scale does not read the same from a
camera above the surface.

## Still open

1. **Does ocean scatter block on swimming?** No. The free camera already goes underwater and the
   bench can spawn there.
2. **`Cave` (15) is the same unemitted-biome gap**, and also somewhere the player will be. Unlike
   the ocean it probably *does* need resolver work, since there is no altitude trick for "inside".
   Fold in, or keep separate?

---

## Related

- Bench verdict: `docs/research/bench-style-batch-2-2026-08-12.md`
- Lake biome, the precedent for water-adjacent scatter: `docs/design/2026-08-11-lake-biome.md`
- Placement rules: `Assets/Scripts/Planet/Scatter/ScatterField.cs`, `ScatterGatherJob.cs`,
  `ScatterGatherBurst.cs`
