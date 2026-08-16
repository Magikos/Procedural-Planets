---
name: project_tree_generator
description: Custom procedural tree/vegetation generator (plan 006) replacing Synty tree meshes — our own code (Broccoli-studied). PLAY-VERIFIED + iterated 2026-08-15: textured foliage per biome, palm fronds, spiky shaded fir, open pine. Vision = generate NEARLY ALL vegetation.
metadata:
  type: project
---

**2026-08-15 UPDATE (branch harvest-vertical-slice, PLAY-VERIFIED across many screenshots, Bryan tuned each):**
Trees now look good in-world. Vision confirmed by Bryan: **"generate nearly all our vegetation this way"** — one
generator = stem/branch structure (levels) + a foliage primitive. Staying on trees until the full archetype set
is nailed down. **Full roadmap + per-archetype build specs live in `plans/006` "Vegetation archetype roadmap"**
**Reference images ARE saved in `docs/research/tree-references/`** (15 files, added 2026-08-15) — re-openable with
the Read tool any session. Best: `tree-forms-urban-forestry.jpg` = 10 crown forms (round/spreading/pyramidal/oval/
conical/vase/columnar/open/weeping/irregular), the structural axis; plan 006 catalogs the rest. Missing/would-help:
LOW-POLY/Synty-style tree shots + NON-TREE vegetation refs (ferns, reeds, cacti, bushes, flowers, vines).

**SIZES ARE NOW REAL METRES (2026-08-15, Bryan: "also work on the sizes" + he saved `highest-trees.jpg`).** Trees
were ~half scale (mature Broadleaf 14.4 m — SMALLER than the Synty tree it replaced). `TreeDef.MaxHeight` (metres
at Age 1) is the single size knob; the recursion builds in proportion units and `TreeStructureGenerator.
NormalizeHeight` scales the finished skeleton, so authored ratios + segment density survive. Broadleaf 32 /
Conifer 42 / Pine 38 / Birch 22 / Palm 20 / Willow 18 / Acacia 14 / Shrub 2.6. Two couplings, both non-obvious:
leaf clumps scale **sublinearly** (`f^0.65`, else a big tree is one inflated blob) so clump **count** must scale
up (`f^0.35`, estimated off the trunk) or the crown thins; and the age curve is superlinear
(`lerp(.06,1,age^1.6)`) so a sapling is a fraction, not a half-size copy — Broadleaf 3.7/9.6/20.3/34.1 m.
Broadleaf crown re-proportioned to Round/Spreading (W/H .45→**.85**, crown base 8.2 m of 34 m).
**Bryan's taste calls, recorded:** wants trees calibrated to REAL heights, not game-shrunk; and
**Synty POLYGON** (rounded, faceted-but-detailed), never the blocky cube-blob low-poly. **Scatter spacing is
still tuned for half-size trees — forests may jam; unverified.**

**Trunk + crown quality pass (2026-08-15, after the size work).** `TreeDef.RootFlare` (default .45, Palm .18,
Shrub .12) widens the base ring decaying over the bottom fifth — trunks meet the ground on a buttress, and the
stump cut-set inherits it free. Broadleaf `TrunkTipScale` .28→.18 + primary `GirthScale` .52→.62 so the trunk
forks into heavy limbs instead of running full-width with twigs. Conifer cone `baseFrac`/`radiusFrac` params
(.12/.26 — was hardcoded .28 with a comment wrongly claiming pine behaviour; spruce/fir go near-ground, PINE is
the one with the clear bole). Acacia → real umbrella (`ParallelAlign` .84, leaves only in the outer .72–1 of long
near-horizontal limbs, so the space UNDER the canopy stays open). **GOTCHA worth keeping: enlarging Pine's tufts
to read better merged them into a solid mass indistinguishable from the Conifer cone — variety LOST. Sky between
the puffs is the feature.** Landed LeafSize 1.5 + fewer tips (branches 5-8, tufts 1-3).

**Bryan's review round (he supplied willow photos): trunks too thick / conifer hid its trunk / willow too narrow.**
Global girth 0.05→0.042, age-girth top 1.0→0.82, default RootFlare .45→.32. Then **`TreeDef.TrunkGirthScale`** —
one global girth ratio CANNOT serve both shapes: a broadleaf trunk is ~60% of the tree (crown above), a conifer's
runs full height, so length-proportional girth made the fir a 4.4 m column. Conifer .55 / Pine .6 / Birch .35 /
Palm .3. Trunk dia as % of height now: Broadleaf 6.6, Conifer 5.0, Pine 5.7, Birch 3.2, Palm 2.7, Willow 6.1.
**Conifer-cone gotcha:** `baseFrac` is where the lowest skirt HANGS FROM, and it droops ~0.7-0.9 × that tier's
radius below it (bottom tier = widest), so baseFrac .12 buried the skirts in the ground; .30 + droop .7 gives
~6 m clear bole on a 49 m fir. **Willow:** wide is the whole point (W/H **1.50**, 27 m on 18 m) —
`ParallelAlign` .86 + long branches + SHORT TRUNK; and strands must be DENSER + SHORTER + WIDER (16-22,
LeafSize 3.4, width .11×len), not longer, or they read as grass blades on bare sticks.

**Palm fronds ARCH, not sag** (Bryan's Synty palm ref): a frond leaves the crown going UP+out, apexes, then bends
over and droops at the tip. Ours launched near-horizontal off the sprout dir and sagged immediately = limp fan.
`AddFrond`: start dir `+ up*0.95`, per-segment bend .22→.42, segs 5→7 (fewer reads as a bent stick), width falloff
squared so it holds width through the arc then points.

**DEAD TREES = a modifier on ANY species, not a species (2026-08-15, Bryan asked).** `TreeDef.Dead` → generator
skips all leaf tiers + gnarls branches (curve ×1.5, noise ×2.2, len ×0.72); `TreeDefLibrary.AsDead/DeadSpecies`
greys bark + 0.8× height. A dead oak keeps the oak silhouette. `TreeInjection` applies it when the prototype name
contains "Dead" — **fixed a live regression**: the 4 `* Dead Tree` prototypes (Desert/Scrub/Tundra/IceBog) were
being replaced by LEAFY generated trees. Dead trees emit a bark part ONLY (empty foliage part skipped — it would
cost a draw band + skew impostor bounds). **Limitation: `Conifer` has no branch tiers** (foliage = parametric
ConiferCone), so a dead conifer is a bare tapered spike with no limbs; nothing maps to it today.
**Young-tree bug found + fixed same round:** age thinning + `RoundToInt` drove tier counts to 0 → **25/60 pine
saplings had the crown off the trunk axis and 4/60 had NO foliage at all** (bare stick). Guard: a leaf tier, or
any tier growing off the trunk, never rounds below 1. Now 0/60 both. Measure lopsidedness as
`|foliageBounds.center.xz - trunkTip.xz| / crownRadius`.

**BIRCH is real now + BARK CAN BE TEXTURED (2026-08-15, Bryan asked for white spotted trunks).** Three gaps:
(1) **routing** — `HasTree` is BIOME-only and no biome maps to Birch, so birch prototypes (Forest) generated
Broadleaf and the def was unreachable; `TreeInjection` now name-matches "birch" first, then biome. Kept
birch-ONLY on purpose: a general name matcher would mis-route "Snow Pine" (Snow→Conifer, intentional).
(2) **UV seam bug** — `TreeTubeMesher` emitted `sides` verts/ring and wrapped the last quad's U from
`(sides-1)/sides` back to 0, squeezing the WHOLE texture backwards into one facet. Invisible with flat-colour
bark, fatal for any bark texture. Now `sides+1` verts, last duplicates first at U=1. **V is cumulative length in
METRES** → bark tiles per metre at any tree size (nice property, keep it).
(3) **procedural birch bark** — `TreeInjection.BirchBark()` builds a 64×128 wrapped texture (white grain + ~26
dark tapered lenticel dashes) into `_BaseMap`, birch material only. `Scatter/VertexColorLit` DOES have a
`_BaseMap` (albedo = vtxColor × baseMap × tint) — so any species can get textured bark this way now. Reads as
banding at distance, not distinct spots; more/smaller dashes would sharpen it. Birch crown also opened
(W/H .21→.32) — was a stripe of leaves on one side of a pole.

**PLAY-VERIFIED ON THE PLANET 2026-08-15** (seed 1691104419) — the check the whole session was waiting on.
Injection live: 109 protos / 54 chop trees / 12 bark-only dead. **Per-instance variety WORKS in-world**: within
80 m, Palm 6/11/11, Taiga Pine 17/4/20, Birch 22/13/17 — variants interleave in one stand. **Density is FINE, the
canopy-jam worry did NOT materialise**: Forest = 194 trees in 80 m, dense but walkable → **do NOT change scatter
`SpacingMeters`**, keep the density parity. Cold fill completes (16,931 tiles, 159,456 instances, 0 queued).
Perf (editor): ~18-20 ms at ground, ~31 ms camera raised 46 m over canopy — 109 protos didn't blow it up
(off-biome protos have no instances so cost nothing). Distant treeline/impostors read correctly.
**Driving the game from MCP: see [[reference_unity_mcp]]** — console commands, screenshots, the compile loop,
and the auto-refresh trap all live there now.

**SPECIES SET COMPLETE (12) except exotics — 2026-08-15.** Added Cypress, Cedar, Poplar, **Fern**.
`TreeDef.Cone{BaseFrac,RadiusFrac,Droop,Tiers}` makes ONE cone mesh cover the conifer family: fir = defaults,
Cypress = radius .10 (W/H .17 pillar), Cedar = radius .38 + droop .12 + **only 5 tiers** (tier COUNT is what
separates cedar from fir — at 7+ the plates overlap back into a smooth cone). Poplar = many short primaries at
`ParallelAlign` .22 sweeping up = narrow deciduous column (W/H .14). **Fern = first non-tree plant**, new
**blade primitive `LeafGroup 4`** (= palm frond but UV u spans the WHOLE texture, not a palm-atlas cell, so it
wears the ordinary biome leaf material — reusable for reeds/blades). **Gotcha: `MaxHeight` normalises the STEM**
(the solve measures branch tips; a fern has none) so fronds arch ABOVE it — set it ~0.6× intended plant height
(0.72 → a 1.2 m fern). `tree.gallery` rebuilt: 12 species × 5 cols (4 ages + DEAD), 26×34 m spacing, **2 m
capsule beside every tree**.
**REGRESSION worth remembering:** making dead trees emit a bark part ONLY broke every consumer assuming a tree
prototype has >=2 parts. `TreeShowcaseSpawner` needed `Parts.Length >= 2` to pick materials, so for the four
"* Dead Tree" prototypes both bark+foliage came back null and `AddMesh` SILENTLY skipped — the whole Shrub row
(Desert/Tundra map to Shrub, both dead-tree prototypes) rendered nothing but capsules. Swept the codebase after:
`TreeShowcaseSpawner` was the only place. Verify galleries by COUNTING cells with geometry, not by eyeballing.

**CAPTURE GOTCHA** (also in [[reference_unity_mcp]]): scatter materials carry `_FadeStart` 120 / `_FadeEnd` 150
dither fade — render a gallery from >150 m and **every tree vanishes, leaving only shadows and labels**. Copy the
material (never mutate the shared asset) and push the fade to ~5000 before wide shots.

**BIOMES NOW MAP TO A SPECIES SET (2026-08-16, commit `658a587`).** One species per biome made every tropical
region a palm monoculture ("palm trees everywhere") and left Cypress/Cedar/Poplar generating but placed nowhere.
`TreeDefLibrary.SpeciesSet(biome)` returns 1-4 species; the pick is by the prototype's **ORDINAL within its
biome**, NOT a name hash — hashing was tried and silently DROPPED species, because with one or two tree
prototypes per biome it can miss the primary entirely (Swamp drew Broadleaf over Willow, Savanna drew Shrub over
Acacia). Ordinal 0 always gets the set's first entry, so every biome keeps what it had. Species shipping: 6 → 9
of 12. **Cypress + Cedar still unplaced** (Steppe/Mountain/Taiga/Snow have ONE tree prototype each, so only
ordinal 0 is ever drawn); Fern needs injection to handle Collect-interaction prototypes.
**DETERMINISM BUG FIXED (was mine):** variant seeds used `HashCode.Combine`, and .NET randomises string hashing
**per process** — the same world seed grew different trees every run, breaking saved worlds and blocking impostor
disk caching. Now an explicit FNV-1a over name+variant.

**ALL 11 TREE SPECIES + FERNS NOW SHIP (2026-08-16, commit `fbc0cb8`).** Cypress/Cedar were unplaced because
their biomes have ONE tree prototype each, so the ordinal pick never left the set's first entry. Fix: the K
variants walk the set (`ordinal + variant`) — variant 0 keeps the biome primary, alternates ride the variants, so
a stand mixes SPECIES as well as ages. **This forced `ImpostorShareKey` from prototype → SPECIES** (a
prototype-keyed atlas would billboard a cedar as a fir); that is also CHEAPER — distinct bakes 18 → 15, since
every Broadleaf shares one card. **Ferns ship**: gate is name + NOT-Chop (they are interaction `None`, not
`Collect` — the original guess was wrong), stem + frond parts, no cut-set, no variants. **Fern material trap:**
Synty ferns wear `Leaf_Palm_01`, a 3-CELL frond atlas, while the blade primitive maps UV 0..1 across the WHOLE
texture → every frond would show all three cells squashed. They wear a tinted single-leaf texture instead.
**Still unverified in-world** — planet generation stalled 3× after "Scene services initialized" (needs a Unity
restart), so all of the above is library-level verified only.

**How foliage works now (key facts):**
- Foliage = **textured leaf cards** (crossed quads, UV 0..1, vtx.B=1 leaf mask, vtx.G=AO) drawn with **each biome's
  own Synty FoliageLit leaf material** (`TreeInjection.PickFoliageMaterial`: prefer "...Canopy", else first
  non-"beard" `Scatter/FoliageLit` part). So each biome auto-gets its real leaf texture.
- **Palette-atlas trap:** pine's Synty material = `Generic_01_A` (a color PALETTE, not a leaf image) → our [0,1]
  cards sampled the rainbow strip. `IsPaletteAtlas` detects "Generic*" and substitutes a clean tinted leaf.
- **Palm fronds** = `LeafGroup 1` drooping ribbons, UV-mapped to ONE cell of the 3-frond `Leaf_Palm_01` atlas
  (mostly green, ~12% dead-brown). Atlas layout found by decoding the TGA (scratchpad `tga2png.py`, `py` has no PIL).
- **Fir/Spruce** (`Conifer`, `FoliageStyle.ConiferCone`) = solid low-poly cone, zigzag spike rim, random per-tier
  roll, AO baked in vtx.G, rendered on **FoliageLit with `_ForceLeaf`** + a runtime **solid-green `_BaseMap`**
  (FoliageLit has NO `_BaseColor` → white without the tex). Trunk tapers to spire (`TrunkTipScale`); foliage
  starts at 0.28h so lower trunk shows.
- **Pine** = `NeedleFoliage` + `LeafGroup 2` needle-tuft primitive (small spiky puff per branch tip); tall bare
  trunk, sparse upper branches. Uses the same solid-green needle material.
- **Shaders:** `Scatter/VertexColorLit` = `_BaseColor` tint, IGNORES vtx color for albedo. `Scatter/FoliageLit` =
  colors from `_BaseMap` (no `_BaseColor`), reads vtx.G AO + vtx.B leaf mask, `Cull Off`, alpha-clips `_BaseMap.a`.
- **Injection default-ON**, per-biome species from `TreeDefLibrary.HasTree(biome)`; runtime toggle re-registers the
  DTO (`SettingsProvider.Update`) so `generate` picks it up. Scatter re-fetches at `ScatterRenderer.Configure`.
- **7 species so far:** Broadleaf, Conifer(fir), **Pine**, Birch, Palm, Acacia, Shrub. `tree.gallery` shows all;
  `tree.species <name>` + `tree.gen`. Biome map: Forest/Grassland/Swamp→Broadleaf, Taiga/Snow→Fir,
  Steppe/Mountain→Pine, Beach/Tropical→Palm, Savanna/Scrub→Acacia, Desert/Tundra→Shrub.
- **Commits:** `50a7e39` species+gallery, `38f0ae5` injection+shader-fix, `00b6cdb` foliage arc, `35aedd1`
  Pine+showcase+palette-fix+sizes, `fdbc76c` Willow+polish. **8 species** now: Broadleaf, Fir(Conifer), Pine, Birch,
  Palm, Acacia, Shrub, Willow(weeping strands, LeafGroup 3). Foliage primitives: clump(0)/frond(1)/needle-tuft(2)/
  weeping(3)/ConiferCone. `TreeShowcaseSpawner` (Assets/Scripts/Showcase, in Sampling.csproj) = per-biome old-vs-new
  review scene w/ 2m capsule; drive it via MCP (see below).
- **WIND on generated foliage (verified 2026-08-15).** `FoliageLit.ApplyWind` needs THREE things or it silently
  does nothing: global wind (`WeatherManager` publishes `_WindDirection`/`_WindStrength01`/`_WindSpeedMps` — the
  TreeShowcase scene sets strength **0**, so showcase screenshots prove nothing about wind), vertex-colour **B**
  as leaf mask (our cards write B=1 ✓), and **per-MATERIAL `_WindStrength`, which defaults to 0**. Species reusing
  the authored `Assets/Art/Materials/Foliage*.mat` inherit 0.08–0.20 and sway. **Materials built in CODE are rigid
  unless you set it** — `TreeInjection.ConiferMat` + `TreePreview.ConiferPreviewMat` set `_ForceLeaf` but not
  `_WindStrength`, so Conifer AND Pine were completely rigid; now 0.10. `_ForceLeaf` alone buys nothing:
  `flex = leafMask * _WindStrength`. **Open:** `_WindStrength` is absolute sway METRES tuned for the old
  half-size trees, so 0.14 m on a 34 m oak reads ~half as strongly as it used to — may want it height-scaled.
- **EDITOR + MCP mechanics moved to [[reference_unity_mcp]]** (auto-refresh is OFF, HotReload patches method
  bodies only, verify the assembly timestamp, console + screenshot recipes). Tree-specific corollary:
  `TreeInjection`'s static material caches (`_mats`, `_coniferMats`) survive a HotReload patch, so any change to
  a generated material's properties needs a real domain reload before it shows up.
- **Per-instance variety: BUILT 2026-08-15 (Approach B), editor-verified, NOT yet play-verified in a real biome.**
  The slot RISK is **answered: there is NO slot→biome table** — `SlotId` only feeds the placement hash
  (`ScatterHash.Slot`) + the `ScatterId` pack; biome/spacing come from the prototype's own fields, so new slots
  auto-place. (The old `PackUnchecked` SlotBits=6 aliasing bug is already fixed — it derives from `ScatterId`.)
  `TreeInjection.Variants` (default **3**, console `tree.variants 1-8`) expands each of the 18 Chop trees into K
  prototypes: own seed, age laddered 0.25→1.0, slot from a free cursor at 73 (→108 of 127), `Spacing *= √K`,
  `ScaleRange` narrowed to 0.85–1.2. Variants are **appended after** the originals (index alignment matters —
  `TreeShowcaseSpawner` pairs raw↔injected by index) and **variant 0 keeps the source SlotId** so saved chops bind.
  **Impostor trap solved:** K× prototypes = K× live octa bakes (**841 ms + two 1024² textures each**, measured);
  new `ScatterPrototypeDto.ImpostorShareKey` makes a species' variants share one atlas in `ScatterImpostorFactory`
  (`FromPrebaked` re-frames it to each variant's bounds → no far-LOD size pop). Measured: 841 ms then **0 ms, 0 ms**,
  texture count +2 not +6. Editor-verified: 73→109 protos in 62 ms, EnsureValid OK, density **100%** of Synty per
  source tree, heights ladder e.g. Meadow 6.6/10.2/14.4 m. **Owed:** play-verify a mixed stand in a real biome +
  `scatter.stats` + `ScatterFlyBench`. Fallback if placement ever misbehaves = Approach A (variant filter in the
  ScatterCull compute).

---
### Original overnight note (2026-08-12, superseded above but kept for history):

2026-08-12 (branch `harvest-vertical-slice`, autonomous overnight, **compile-only — NO editor,
nothing visually verified; visuals are guesses Bryan tunes on wake**).

**Why:** the harvest fall/log looked wrong because Synty tree FBX meshes are OPAQUE — can't
identify trunk vs foliage, foliage cards splay when the tree lies horizontal, no clean cut point.
Decision (Bryan): REPLACE all scatter trees with our own procedurally-generated trees we control
fully. Use **Broccoli** (`D:\Unity\Explore Assets\Assets\Waldemarst\Broccoli`) as a CODE
REFERENCE only — study it, write our own, do NOT install the tool.

**Design doc:** `plans/006-procedural-tree-generator.md` (full: 5-stage pipeline, adopt-vs-skip
from Broccoli, ages, per-biome variety, Synty-faceted+toon style, Burst/async perf, Valheim-chop
design stub, overnight-status section). Broccoli study done via 4 parallel agents.

**Broccoli adopt:** recursive per-level rules (~8 params: frequency, golden-angle twirl 137.5°,
range band, parallelAlign, gravityAlign base→top, length falloff, girth); Branch(centerline +
isTrunk) / Sprout(dual anchor) skeleton; skeleton-then-mesh split; generalized-cylinder tubes with
CONSTANT ring N (dodges variable-ring bridging); crossed card leaves welded into one mesh.
**Skip:** node-graph engine, Element⇄Component⇄Manager + TreeFactory MB, atlas/billboard bakers
(we have impostors), SpeedTree wind, roots, procedural textures.

**Built (all `Assets/Scripts/Planet/Trees/`, static classes, no editor deps):**
- `TreeDef.cs` + `TreeDefLibrary.SampleBroadleaf(age)` — the definition (only ONE species so far).
- `TreeStructureGenerator.Generate(def,seed)` → `TreeSkeleton` (branches+sprouts); age scales
  height/girth/frequency; child dir = Slerp(parentFwd, twirled-azimuth, ParallelAlign) + gravity.
- `TreeTubeMesher.Build(sk, sidesDelta)` — bark tube; `BuildCappedTube` for cut pieces.
- `TreeLeafMesher.Build(sk, scale, skip)` — crossed double-sided toon cards per sprout.
- `TreeCutSet.Carve(sk, chopFraction, out stump, out log)` — splits the TRUNK centerline → clean
  capped stump + log (foliage excluded by construction; this is the whole point vs Synty).
- `TreeGenerator.Generate` → `GeneratedTree` bundle {BarkLods[2], FoliageLods[2], Stump, Log,
  Height, ChopHp, WoodYield, ...}.
- `TreePreview.cs` `[CommandPrefix("tree")]`: `tree.gen [seed]`, `tree.age <0-1>`,
  `tree.inject <on/off>`. Registered by Planet in EnsureRuntimeOwners.
- `TreeInjection.cs` — **DEFAULT OFF**. `Apply(ScatterLibraryDto)` swaps Chop-interaction tree
  prototypes for generated trees (bark+foliage parts + LODs) + generated stump, clears Synty
  impostor, ages varied per tree TYPE (seeded by name). Heavily guarded (per-tree try/catch keeps
  Synty; top-level catch returns lib unchanged). Wired: `Planet.RegisterWorldSettings` →
  `settings.Register(TreeInjection.Apply(ScatterLibraryDto.From(scatterLib)))`.

**Commits:** `d3ea619` (T1 skeleton+tube), `87b76ab` (T2 leaves+cut-set+bundle), `17ae354`
(LODs+injection). New `.cs` have NO `.meta` yet (unattended) — generate on Bryan's first import.

**T1 was play-verified by Bryan earlier** ("seemed to work fine, good job") — the rest (leaves,
cut-set, LOD, injection) is compile-only.

**REVIEW ON WAKE (order):** `tree.gen` to eyeball shape/leaves/color; then `tree.inject on` +
`generate` to see the world swapped. Expect to tune: scale vs old Synty, leaf-card blob look,
bark/leaf colors (flat PropLit guesses `(.35,.24,.14)` / `(.24,.44,.16)`), faceted-vs-smooth
(currently smooth; Synty wants faceted), branch angles/counts in `SampleBroadleaf`, LOD distances.

**NOT done:** per-INSTANCE age variety (needs per-instance scatter mesh variants — currently age
is per tree TYPE); fallen LOG still renders LogRenderer's placeholder cylinder (only the STUMP is
wired to the generated cut-set via injected `StumpMesh`); only `SampleBroadleaf` species (per-biome
type set is next); Burst/Awaitable migration deferred (T7); **Valheim chopping** (fell hinged at
chop point + branch/leaf fade + trunk→carryable log sections + a `CarryController`) = design stub
in plan 006 §"Valheim chopping", NOT built — it's a real new gameplay system, build in play.

Related: [[project_gameplay_roadmap]] (harvest POC this replaces the trees for),
[[project_game_vision]] (harvest pillar), [[project_scatter_lod_impostor]] (impostor tier the
generated LOD0 re-bakes into), [[project_surface_props_lighting]] (Planet/PropLit prop shader).
