---
name: project_gameplay_roadmap
description: 2026-08-12 pivot from look-polish to the gameplay frontier — roadmap doc, code-health pass outcome (landed + deferred-with-reasons), harvest-slice plan 003, and a latent ScatterGatherBurst slot-bit bug.
metadata:
  type: project
---

2026-08-12 (branch character-controller-mvp): after the lake/scatter/impostor look-polish arc
+ a narrow code-health pass, Bryan asked "what's next" and (via `/improve`) wanted a grounded
roadmap. Delivered. Three parallel read-only surveys (gameplay inventory, scatter/render
code-health, character/world-feel) grounded it.

**Roadmap doc:** `docs/design/2026-08-12-next-roadmap.md`. Substrate mature; game ~0% built.
Three tracks: **A harvest beachhead** (highest leverage — de-risked: `ScatterInteraction`
Collect/Chop tags + `EntityHarvest` + player raycaster all exist with ZERO runtime reader);
**B character feel** (camera hard-snaps `PlanetCharacterController.cs:133-148`, actor is a
sliding un-animated capsule `:268`, spawn is console-only); **C code-health** (narrow).
Recommendation: B feel-quickwins → then A harvest. Deprioritized: more polish (diminishing),
collider streaming (L, not needed till physics loot), magic/combat/mounts (later pillars).

**Code-health pass — LANDED, uncommitted, builds clean Core→Planet:**
- `ScatterFlyBench.cs` 5× `Debug.Log*` → `LoggerProvider`.
- `GrassNearFieldController.cs`: removed `EnableMultiFaceDispatch` const-true toggle + collapsed
  its 3 dead `!` branches (provably behavior-identical) + pruned SLICE-history header + 2 stale
  change-history comments.

**Code-health — DEFERRED (don't blind-retry; reasons):**
- **ScatterField split** — declined. CLAUDE.md "commands stay on the service"; extracting the 6
  diagnostics widens `GatherCoreSync`/`TryResolveSurfaceAnchor`/`EstimateCandidates`/`_library`/
  `_configured`/radii to internal = net coupling increase to satisfy a line count. File is
  cohesive (placement core + proofs of that core).
- **Grass dedup cluster + god-class split** (survey #1, the big one) — MEDIUM risk on the LIVE
  grass GPU draw; byte-identical copy-paste of climate-map/readback/indirect-draw between
  `GrassNearFieldController` and `GrassChunkRuntime`/`GrassChunkDispatcher`. Must stay
  bit-identical or grass regresses → needs play-mode visual verify. Do as ONE coherent pass in a
  verify-capable session, not piecemeal.
- **`SuppressionRadius` KEPT** — reads dead (fraction=0) but is load-bearing:
  `GrassPlacementController.cs:142-143` gates chunk-grass suppression on it (4-file de-feature).

**POC #1 = "chop one tree" (harvest spine on the capsule):** walk (capsule, console spawn) → look
at tree → Interact → vanishes → +1 item → persists on reload. Deferred: animated model/camera
(plan 004), inventory UI, Collect/Chop split, FX. **Bryan's iteration directive (2026-08-12):** the
code must grow cheaply toward the Valheim chop loop (animations, sounds, per-hit damage w/ axe
quality → N hits, animated topple, tree→logs→wood) — so build SEAMS now, implement trivially,
features additive later. Seams (plan 003 §3b): (1) one verb choke `HarvestService.TryHarvest(id,
proto, tool)`; (2) node takes damage not boolean-remove (HP defaulted to one-shot); (3) `ToolTier`
{Name,Damage}; (4) `HarvestYield` {ItemId,Count} from prototype+ScatterInteraction; (5) EventBus
`HarvestHitEvent`/`ScatterHarvestedEvent` = the anim/sound/vfx hook (POC HUD line is first
subscriber); (6) removal is one call so a topple anim can delay it. Build seams NOT features; mark
deferrals with `ponytail:` comments naming the upgrade path.

**Harvest slice plan:** `plans/003-harvest-vertical-slice.md` (indexed in `plans/README.md`).
**BUILT 2026-08-12 (autonomous, uncommitted, compile-only — tests written-not-run, NOTHING
play-verified):** Stage 0 SlotBits fix (ScatterId bit-counts made public; `ScatterGatherBurst` derives
them; +Burst-parity test; fixed 2 stale ScatterId tests where player bit=63 now). Stage 1 `ScatterDrawBuckets`
retains `_ids` + `Ids(p)` + `RemoveInstanceById` (snapshot-tile+RemoveTile+re-add, correct-by-construction);
`ScatterTileCache.Commit` passes `inst.Id`, +`Ids`/`RemoveInstance`. Stage 2 `ScatterHarvestStore` (seed-keyed
JSON, ulong-as-string) + Commit filter + `Planet.cs` create/Configure(Seed)/SetHarvestStore wiring. Seams:
Core `HarvestTypes.cs` (ToolTier/HarvestYield/HarvestHitEvent/ScatterHarvestedEvent : IGameEvent) +
`InventoryService.cs`; Planet `HarvestService.cs` (delegate-injected orchestrator, one-shot fell) +
`ScatterPicker.cs` (ScatterPickMath ray→nearest). New EditMode tests: HarvestStore round-trip, HarvestService
one-shot, ScatterPickMath geometry. Builds clean Core→Planet→Tests. **REMAINING = Stage 4 only** (Interact
input in InputMapService + PlanetCharacterController glue + DebugOverlayHud subscribe to ScatterHarvestedEvent)
— live-player wiring, needs play. NOTE: new .cs files need a Unity import for .meta; csproj edits were throwaway
(git-ignored, Unity regenerates).
**Stage 4 ALSO BUILT (2026-08-12, same autonomous session, compile-only):** Interact=**F** added to
InputMapService/IInputMapService; PlanetCharacterController Update harvests the camera-ray-aimed instance
(resolve HarvestInteractor per-press via ServiceLocator, reach 10 / perp 1.5); Planet builds+registers a
`HarvestInteractor` (picker+HarvestService wired to _scatterRenderer.Cache/_harvestStore/_inventory/library)
in RegisterWorldServices; DebugOverlayHud subscribes ScatterHarvestedEvent → toast; HarvestInteractor logs
"[Harvest] Chopped Nx Wood". **6 tree prototypes set Interaction:0→2 (Chop)** — Meadow/Autumn Forest/Golden
Forest/Golden Meadow/Autumn Meadow/Birch — so there's harvestable content (needs stop→reimport→re-enter play
since ScatterLibraryDto snapshots at boot). PLAY-VERIFY RISKS: (a) does SettingsProvider.GetSettings resolve in
RegisterWorldServices (else interactor unregistered → F no-op); (b) reach/perp feel; (c) RemoveInstanceById.
NOTHING play-verified. Full POC now in tree, awaiting Bryan's play test.
**BOOT-CRASH FIXED (first play attempt):** eager `SettingsProvider.GetSettings<ScatterLibraryDto>()` in
`Planet.RegisterWorldServices` threw "no DTO registered" → broke SceneBootstrap early-init. **LIFECYCLE FACT
(corrects prior wrong assumption): `RegisterWorldServices` runs BEFORE the world settings/DTOs are registered —
NOT settings-frozen-first.** Fix: `ScatterPicker` + the HarvestService protoInfo delegate now resolve the
library LAZILY via a `System.Func<ScatterLibraryDto>` guarded by `SettingsProvider.IsRegistered<>` (used only
post-generation, when the DTO exists). Builds clean.
**BOOT-CRASH #2 FIXED:** `context.Register(new HarvestInteractor(...))` threw "already registered by
HarvestInteractor." GOTCHA: **`Planet.RegisterWorldServices` is called MORE THAN ONCE**, and
`WorldContext.Register` (ServiceLocator.cs:75-82) throws on re-registering a DIFFERENT instance but is
idempotent for the SAME instance (`!ReferenceEquals(existing, service)`). The existing registrations pass
stable instances (`this`/`_grass`/`_surfaceEdits`); my `new HarvestInteractor` each call was fresh → threw.
Fix: build the interactor ONCE into a `_harvestInteractor` field (deps stable, library lazy) and register that
same instance each call. Harvest block also wrapped in try/catch so a wiring fault can never crash boot again.
Builds clean.
**POC PLAY-VERIFIED 2026-08-12:** `[Harvest] Chopped 3x Wood` — F on a tree fells it, grants wood, logs. First
gameplay loop runs. Runtime-proven: colliderless ray→nearest pick, runtime GPU-instance removal
(RemoveInstanceById), ScatterInteraction reader, ScatterHarvestedEvent + HUD/log. Tuning that got it working:
ALL 18 tree/pine/palm prototypes set Interaction:2 (Chop); pick corridor widened (reach 12, perp 3.5) since a
tree pivot sits at its BASE not the trunk; screen-center crosshair added (aim is camera-forward, NOT mouse);
HarvestInteractor logs every F press (hit/miss/not-harvestable). STILL TO CONFIRM: persistence across
reload/regen (the store write + Commit filter — the chop should stay gone). EditMode suite still written-not-run
by me (Bryan ran the editor; unknown if he ran Test Runner). Big uncommitted worktree (code-health pass + full
harvest POC + roadmap/plans/memory) — Bryan owns commit timing.
Key decisions from a code-path survey: removal = filter harvested `ScatterId`s at
`ScatterTileCache.Commit:422` (sole choke point for BOTH serial + Burst paths; buckets are the
only draw source → auto-fixes the GPU draw, no shader change). Persist = mirror
`SurfaceEditController` (seed-keyed JSON, `Configure(seed)` at `Planet.cs:369` gen-complete
block). Hardest gap = picking which instance the player looks at (no colliders; buckets drop the
Id at `:437` → must retain it + build a ray→nearest-instance query).

**LATENT BUG (harvest Stage 0 prerequisite):** `ScatterGatherBurst.PackUnchecked:179` packs with
`SlotBits=6` vs canonical `ScatterId.cs:9` `SlotBits=7`. The Burst path is the DEFAULT on the
real planet, so lake-shore props at slots 64–67 (see [[project_lake_biome]]) pack aliased ids.
Independent of harvest but harvest is the first feature needing stable per-instance ids. Fix the
Burst packer to the canonical layout (prefer sharing `ScatterId`'s constants).

Related: [[project_lake_biome]] (slots 64-67), [[reference_collision_strategy]] (collider
streaming deferred), [[project_current_focus]] (prior code-refactor arc).
