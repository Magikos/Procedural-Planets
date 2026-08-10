# ProceduralPlanets — "What's Next" Roadmap

_Advisory roadmap, evidence-grounded. Branch reality: work sits on `scatter-placement`/`character-controller-mvp` off `main` @ c54fc72, HEAD at `ddbd42c`. `MEMORY.md` still names `code-refactor` as active — that is stale and should be corrected._

> Provenance: generated 2026-08-09 by a 6-agent read-only survey (recon + look/systems/code-health/perf lenses + synthesis). Directions and tradeoffs are the deliverable; specific `file:line` refs are leads — spot-check before acting on any one of them.

## 1. Current state

The planet **renderer is mature and effectively feature-complete**: biomes (14 land, interlock-blend + D2 albedo tint just landed at `ddbd42c`), grass (GPU compute blanket), scatter/foliage (42 prototypes, GPU-indirect draw), water/ocean, clouds/weather, atmosphere, and a fresh PropLit night-side shader are all shipped. The last three arcs — look/lighting polish, the character-controller MVP, and biome-border seamlessness — were refinement and a first thin gameplay crust on top of that renderer, not new pillars. The gameplay layer is exactly **one agency loop** (walk the sphere) that is fully coded but **never felt by a human**, and every verb past walking is either designed-but-unbuilt (collision) or missing (dig, build, interact, swim, NPCs). The recurring root cause threaded through look, collision, and grounding is the **analytic surface vs. visible-mesh chord error** (`docs/design/2026-08-09-surface-unification.md`), decided as "per-consumer authority + error budget," not a unification service.

## 2. Maturity snapshot

| System | State | Evidence / note |
|---|---|---|
| Terrain / chunks | Shipped | `Planet/Surface/` quadtree + chunk mesh cache; reads flat under light (see plan 002) |
| Biomes | Shipped | `Planet/Biomes/`; interlock-blend + D2 tint landed `ddbd42c`; height-blend (G) reverted as no-op |
| Grass | Shipped | GPU compute blanket + far-field overlay; edge-line saturation still a live knob |
| Scatter / foliage | Shipped (fresh) | `Planet/Scatter/` ~28 files; GPU-indirect rewrite default-ON; some concentrated debt |
| Water / ocean | Shipped | Ocean/WaterVolume render features; **caustics = hard don't-touch** |
| Clouds / weather | Shipped, "parked needing polish" | Volumetric raymarch at **full res, no downsample** — largest untouched GPU cost |
| Lighting / atmosphere | Shipped | Atmosphere passes + PropLit night-side shader (`db2a66b`) |
| GPU-indirect rendering | Shipped | `ScatterGpuDraw` RenderMeshIndirect; big perf win banked |
| Character controller | **Partial** | `Planet/Character/`; 78/78 EditMode pass, programmatic walk verified; **feel/camera/input never human-verified** (`plans/BUILD-STATUS.md`) |
| Collision | **Stubbed** | Analytic + visible-mesh raycast only; **zero MeshCollider/BakeMesh/Rigidbody** in `Assets/Scripts`; design pinned in `docs/design/2026-08-09-collision-strategy.md` |
| Save / load | **Partial** | Only `SurfaceEditStamp` (versioned, seed-keyed, `SurfaceEditController.cs:466`) + camera store; no character/inventory/world state |
| Digging / caves | **Missing** | No voxel/SDF/excavate code; designed as a future SDF/marching-cubes Phase 9 byproduct |
| Building / interaction / NPCs | **Missing** | No place/pickup/inventory/behaviour code anywhere |

## 3. The three candidate arcs

### Arc A — LOOK / CONTENT polish

**The case.** Every visual subsystem is shipped, so this arc is refinement and content breadth, not new systems — but there are real, diagnosed levers left, and one of them (terrain relief) lifts the perceived quality of *the entire planet* at once. Highest-confidence arc: the changes are per-material or single-shader, hot-reloadable, and judged by F10 A/B.

**Top moves.**
1. **Run plan 002 — widen the terrain diffuse-response curve.** Terrain reads flat despite a proven normal/ARM pipeline because `dayLight = lerp(0.34, 1.08, terrainDiffuse)` (`Assets/Graphics/Shaders/PlanetVertexColor.shader`) compresses normal variation into a narrow band. Diagnosis is already written (`plans/002-terrain-relief-experiment.md`, `project_normal_mapping_flat.md`); it has never been executed. This is the single biggest look lever and it unblocks judging every other tweak against terrain that has form.
2. **Bring foliage to life — wind sway + interactor push.** The scene is motionless: every foliage material ships `_WindStrength:0` and the grass-interactor push hook is fully **inert** because no material sets `_InteractiveBend` (`project_planet_look_dev.md:77-88`, commit `2f099c6`). Motion is the cheapest aliveness lever for a Synty look — S effort, per-material, hot-reloadable.
3. **Resolve the grass-overlay biome-edge line.** Long-running bright-green-over-tan pop; a live knob (`grass.surface-saturation`, default 0.72) exists but no baked value. Structural fix: fade overlay saturation by biome-blend proximity so interiors stay lush but borders don't pop (`plans/look-fixes-backlog.md #3`, commit `0e9be87`). Prior greenness-gate attempt starved savanna and was reverted — don't reintroduce that.

**Evidence.** `plans/002-*`, `PlanetVertexColor.shader`, `project_planet_look_dev.md`, `plans/look-fixes-backlog.md`.
**Effort.** ~1–2 weeks for the three high-value moves (excludes L-effort content/POI buildout).
**Risk of NOT doing it.** The planet keeps reading flat and static; every future gameplay screenshot/trailer sits on top of terrain that doesn't show its own form, and the "a place worth exploring" third-person vision stays undercut by uniform, motionless fields.

### Arc B — GAMEPLAY / SYSTEMS frontier

**The case.** This is the only arc that changes **what the project is** — from "a planet you look at" to "a planet you play in." The character MVP architecture is genuinely good (pure testable `CharacterMotor` + `IGravityProvider`/`IGroundingProvider` seams, flat-world-portable, 15/78 tests), and the collision keystone is **fully designed** in a decision-of-record. The frontier is unusually de-risked for how much it unlocks.

**Top moves.**
1. **Human-verify + tune the character MVP.** The only agency loop is coded but never felt — WASD/mouse-look/jump/crouch/sprint/third-person-follow ran only via injected input, and every constant (WalkSpeed=5, CamDistance=5.5, JumpHeight=1.6, LookSensitivity=0.12) is an admitted guess (`plans/BUILD-STATUS.md` "What only YOU can verify"). Cheapest unblock of product confidence; gates honest work on every downstream feel decision. **Needs Bryan at the keyboard — not automatable.**
2. **Streamed per-chunk MeshCollider "physics bubble" (the keystone).** Zero physics code exists (`grep`: no MeshCollider/BakeMesh/Rigidbody). Reuse each near-player chunk's render mesh, cook once via async `Physics.BakeMesh` on a Burst job, cache by chunk+geometry-version, evict when far — the design is fully pinned (`docs/design/2026-08-09-collision-strategy.md`). **Every** dynamic verb (thrown rock, felled tree, ragdoll, dropped loot, physics-built structure, dig re-cook) is retro-blocked on this.
3. **First interaction verb — instanced-to-Rigidbody handoff.** Aim at a scatter prop, press interact, swap the GPU-instanced billboard for a real Rigidbody that falls/rolls/rests against the bubble (`collision-strategy.md` use-case matrix; hand off from `ScatterGpuDraw.cs`). Turns "walk around" into "do something" and exercises the collision spine end-to-end.

**Evidence.** `plans/BUILD-STATUS.md`, `docs/design/2026-08-09-collision-strategy.md`, `docs/design/2026-08-09-surface-unification.md`, `Planet/Character/`, `ScatterGpuDraw.cs`.
**Effort.** Verify = S (a session with Bryan). Collision spine = M. Interaction verb = M. ~3–5 weeks to a first dynamic-object loop.
**Risk of NOT doing it.** The project stays "a pretty sphere you can't affect." The character MVP's tuning bit-rots unvalidated; the fully-designed collision spine ages while its two enabling docs drift; and the surface-drift chord error keeps threatening to make any future body rest visibly off the ground because nothing forced collision + grounding onto one reference surface.

### Arc C — CODE-HEALTH consolidation of the fresh scatter/render code

**The case.** The scatter/GPU-indirect code is **mostly well-engineered** (Awaitable-only exact, ShaderGlobalIds discipline perfect, DTO discipline clean, swap-remove well-tested) — so this is targeted debt paydown, not a rescue. Its value is *timing*: the code is fresh in memory now, and there is a real latent bug class.

**Top moves.**
1. **Collapse the triplicated gather math to one source of truth.** The biome-memo key (bit-packed face/sampleLevel/xb/yb) is copied verbatim in `ScatterField.cs:276`, `ScatterGatherJob.cs:113`, and `ScatterBiomePrecompute.cs:46`, and `BiomeSampleLevel=9` is redeclared in all three. A one-line edit in one file silently diverges GPU placement from the managed reference, caught only by the parity test. One shared helper + one const kills the whole bug class.
2. **Split the two files that breach the 400-line guardrail.** Extract `ScatterField.cs`'s ~285 lines of console diagnostics (lines 413-697, six commands) into a same-assembly companion — CLAUDE.md sanctions this once the command set outgrows the service — leaving a ~400-line placement authority. Carve `ScatterTileCache.cs`'s 90-line Burst orchestration + native-buffer lifecycle (`:329-420`) into a `ScatterBurstGatherRunner`, isolating the trickiest lifetime code.
3. **Add EditMode coverage for the tile-cache planning core** (currently zero direct coverage — the most stateful class), enabled by move 2's extraction. Plus resolve the two flagged small items: `scatter.lodview` is **dead under the default GPU draw path** (`ScatterLodBatcher.cs:22-98` only), and `ScatterRenderer.cs:65-66` writes `enableInstancing` onto the SO-referenced material (audit N2 — Bryan-decides).

**Evidence.** `ScatterField.cs`, `ScatterGatherJob.cs`, `ScatterBiomePrecompute.cs`, `ScatterTileCache.cs`, `docs/audit/2026-07-26-scatter-audit.md`.
**Effort.** ~1 week; each item is a mechanical, test-guarded extraction.
**Risk of NOT doing it.** The triplicated key drifts and silently desyncs GPU vs. managed placement — a class of bug the parity test catches only if someone runs it. The two oversized files keep accreting responsibility (CLAUDE.md: "when you're about to add a responsibility, split first"), and the window where this code is cheap to touch closes as memory of it fades.

## 4. Recommended next arc

**Recommendation: Arc B (Gameplay / Systems frontier), sequenced and gated — with two cheap Arc-A/perf wins run in parallel.**

**Rationale.** Look and code-health both refine *what already exists*; only the gameplay frontier changes *what the project is*, and it is far more de-risked than a "start a new pillar" arc usually is — the character architecture is clean and tested, and the keystone is a fully-pinned decision-of-record awaiting nothing but implementation. The one live liability in the whole codebase is that the sole agency loop has **never been felt by a human**; that must be retired before any downstream feel work is honest. It's also the cheapest possible move (one session with Bryan). Look polish is the strongest *fallback* if the near-term goal is a beautiful demo rather than a game — but it produces a prettier version of the same non-interactive planet.

**Dependency note (what must precede what):**

- **Human-verify the character MVP first (gate).** It defines the fixed-LOD reference surface that grounding uses, and collision must bind to the *same* surface or bodies rest off the ground (surface-drift root cause). Nothing else in Arc B is honest until locomotion feel is confirmed.
- **Then the collision spine** (`collision-strategy.md`) — it retro-blocks every dynamic verb, so it comes before interaction, save-generalization, digging, and ocean swimming.
- **Then the first interaction verb** (instanced→Rigidbody), which exercises the spine end-to-end.
- **Save-spine generalization** waits for at least one persistent verb to justify its scope (avoid building persistence infra before the verbs exist — YAGNI).
- **Digging/caves (Phase 9)** and **ocean swimming** are *later*: digging shares the spine's fixed-LOD reference and re-cook path (collision as a meshing byproduct); swimming slots into the existing `IGroundingProvider` seam but needs the same surface-authority discipline.
- **Run in parallel (independent, low-risk):** foliage wind sway (Arc A, S) and half-res cloud raymarch (perf, M) — neither touches character/collision code, both bank quality/perf wins while Bryan's keyboard time is the bottleneck.
- **Do opportunistically, not as its own arc:** Arc C move 1 (collapse the triplicated gather math) — it is a genuine latent bug and cheap; fold it in when the scatter files are next touched rather than spending a full arc on consolidation now.

## 5. Ranked backlog

Impact/Effort use High/Med/Low. (Lens ratings mapped: L→High, M→Med, S→Low.)

| # | Item | Arc | Impact | Effort | Evidence |
|---|---|---|---|---|---|
| 1 | Human-verify + tune character MVP (feel/camera/input) | Systems | Med | Low | `plans/BUILD-STATUS.md`; `PlanetCharacterController.cs` |
| 2 | Enable foliage wind sway + wire interactor push | Look | Med | Low | `project_planet_look_dev.md:77-88`; commit `2f099c6` |
| 3 | Half-res cloud raymarch + bilateral upsample | Perf | High | Med | `CloudRenderFeature.cs:150-157`; `Cloud.shader:345`; `CloudSettings.cs:29` |
| 4 | Streamed per-chunk MeshCollider physics bubble (keystone) | Systems | High | Med | `docs/design/2026-08-09-collision-strategy.md`; surface-unification doc |
| 5 | Collapse triplicated gather math to one helper + const | Code-health | Med | Med | `ScatterField.cs:276`; `ScatterGatherJob.cs:113`; `ScatterBiomePrecompute.cs:46` |
| 6 | Run plan 002 — widen terrain diffuse-response curve | Look | High | Med | `plans/002-terrain-relief-experiment.md`; `PlanetVertexColor.shader` |
| 7 | First interaction verb (instanced→Rigidbody handoff) | Systems | Med | Med | `collision-strategy.md`; `ScatterGpuDraw.cs` |
| 8 | Resolve grass-overlay biome-edge line (proximity fade) | Look | Med | Low | `plans/look-fixes-backlog.md #3`; commit `0e9be87` |
| 9 | Extract ScatterField console diagnostics (700→~400) | Code-health | Med | Med | `ScatterField.cs:413-697` |
| 10 | Summed-area table for O(r²) biome bake | Perf | Med | Med | `BiomeMapBaker.cs:29,157-168`; biome-blend doc:240 |
| 11 | Carve out ScatterBurstGatherRunner (split 483-line file) | Code-health | Med | Med | `ScatterTileCache.cs:329-420` |
| 12 | Fix Mountain steep-slope blue-white snow/rock read | Look | Med | Low | `PlanetVertexColor.shader:24-31,754-759`; commit `a41d196` |
| 13 | Author height into ARM-alpha + accept G height-blend | Look | Med | Med | `docs/design/2026-08-09-biome-blend-seamlessness.md`; `BiomeSurfaceTextureArrays.cs` |
| 14 | EditMode coverage for tile-cache planning core | Code-health | Med | Med | no `ScatterTileCacheTests.cs`; `PackTile/UnpackTile:454-460` |
| 15 | Generalize save spine (world + character + placed objects) | Systems | Med | Med | `SurfaceEditController.cs:466` |
| 16 | Consolidate scatter cull to one dispatch per prototype | Perf | Low | Med | `ScatterGpuDraw.cs:165-186`; `ScatterCull.compute:20-38` |
| 17 | Fold/half-res the god-ray streak pass | Perf | Low | Low | `CloudRenderFeature.cs:185-250` |
| 18 | Amortize grass redispatch (frontier update vs. all-chunks/25m) | Perf | Low | Med | `GrassPlacementController.cs:125-131`; `GrassChunkRuntime.cs:96,108` |
| 19 | Fix scatter.lodview dead under GPU path; audit N2 material write | Code-health | Low | Low | `ScatterLodBatcher.cs:22-98`; `ScatterRenderer.cs:65-66` |
| 20 | Ocean traversal — swimming + buoyancy grounding provider | Systems | Med | Med | `plans/BUILD-STATUS.md`; `IGroundingProvider.cs` |
| 21 | Content breadth — scale in LMHPOLY catalog + POIs/landmarks | Look | High (long-term) | High | `project_planet_look_dev.md:97-103`; biome `.asset` files |
| 22 | Terrain digging / caves via SDF or marching-cubes (Phase 9) | Systems | High | High | `collision-strategy.md` "SDF alignment"; `PlanetChunkMeshJob` |

## 6. Explicit tradeoffs (so you can choose)

- **Systems vs. Look — the core choice.** Systems changes *what the project is* but is the least visually rewarding per week and carries integration risk (collision hitch, surface drift). Look changes *how good the same non-interactive planet looks*, is higher-confidence and F10-verifiable, but at the end you still have a planet you can't affect. Pick Systems if the goal is "a game"; pick Look if the near-term goal is a beautiful demo/trailer.
- **Code-health timing.** The scatter code is *mostly clean*, so a full consolidation arc is hard to justify as the headline — but the triplicated gather key is a genuine latent bug and the code is cheapest to touch now. Recommendation: harvest move 1 opportunistically rather than spending an arc; if you expect to build heavily on scatter (e.g. dig-driven re-scatter), promote the file splits (moves 2–3) sooner.
- **Perf is not an arc — it's parallel wins.** The famous "12ms scatter" is stale; that arc closed (73→37ms GPU). The one big untouched lever is **clouds** (two full-res fullscreen passes, ~240 samples/pixel, no downsample). Half-res cloud raymarch (#3) is the best perf/effort ratio in the whole backlog and touches nothing on the character/collision path — run it alongside whatever arc you pick.
- **The gate is non-negotiable regardless of arc.** Human-verifying the character MVP (#1) costs one session and de-risks *both* Systems (feel base for every verb) and any future demo (you'll walk it in trailers). Do it first even if you then choose Look.
- **Don't-touch guardrails hold across all arcs:** caustics (`Ocean.shader`) are hard off-limits; visual constants change only through F10 A/B under `pp-change-control`; audit item N2 (#19) is Bryan-decides.
- **Two hygiene chores worth doing now, cheaply, in any arc:** correct `MEMORY.md`'s stale `code-refactor` active-branch claim, and note that CLAUDE.md's "no test framework" rule is unreconciled with the 78 EditMode tests that already exist.
