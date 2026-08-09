---
name: project-character-terrain-plans
description: "2026-08-08: plans/ dir holds the two frontier plans off the roadmap survey — 001 character-controller MVP (walk the sphere), 002 terrain-relief diagnosis. Both REVISED after Codex review, awaiting 2nd opinion + Bryan approval, then execute 002→001."
metadata:
  type: project
---

## 2026-08-08 — first frontier plans written (branch `scatter-placement`, planned at commit `c54fc72`)

After the look/lighting arc + scatter GPU-indirect rewrite landed on main, ran the `improve next`
roadmap survey → picked D1 + D2 to plan. Self-contained plans live in `plans/` (root, the improve-skill
staging area), NOT `docs/design` yet — they promote to `docs/design` (001) / `docs/research` (002 result)
on Bryan's approval.

- **plans/001-character-controller-mvp.md** — P1/M. Spike, toggle-gated. Walk the sphere: radial
  locomotion + ground-snap via `IPlanetSurfaceSampler.TryGetSurfaceRadius` (chunks have NO colliders, so
  NO physics — analytic ground). Grass bend (`IGrassInteractor`, register is STATIC, exemplar
  `DebugGrassInteractor`), 3rd-person follow + free-camera suspend, optional foot-trail. Pure
  `SphereLocomotion` math + EditMode tests.
- **plans/002-terrain-relief-experiment.md** — P1/S. Diagnosis not fix. Why terrain reads flat. Open
  question is H1-weak (source normal amplitude / `0.065` triplanar tiling sub-pixel at altitude) vs
  H2 (lighting curve `dayLight = lerp(0.24,1.12,litDiffuse)…` compression). H1-placeholder/H3 already
  refuted (Editor.log "0 placeholder", failure-archaeology entry 10). Control test = `Material.SetFloat`
  on the ONE shared runtime clone (`_terrainMaterial.Material`) — NOT `SetGlobalFloat` (props are
  `UnityPerMaterial` CBUFFER). Use OBLIQUE sun (`time.set-local 0.35`), NOT noon (sun ∥ geom normal →
  no relief contrast).

### Review loop (see [[feedback-adversarial-review-verification]])
Codex reviewed → I verified all claims with parallel agents (9/10 CONFIRMED). Codex caught real bugs in
my originals: foot-trail `saveStamp:false` (no regrowth), "~90-116 materials"+`SetGlobalFloat` (wrong),
"freeze at noon" (worst relief angle), spawn command can't bootstrap its instance (`MonoTargetType.Single`
needs a live component → use static cmd + `SceneBootstrap.EnsureComponent`). All confirmed corrections
FOLDED INTO THE BODIES 2026-08-08; each plan keeps a "Claude verification" record marked APPLIED.

### Round 2 — Codex re-review verified + folded (2026-08-08)
Codex re-reviewed; I verified again with parallel agents (C12/T9/C13/T10 all CONFIRMED). Real residual
bugs in my own round-1 revision, now fixed: C13 (stale Current-state foot-trail text still said
saveStamp:false/raw radial), T9 (tiling default is 0.055 NOT 0.065, and I named tiling but never tested it
→ added a live Material.SetFloat 0.055→0.0055 A/B), T10 (leftover "frozen noon" in test plan). C12: my
EnsureComponent choice put the controller ON the bootstrap GameObject → moving it moves bootstrap; fix =
inert-but-ACTIVE host owns a separate movable CHILD, and character.spawn is `MonoTargetType.Single` (not
static; host resolves via FindObjectsInactive.Exclude so must stay active). T11: result lives in the
promoted docs/design plan, not a separate docs/research file.

**Motor design (Bryan's call, AskUserQuestion):** chose PLANET-AGNOSTIC PARAMS (option A) over Codex's
full interface injection (C9-C11). Renamed `SphereLocomotion` → `CharacterMotor.Step(up, …)` (returns the
tangent step, no planet center/radius); the controller computes radial `up` + ground-snaps. Gets the
non-spherical testability (constant-up/planar fakes = just args) WITHOUT `IGravityProvider`/`IGrounding`
interfaces (one-impl-each = YAGNI on a one-planet project; trivial later extract).

### Round 3 — Codex re-review + Bryan resolves the motor design (2026-08-09)
Codex round 3: C15 (orient from FINAL grounded frame, not pre-step up — one-frame lean), C16 (grass:
register-once in host OnEnable / Unregister OnDisable / IsActive=spawned — NOT unregister-on-despawn; the
always-active host's OnEnable never re-runs, so unregister breaks 2nd spawn; registry skips inactive + springs
back on active→inactive edge), C17 (CharacterMotor.TryStep degenerate-up contract), C18 (factual fix: FSM
_stateCache is INSTANCE dict not static singletons; stamp State Machine repo main@95b0dfc159254d), T12 (framing
drift), T13 (mode 82 = H2 negative control since it returns before the dayLight edit; 2×2 strength-0 control to
isolate normal-map relief from macro geometric curvature), T14 (archive PNG+sidecar before MaxCaptureRuns=6
prune), T15 (drift-check _BiomeTriplanarTiling from BOTH shader default + Planet.mat). All verified + folded.

**C14 — motor design RESOLVED (Bryan):** Bryan named REAL requirements → gravity on ANY surface (flat test
planes, radial sphere, **flight**, **anti-gravity spells**) + one controller across many actors (birds/bears/
dragons/knights on horseback) + "composable mindset, strong set of tools." That gives the gravity/grounding
seams MULTIPLE real implementations → no longer YAGNI. Design = **HYBRID**: keep option-A's pure
`CharacterMotor.Step(up,…)` (actor-agnostic tangent-step tool) + adopt Codex-C14's injected
`IGravityProvider` (RadialGravityProvider; up = -accel.normalized) + `IGroundingProvider`
(PlanetSurfaceGrounding wraps IPlanetSurfaceSampler), composed at bootstrap. Controller loop asks the
capabilities — NO inline radial/sampler. MVP wires ONLY radial+analytic (+ ConstantGravity/PlanarGrounding
test fakes = the 2nd impl). Flight/anti-grav = new gravity providers; NPC variety + fly/walk/ride behaviors =
the harvested FSM ([[reference-state-machine-project]]) on top — all DEFERRED but proven-composable. This
supersedes the round-2 "option A / planet-agnostic params, no interfaces" disposition.

### Round 4 — Codex converges; composable design completed (2026-08-09)
All verified + folded, no re-litigation. **001:** C19 (THE one — split the actor-agnostic locomotion driver
`SurfaceCharacterController` from the thin player host `PlanetCharacterController`; round-3 hybrid had
gravity/grounding behind interfaces but left the CONTROLLER player-specific → "one controller across many
actors" wasn't delivered; the driver = motor + injected gravity/grounding + Tick→pose, no Unity/input/camera/
grass/console), C20 (contracts: gravity accel m/s² + configurable magnitude; TryGround true=grounded-pose drop
IsGrounded; Normal=radial-up EXPLICIT, sampler has no normal + gradient=slope=out-of-scope), C21 (real boot
NRE: GrassInteractorRegistry.Register snapshots WorldPosition at :87 BEFORE any IsActive gate → child-backed
WorldPosition throws at boot-OnEnable; fix = unspawned fallback + IsActive=>spawned&&child!=null), C22 (build:
.csproj are Unity-generated/git-ignored/EnableDefaultItems=false → pre-import dotnet build FALSELY GREEN; build
ProceduralPlanets.Planet.csproj AFTER Unity import; char files → Planet asmdef). **002:** T16 (weather.freeze
too — cloud shadow advects under sun-only freeze, multiplies into dayColor:1144), T17 (_terrainMaterial private
+ no console path → MCP execute_code helper to find the one "Planet/VertexColor (runtime)" clone; debug.screenshot
takes a label arg → filename), T18 (probeTiling=max(0.001,baseline/10)), T19 (drop dotnet build, shader-only),
T20 (scope verdict to sampled biome + 1 cross-biome confirm).

### Round 5 — verified + folded; design converged (2026-08-09)
**001:** C23 (I over-claimed — `SurfaceCharacterController` always TryGrounds + uses only gravity direction, so
it's a GROUNDED driver; a gravity-provider swap alone can't fly; flight = a separate AIRBORNE driver reusing
CharacterMotor+IGravityProvider, FSM picks between them), C24 (driver needs a finite `CharacterPose` seed +
ResetPose on respawn/regen; TryGetGravity==true ⇒ finite NON-ZERO accel, zero→false; else first-tick from
Vector3.zero = invalid planet-center). **002:** T21 (shipped the actual paste-ready execute_code material
helper — AutoRegister=false/scripting_ext "Scripting Extensions"; find by shader.name + " (runtime)" suffix,
assert count==1), T22 (foliage wind is _Time-driven → moving ShadowCaster shadows hit terrain day path :1132
even with weather frozen → also `weather.wind-speed 0`), T23 (`scatter.count`→DescribeBiomeAt names biome;
`scatter.goto <BiomeType>` reaches one — BiomeMapPrimaryId/sidecar don't name it).

**CONVERGENCE:** 5 rounds, severity strictly decreasing (round1 = broken logic; round5 = wording + init-seed +
missing code snippet + wind confound). Design settled. I recommended Bryan APPROVE rather than loop further —
remaining risk is dry-run-surfaceable, not design debt.

**State:** both REVISED (5 rounds) — recommend approve → promote 001 to docs/design/, execute 002 then 001
serially (one Unity editor; restore play/shader/capture state between). Not executed yet. GOTCHA: DTO/const/
shader changes need clean stop→play, and shader-CODE edits need force-import (don't hot-reload).
