# Ocean scatter — populating the water

**Status:** not started. Tracking doc, written 2026-08-12 after the asset bench kept a coral.
**Why now:** `Corals - Coral1_1` passed the bench (`docs/research/bench-style-batch-2-2026-08-12.md`)
and is promoted to `Assets/AssetPacks/Corals/`. There is nowhere to put it.

---

## Current state (verified 2026-08-12)

**Ocean has no scatter at all.** 69 prototypes exist; a scan of their `Biome` values shows
1–14, 17, 18 in use. Three biome types have zero prototypes:

| Value | `BiomeType` | Emitted by the resolver? |
|---|---|---|
| 0 | `Ocean` | yes — `BiomeRegistryDto` blends Ocean↔Beach |
| 15 | `Cave` | **never** |
| 16 | `Underwater` | **never** |

`Underwater` is declared in the enum and referenced nowhere. It is a placeholder, not a feature.

**Scatter cannot place below the waterline today.** Two water-aware paths exist and neither
puts anything on the seabed:

- `MinWaterClearanceMeters` — a *rejection* gate. `ScatterGatherBurst` drops any instance whose
  `altitudeMeters < MinWaterClearance`. It exists to keep land props out of the water.
- `OnWater` — pins the instance to the sea surface
  (`ScatterField`: `placeRadius = ctx.SeaRadiusLocal + OnWaterSurfaceOffsetMeters / scale`).
  This is the lily-pad path from the lake biome. It floats things *on* the water.

There is no mode that places an instance on the terrain surface *underneath* the water.

---

## What the work actually is

Roughly in dependency order. None of this is estimated yet.

### 1. Emit a seabed biome

Decide whether the seabed is `Underwater` or just `Ocean` with a depth axis, then make the
resolver emit it. Today `Ocean` means "this cell is water", with no notion of what is beneath.
Depth almost certainly needs to be a resolver input: a coral shelf and an abyssal plain are not
the same biome, and the difference is the only thing that makes ocean scatter look deliberate.

If `Underwater` stays, it needs a real definition (colour, textures, grass params) or it will
fall through the biome atlas the way an unregistered biome does.

### 2. A below-water placement mode

`OnWater` and `MinWaterClearance` do not compose into "on the seabed". The likely shape is a
third mode on `ScatterPrototype` — place on terrain, require the terrain to be *below* sea
level, and gate on depth rather than altitude. Needs CPU/Burst parity
(`ScatterField` + `ScatterGatherJob` + `ScatterGatherBurst`), as every placement rule does.

Depth gating wants a min and a max: corals in the shallows, nothing in the deep.

### 3. Rendering underwater

Open question, and the one most likely to produce ugly results:

- Does `Scatter/FoliageLit` read correctly through the water volume, or do submerged props need
  the ocean's fog/absorption applied? A prop lit as though it were in air will not sit in the water.
- Caustics: the ocean shader has them; scatter does not receive them.
- The far-field impostor tier bakes against a sky background. Underwater it should not.

### 4. Depth-aware density and LOD

Ocean is the largest surface on the planet. Uniform density is not affordable and not desirable —
scatter should concentrate on shelves and reefs. This may want the clumping/colony work already
noted in `project_scatter_clumping_direction`, since a reef *is* a colony.

### 5. Content

One coral prefab is not a biome. `Assets/AssetPacks/Corals/` currently holds `Corals.FBX`
(33 sub-meshes) plus one prefab; the source pack also has `CoralGroups.FBX`, `CoralRocks.FBX` and
`Seaweeds.FBX` still in the scratch project, and the bench can judge those next.

---

## Decided

**The player will be down there** (Bryan, 2026-08-12). The seabed is real content, not set
dressing. That rules out the cheap version — a shallow shelf band decorated for viewing from
above — and commits us to the depth axis in §1.

Consequences worth stating now, because they are easy to forget later:

- **Depth bands are a biome axis, not a density curve.** Shelf, reef, slope and deep floor want
  different prototype sets, not one set thinned with depth. Whatever §1 emits has to carry enough
  resolution to say which band a cell is in.
- **Underwater rendering stops being optional.** Content the player swims through has to be lit
  through the water volume, receive caustics, and fade into the correct fog. §3 moves from
  "open question" to required work.
- **Scale reads differently underwater.** Props judged from a free camera above the surface will
  not read the same at swimming eye height. Bench any ocean candidate from where the player will
  actually be.
- **Density has a floor as well as a ceiling.** An empty seabed the player swims across is worse
  than an empty one they fly over — the emptiness is the experience rather than a distant texture.

## Still open

1. **Does ocean scatter block on swimming being implemented?** It does not: the free camera can
   already go underwater, and the bench can spawn there. Judging at swimming eye height is a
   camera position, not a feature dependency.
2. **`Cave` (15) is also unemitted** — the same class of gap, and also somewhere the player will
   physically be. Fold into this pass, or keep deliberately separate?

---

## Related

- Bench verdict and follow-ups: `docs/research/bench-style-batch-2-2026-08-12.md`
- Lake biome, the closest precedent for adding a water biome + its scatter:
  `docs/design/2026-08-11-lake-biome.md`
- Scatter placement rules: `Assets/Scripts/Planet/Scatter/ScatterField.cs`,
  `ScatterGatherJob.cs`, `ScatterGatherBurst.cs`
