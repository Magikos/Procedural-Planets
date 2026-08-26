# Plan 004 — Character presence & feel (make walking + looking feel good)

**Status:** TODO — ready to execute. **Written against commit `17a8672`.**
**Author:** advisor pass (overnight, 2026-08-12). No code changed to produce this plan.
**Roadmap parent:** [docs/design/2026-08-12-next-roadmap.md](../docs/design/2026-08-12-next-roadmap.md) — Track B. Recommended to run **before** plan 003 (harvest), because the harvest loop is experienced *through* the camera + character; a hard-snap camera over a sliding capsule makes any demo feel bad.

> Executor note: zero prior context assumed. Follow [CLAUDE.md](../CLAUDE.md): **Awaitable only**,
> **`ILogger`/`LoggerProvider`**, resolve services at init not per-frame, **do not commit** (Bryan's
> call). Build `ProceduralPlanets.Core.csproj` then `ProceduralPlanets.Planet.csproj` **serially**
> after each stage. **This is FEEL work — most of it is visual.** Per the project's visual-tuning
> rule: every feel constant you introduce (damp time, boom radius, accel rate) goes behind a **live
> console knob** so Bryan tunes it in play and *his* number gets baked. Do NOT hand-bake a guessed
> feel constant as final. Stages here are not "done" until Bryan has played them and said so.

## 1. Goal

Walking the planet and looking around in 3rd person feels good — smooth camera, a character with
presence, eased motion, and a real way to become the character. Concretely, on wake Bryan can:
1. Enter play → the character auto-spawns on the planet (no console needed) and an input toggles
   character mode on/off.
2. Orbit the camera → it glides (no per-frame snap) and never clips through terrain.
3. Move → acceleration/deceleration + smooth turning instead of instant velocity + snap-facing.
4. (Later stage) See a rigged, animated model walk/idle instead of a sliding capsule.

## 2. Current state (evidence — what exists vs the gap)

The movement **architecture is genuinely good** (clean, injected, spherical-first); the **presentation
is a prototype**. All character code is in `Assets/Scripts/Planet/Character/` (10 files).

| Piece | State | Evidence |
|---|---|---|
| Pure motor, planet-agnostic (`up` as param) | BUILT, clean | `CharacterMotor.cs:18` `TryStep` |
| Actor-agnostic driver w/ injected gravity+grounding | BUILT | `SurfaceCharacterController.cs:33,64-89`; `IGravityProvider`, `IGroundingProvider` |
| Radial gravity + analytic/raycast grounding | BUILT | `RadialGravityProvider.cs:8`; `PlanetRaycastGrounding.cs:11`, `PlanetSurfaceGrounding.cs:9` |
| 3rd-person orbit camera | **PARTIAL — hard-snaps every frame** | `PlanetCharacterController.cs:133-148` (orbit at `CamDistance=5.5`, no damping, no boom) |
| Mouse-look (hold-RMB) | works | `PlanetCharacterController.cs:108-118,165` |
| Movement smoothing | **ABSENT** — instant velocity + snap-to-facing | `SurfaceCharacterController.Tick` |
| Grounding normal | radial-up only (no slope lean) | `PlanetSurfaceGrounding.cs:39`, `PlanetRaycastGrounding.cs:46` |
| Character model | **ABSENT** — runtime `CreatePrimitive(Capsule)` that slides | `PlanetCharacterController.cs:268`; `Planet/PropLit` mat `:277-283` |
| Animation | **ABSENT** — no Animator/clip anywhere | grep: 0 hits |
| Spawn | console-only, aim-dependent | `CharacterCommands.cs:20-29`; `Spawn:152`, `TrySeedPose:223-261` |

**Single biggest feel gap:** no character *presence* — sliding capsule + hard-snap camera. The camera
smoothing (Stage 1) and the animated model (Stage 4) are the two highest-impact fixes.

## 3. Stage 1 — camera damping + collision-aware boom (highest impact)

The orbit rig in `PlanetCharacterController.LateUpdate` (`:133-148`) sets camera position/rotation
directly from the target every frame → hard snap; and the boom is a fixed length → it clips terrain.

**Do (in the camera rig code, keep it on the player host — it's player-facing, not the reusable driver):**
1. **Damp** the boom pivot position and the look rotation toward their targets with
   `Vector3.SmoothDamp` (hold a `_camVel` ref field) + `Quaternion.Slerp`/`RotateTowards`. Expose the
   smooth time as a console knob (e.g. `char.cam-smooth <seconds>`, human units per the console rule).
   Frame-rate independent (use `Time.deltaTime`). This alone removes the snap.
2. **Collision boom:** before placing the camera at `pivot - lookDir * CamDistance`, `Physics.SphereCast`
   (or, since chunks have no colliders — see caveat) an **analytic** surface check from the pivot outward
   along `-lookDir`, and pull the camera in to the first surface hit minus a small skin. Reuse the
   existing `IPlanetSurfaceRaycaster` (`Planet.cs` implements it; the player already holds one at
   `PlanetCharacterController.cs:32`) to find the terrain radius along the boom and clamp `CamDistance`
   so the camera never goes below the surface. Expose the desired distance + min distance as knobs.

**CAVEAT (important):** terrain chunks have **no mesh colliders** (analytic grounding only — see
`docs/design/2026-08-09-collision-strategy.md`). So a `Physics.SphereCast` will hit nothing. The boom
must use the **analytic raycaster** (`IPlanetSurfaceRaycaster.TryRaycastSurface`, `Planet.cs:515`), not
Unity physics. This is the same raycaster grounding already uses.

**Verify (play):** orbit the camera fast — it glides, no snap; walk toward a hill with the camera behind
into the slope — the camera pulls in instead of going underground. Bryan locks the smooth time + boom
distances.

## 4. Stage 2 — movement smoothing (accel/decel + turn slerp)

`SurfaceCharacterController.Tick` applies instantaneous planar velocity and snaps facing. Motion reads
robotic.

**Do (in the reusable driver — this benefits every future actor, so it belongs in
`SurfaceCharacterController`, not the player host):**
- Add an **acceleration** model: ease the current planar speed toward the input-target speed at a
  configurable m/s² (separate accel vs decel feels best). Keep it in the tangent plane the motor already
  works in.
- **Slerp the facing** toward the movement direction at a configurable deg/s instead of snapping.
- Both rates come from the driver's config/constructor (it already has a config seed per plan 001's
  `CharacterPose`/reset contract). Expose as console knobs for tuning.
- Preserve determinism/reset: on respawn/regen the smoothing state resets with the pose (plan 001 already
  established `ResetPose`).

**Verify (play):** start/stop walking — eased, not instant; turn 180° — the body rotates smoothly. No
regression to grounding (still glued to the surface). Bryan locks the rates.

## 5. Stage 3 — real spawn + input toggle (remove the console dependency)

Spawning is console-only and aim-dependent (`CharacterCommands.cs`, `Spawn:152` raycasts camera-forward).

**Do:**
- **Auto-place** the character on planet-ready: subscribe to the generation-complete event (the same
  `PlanetGeneratedEvent` the scatter/grass configure off — `Planet.cs:371`) and seed the pose at a valid
  surface point (reuse `TrySeedPose:223-261`; if the current camera aim misses the planet, fall back to a
  deterministic surface point like the sub-camera nadir rather than failing).
- **Input toggle:** add an `EnterExitCharacter` action to `InputMapService` (pattern: `AddButton("…",
  "<Keyboard>/…")`, see `InputMapService.cs:180`) that switches between the free debug camera and character
  mode, so becoming the character is a keypress, not a console command. Keep `character.spawn` working as a
  dev alias.

**Verify (play):** press Play → character is on the planet without touching the console; toggle key enters/
exits character mode; the free camera resumes cleanly on exit.

## 6. Stage 4 — rigged model + locomotion Animator (biggest presence win, do after 1-3)

The actor is a sliding primitive capsule (`PlanetCharacterController.cs:268`). Nothing about walking reads
as walking.

**Do:**
- Replace the runtime capsule with a **rigged Synty character**. The asset is already in-project and
  needs no hunt: `Assets/_Bench/SyntyFantasyHero/Prefabs/Characters_Presets/Chr_FantasyHero_Preset_22.prefab`,
  whose source `Models/ModularCharacters.fbx` imports as **Humanoid** (`animationType: 3`, verified) — so the
  Kevin Iglesias humanoid clip set (per `.agent-memory/.../reference_external_asset_library.md`) retargets
  onto it directly. Preset 22 is a generic fantasy hero — fine as the MVP placeholder; the wizard look
  (robe/hat/staff) is a later swap from the modular `PolygonFantasyHeroCharacters` pack in the external
  library. Parent the prefab to the same movable child the host drives (plan 001's host/child split), so
  grounding/camera/facing are unchanged — only the visual swaps.
- Add an **Animator** with an idle/walk/run blend driven by the driver's planar speed
  (`SurfaceCharacterController` grounded speed) + a grounded flag. Feet-slide is acceptable for the slice
  (no IK); a locomotion blend tree keyed to speed is the win.
- Use `Planet/PropLit` (planet-aware night-darkening) or the character's own lit material — NOT default URP
  Lit — per the surface-props lighting rule in `.agent-memory`.

**Verify (play):** walking shows a walk cycle, standing shows idle, and the model darkens on the night side.
Bryan's eyes lock the look. This stage is M/L — the rig import + blend tree is the bulk.

## 7. Deferred / out of scope (do NOT build here)

Slope-aware grounding normal (survey item #5, M — the body staying radial-upright is a *smaller* gap than
camera/animation; defer to after Stage 4), foot IK, jump/air-control polish, flight/swim drivers (separate
airborne driver per plan 001), ragdoll/physics (needs collider streaming — deferred project-wide). Keep this
plan to: camera feel, movement feel, spawn/toggle, animated model.

## 8. Files

**Modify**
- `Assets/Scripts/Planet/Character/PlanetCharacterController.cs` — camera damping + analytic boom (Stage 1);
  auto-spawn subscription + model swap host wiring (Stages 3-4).
- `Assets/Scripts/Planet/Character/SurfaceCharacterController.cs` — accel/decel + facing slerp (Stage 2).
- `Assets/Scripts/Core/Interfaces/IInputMapService.cs` + `Core/Services/InputMapService.cs` —
  `EnterExitCharacter` action (Stage 3).
- Add console knobs for the feel constants (camera smooth time, boom distances, accel/decel, turn rate) on
  the services that own them (`[ConsoleCommand]` on the player host / driver), human units per the rule.

**New**
- A character prefab + Animator controller asset (Stage 4) under `Assets/…` (Synty rig + locomotion blend).

## 9. Test plan

Mostly play-mode feel (Bryan's eyes lock each stage). One cheap EditMode guard: extend the existing
`CharacterMotor`/driver tests so the Stage-2 smoothing preserves the plan-001 invariants — a straight input
still converges to the target speed/facing (no overshoot to NaN), and `ResetPose` clears the smoothing
state. No new framework (UTF already present).

**No dedicated test scene / obstacle course** — do NOT build one for this plan. The real generated planet
already contains every feel-relevant situation (slopes for camera boom + slope walking, water edges,
cube-face corners, dense scatter to walk through), and there are **no colliders** on terrain or scatter
(`PlanetCharacterController.cs:271` strips even the capsule's collider), so there is nothing to *collide*
with — an obstacle course would test collision response that does not exist yet. Obstacles/traversal
(walls, steps, ledges, gaps, ragdolls) become testable only once collider streaming lands
(`docs/design/2026-08-09-collision-strategy.md`, deferred project-wide); a bespoke scene earns its place
then, not now.

**Repeatability instead (cheap, no scene):** tuning feel constants needs the SAME situation twice, so
before Stage 1: (a) pin a **fixed planet seed** so terrain is identical across tune passes; (b) save
teleport poses to ~4 canonical stress spots — a steep slope, a cube-face corner, a lake shoreline, a
dense-scatter patch — via the existing `CameraTeleportStore` / a console script (`scatter.goto <Biome>`
reaches biomes; `character.spawn` seeds the actor). That gives deterministic A/B for locking each knob.

## 10. Escape hatches

- If the analytic boom can't find a surface radius along `-lookDir` (over an ocean edge, off-planet), fall
  back to the fixed `CamDistance` rather than snapping the camera to the planet center.
- If a Synty rig import pulls in a build-breaking postprocessor (the library memory names CS0101 dup
  `SimpleCameraController`, ExplosiveLLC `SetupInputLayers`, FAE `UniversalPBRSubShader`), STOP the import
  and report — do not fight the postprocessor blindly.
- Any feel constant you're unsure of → ship the console knob and leave the number for Bryan; do not bake a
  guess as final.

## 11. Maintenance note

The host/driver split (plan 001) is load-bearing here: camera + input + model live on the **thin player
host**; accel/facing smoothing lives in the **reusable driver** so birds/bears/mounts inherit good motion.
Keep that boundary — do not push camera/input into `SurfaceCharacterController`. Feel constants are Bryan's
to lock; treat any baked number as his, not to be re-tuned without a fresh play session (visual-tuning rule).
