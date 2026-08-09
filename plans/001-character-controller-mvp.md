# 001 — Character controller MVP (walk the sphere)

**Planned at commit `c54fc72`** · Priority P1 · Effort M · Risk Med · Kind: spike (build, behind a toggle)

First on-planet agency: a placeholder character that spawns on the surface, walks along the sphere
with WASD relative to a third-person camera, stays glued to terrain height, bends grass as it moves,
and leaves a faint foot-trail. It is a **spike gated behind a console toggle** — the free-fly camera
workflow stays the default and is untouched when the character is off.

This is deliberately NOT the full Phase 10 character system. No flight, no jumping, no slopes/stamina,
no marching-cubes collision, no animation, no persistence. Those are follow-ups; this proves the core
loop feels right first.

---

## Drift check (run before starting; if any fails, STOP and report)

The plan quotes code at these anchors. Confirm they still exist before writing anything:

```bash
# Planet center/radius live on the camera rig context + are re-published each world load
grep -n "PlanetCenter\|PlanetRadius\|SeaLevelRadius" Assets/Scripts/Core/Services/FreeCameraController.cs
# expect: PlanetCenter => _lastPlanetCenter (≈ line 50), PlanetRadius (51), SeaLevelRadius (52)

# Ground query — analytic, no colliders on chunks
grep -n "TryGetSurfaceRadius\|TryRaycastSurface\|PlanetSurfaceRaycastHit" Assets/Scripts/Core/Interfaces/IPlanetSurfaceSampler.cs
# expect: TryGetSurfaceRadius(Vector3 worldUnitDirection, out float surfaceRadius) ; TryRaycastSurface(Ray, float, out hit)

# Grass bend contract + registry
grep -n "interface IGrassInteractor\|WorldPosition\|ReleaseSeconds" Assets/Scripts/Core/Interfaces/IGrassInteractor.cs
grep -n "public static void Register\|public static void Unregister" Assets/Scripts/Planet/Grass/GrassInteractorRegistry.cs
# expect: both STATIC — Register(IGrassInteractor) / Unregister(IGrassInteractor)
# Self-registering IGrassInteractor exemplar (implements + Register/Unregister on enable):
grep -n "class DebugGrassInteractor\|GrassInteractorRegistry.Register" Assets/Scripts/Planet/Grass/DebugGrassInteractor.cs

# Foot-trail path stamp
grep -n "public bool TryPaintDisc" Assets/Scripts/Planet/Surface/SurfaceEditController.cs
# expect: TryPaintDisc(Vector3 localUnitDirection, float radiusMeters, float strength, float regrowSeconds, bool saveStamp, out string summary, bool invalidateGrass = true, bool saveImmediately = true)

# World-service registration pattern to copy
grep -n "IWorldServiceRegistrar\|RegisterWorldServices" Assets/Scripts/Planet/CelestialManager.cs

# Input map is code-built (no .inputactions asset)
grep -n "AddAction(\"Move\"\|AddCompositeBinding(\"2DVector\")\|InputAction AddButton" Assets/Scripts/Core/Services/InputMapService.cs
```

If a signature differs (params added/reordered, renamed), STOP — the code moved since `c54fc72`;
re-derive against the current signatures before proceeding.

---

## Why this matters

The whole product vision is a full-planet third-person experience. Nothing in the repo yet lets you
*be* on the planet — only the free-fly debug camera. This is the smallest slice that turns "a planet
you look at" into "a planet you stand on," and it exercises three systems that already exist but have
never had a real client: the analytic surface sampler, the grass interactor registry, and the surface
edit (path-wear) stamp system. If those three don't compose cleanly behind one moving agent, we want
to find out now, cheaply, before building anything on top.

## Current state (what exists, with exact excerpts)

**No chunk colliders.** `ChunkMeshCache.CreateRenderHandle()` adds only `MeshRenderer` + `MeshFilter`.
So the character CANNOT use Unity physics/`CharacterController` against terrain. Ground height comes
from the analytic sampler instead — this is a feature, not a workaround: it's exact and collider-free.

`Assets/Scripts/Core/Interfaces/IPlanetSurfaceSampler.cs`:
```csharp
public interface IPlanetSurfaceSampler
{
    bool TryGetSurfaceRadius(Vector3 worldUnitDirection, out float surfaceRadius);
}

public struct PlanetSurfaceRaycastHit
{
    public Vector3 Point;
    public Vector3 Normal;
    public float Distance;
    public float SurfaceRadius;
}

public interface IPlanetSurfaceRaycaster
{
    bool TryRaycastSurface(Ray worldRay, float maxDistance, out PlanetSurfaceRaycastHit hit);
}
```
Both are registered as world services (see `SceneBootstrap.cs` required-services list) and resolved
from `ServiceLocator`. `TryGetSurfaceRadius(dir)` is the laziest ground: `pos = center + dir * radius`.
Use it for the MVP. `TryRaycastSurface` (returns a surface `Normal`) is the upgrade for slope-aware
orientation — noted, not built here.

**Planet center/radius** — `FreeCameraController.cs`, cached from `PlanetGeneratedEvent` each world load:
```csharp
public Vector3 PlanetCenter => _lastPlanetCenter;   // line ≈50
public float  PlanetRadius => _lastPlanetRadius;    // 51
public float  SeaLevelRadius => _lastSeaLevelRadius;// 52
```
These are also on the `ICameraRigContext` interface (`Assets/Scripts/Core/Services/ICameraRigContext.cs`),
which the free camera registers. Resolve center once at init; do not read per frame from the SO or event.
Prefer subscribing to `PlanetGeneratedEvent` directly (it carries `PlanetCenter`/`Radius`/`SeaLevelRadius`)
so the character doesn't depend on the camera being present.

**Grass bend** — implement `IGrassInteractor` and self-register:
```csharp
public interface IGrassInteractor
{
    Vector3 WorldPosition { get; }  // read fresh each upload
    float Radius { get; }           // meters, smoothstep falloff to 0
    float Strength { get; }         // ~0-1
    float ReleaseSeconds { get; }   // recovery time after moving away
    bool IsActive { get; }          // false = skipped, no GPU slot
}
```
`GrassInteractorRegistry.Register(IGrassInteractor)` / `Unregister(IGrassInteractor)` — **both `public
static`**. Complete self-registering exemplar: `Assets/Scripts/Planet/Grass/DebugGrassInteractor.cs:17-39`
(a MonoBehaviour that *implements* `IGrassInteractor` and calls `GrassInteractorRegistry.Register(this)`
in `OnEnable` / `Unregister(this)` in `OnDisable`; the registry packs a GPU buffer each frame). Note:
`CameraFollowGrassInteractor` is NOT the exemplar — it only repositions the debug object's transform and
neither implements nor registers the interface.

**Foot trail** — `SurfaceEditController.TryPaintDisc`:
```csharp
public bool TryPaintDisc(Vector3 localUnitDirection, float radiusMeters, float strength,
    float regrowSeconds, bool saveStamp, out string summary,
    bool invalidateGrass = true, bool saveImmediately = true)
```
Note it takes a **planet-local unit direction**, not a world position — obtain it via
`ISurfacePathBrushService.TryGetSurfacePathLocalDirection(worldPoint, out localDir)`
(`SurfaceEditController.cs:300-311`, `InverseTransformPoint`); do NOT pass raw `normalize(pos-planetCenter)`
(correct only while the planet transform is unrotated). Per-frame callers batch with **`saveStamp: true,
saveImmediately: false`** while moving (the stamp must be saved or regrowth has no source of truth — see
Step 5), then flush when the character stops (mirror `SurfacePathMousePainter.cs` — `TryPaint...` with the
default `saveStamp:true` + `saveImmediately:false`, `FlushSurfacePathEdits()` at stroke end). Throttle:
only paint after moving > ~half a brush radius since the last stamp, or trails get dense and expensive.

**World-service registration** — copy `CelestialManager` (MonoBehaviour + `IWorldServiceRegistrar`):
```csharp
public void RegisterWorldServices(IWorldContext context)
    => context.Register<ICelestialTimeController>(this);
```
`SceneBootstrap` collects every `IWorldServiceRegistrar` MonoBehaviour in the scene and calls
`RegisterWorldServices` on each. Initialization runs through `LoadingManager`'s
`IEarlyInitialize`/`ILateInitialize` graph — **no `[DefaultExecutionOrder]`, no
`RuntimeInitializeOnLoadMethod`.**

**Input** — `InputMapService` builds an `InputActionAsset` in code (no `.inputactions` file):
```csharp
Move = GameplayMap.AddAction("Move", ...);
Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w") ... ;   // WASD pattern (≈ line 63)
Sprint = GameplayMap.AddAction("Sprint", ...); Sprint.AddBinding("<Keyboard>/leftShift"); // ≈93
InputAction AddButton(string name, string binding) { ... }             // helper, ≈171
```
Reuse the existing `Move` action if it fits; otherwise add a character map the same way. Do not create
a `.inputactions` asset — the project builds bindings in code.

## Commands

```bash
# Compile check (code-health only — a green build is NOT visual/gameplay proof). C22 protocol:
# 1. Let Unity import the new scripts + regenerate project files (the .csproj are Unity-generated, git-ignored,
#    EnableDefaultItems=false → a pre-import dotnet build compiles ZERO new files and is falsely green).
# 2. Build the assembly the new files belong to (character code → ProceduralPlanets.Planet; Core builds transitively):
dotnet build ProceduralPlanets.Planet.csproj

# EditMode logic tests — Unity Test Runner (Window ▸ Testing ▸ Test Runner) or via Unity MCP run_tests.
# dotnet does not run Unity tests. New tests go in Assets/Tests/EditMode/ (ProceduralPlanets.Tests.EditMode).
```

Gameplay verification is play-mode + screenshot, driven by the run/operate skill (`.agent-skills/pp-run-and-operate/`).
The executor either drives Unity via MCP (`manage_editor` play, `manage_camera`, `debug.screenshot`) or
hands the visual gate to Bryan with exact steps. A build passing is necessary but not sufficient.

## Design — composable capabilities (resolved 2026-08-09)

Bryan's requirements make the controller a **reusable toolset**, not a one-off sphere-walker: gravity must
work on **any surface** (flat test planes, radial sphere, flight, anti-gravity spells) and the same
controller must drive **many actors** (birds, bears, dragons, knights on horseback). That converts the
gravity/grounding seams from speculative one-impl interfaces into seams with **genuinely multiple real
implementations** — so PP's "no interface with one implementation" rule does not bar them; they're earned.

The architecture is a **hybrid** of the two options weighed earlier, in three layers:
- **Pure motor (from option A).** `CharacterMotor.TryStep(up, …)` stays planet-agnostic and actor-agnostic —
  the tangent-step tool every actor reuses. It never learns about planets or gravity sources.
- **Injected gravity + grounding capabilities (from option C14).** An `IGravityProvider` ("which way is down,
  how strong?") and an `IGroundingProvider` ("where is the supporting surface?"). Swapping the **grounding**
  impl covers a flat test plane, the analytic sphere, and future collider/marching-cubes ground; swapping the
  **gravity** impl changes the down-direction/strength.
- **Actor-agnostic driver + thin player host (C19).** A plain `SurfaceCharacterController` composes the motor +
  the two capabilities and advances a pose from plain per-tick intent — **no** Unity input/camera/grass/console,
  **no** `planetCenter`/radius reconstruction, **no** direct sampler call. Only the thin `PlanetCharacterController`
  MonoBehaviour host carries the player-specific input, camera, grass, and console.

**Scope of reuse — be precise (C23).** `SurfaceCharacterController` is specifically a **grounded**
surface-locomotion driver: it always calls `TryGround` and holds pose on ground failure, and it uses only the
gravity *direction*. So **grounded** actors reuse it directly — bears, mounts, a bird's *grounded* state — but
**a gravity-provider swap alone does NOT produce flight or anti-gravity**: the driver would still snap to
support or refuse to move. A future *airborne* behavior is a **different driver** that reuses the lower shared
tools (`CharacterMotor` + `IGravityProvider`, which integrate acceleration incl. magnitude), and the deferred
behavior **FSM** (the [prior-art harvest](#appendix--prior-art-harvest-the-state-machine-project-2026-08-08)'s
`AdaptiveStateMachine`) selects *which* driver/behavior is live. (If a single live driver must ever span
grounded **and** airborne, that needs an explicit grounding/constraint-policy seam — deliberately NOT built in
this MVP.) So "one controller across many actors" means the *grounded* driver + the shared low-level tools —
not that this driver flies.

MVP still ships "walk the sphere": it *implements* only `RadialGravityProvider` + `PlanetSurfaceGrounding`
(plus constant-gravity + planar-ground fakes for tests). Flight/anti-grav (new gravity impls **and** an
airborne driver), NPC actors, and the behavior FSM are deferred follow-ups that reuse the shared tools.

## Scope

**In scope — create.** Two layers: an **actor-agnostic locomotion driver** (plain, reusable by any actor) and
a **thin player/planet host** (the only Unity/player-specific piece). This split is what actually delivers
"one controller across many actors" (C19) — the player deps live on the host, not the reusable driver.

*Reusable layer (no Unity input/camera/grass/console — a bird/bear/dragon/knight reuses this):*
- `Assets/Scripts/Planet/Character/CharacterMotor.cs` — pure static math, **planet-agnostic** (no Unity
  lifecycle, no planet center/radius, no gravity source). `TryStep(up, …, out next)` returns the tangent-plane
  step. Degenerate-`up` contract per Step 1 (C17).
- `Assets/Scripts/Planet/Character/IGravityProvider.cs` + `RadialGravityProvider.cs` — `bool
  TryGetGravity(Vector3 worldPos, out Vector3 acceleration)`. **Acceleration is in m/s²** (`up =
  -acceleration.normalized`); `RadialGravityProvider` points it toward the planet center with a **configurable
  finite positive magnitude** (default ~9.81). MVP consumes only the *direction*, but the magnitude is in the
  contract so future flight/jump/anti-grav (which integrate acceleration) need no signature change.
  **`TryGetGravity == true` guarantees a finite NON-ZERO acceleration** (callers derive `up` by normalization,
  so zero is not a valid "success", C24); a configured magnitude of zero, or sampling *at* the center, returns
  `false` + `Vector3.zero` (C20/C24).
- `Assets/Scripts/Planet/Character/IGroundingProvider.cs` + `PlanetSurfaceGrounding.cs` — `bool
  TryGround(Vector3 worldPos, Vector3 downDir, float footOffset, out GroundResult result)`,
  `GroundResult { Vector3 Position; Vector3 Normal; }`. **`true` = a valid grounded pose** (no redundant
  `IsGrounded` flag); **`false` = no support** → the caller keeps its prior settled pose and must not read the
  default `GroundResult` fields (C20). The planet impl wraps `IPlanetSurfaceSampler.TryGetSurfaceRadius`
  (radius → `Position = center + up*(radius+footOffset)`); **`Normal = radial up` explicitly** — the sampler
  returns no normal, and finite-difference gradient sampling would be slope-awareness, which is out of scope.
  This is the ONLY place that touches `IPlanetSurfaceSampler` / planetCenter.
- `Assets/Scripts/Planet/Character/SurfaceCharacterController.cs` — plain **actor-agnostic** class (NOT a
  MonoBehaviour; no input, camera, grass, console, or scene lifecycle). Holds the injected `IGravityProvider`
  + `IGroundingProvider`. **Seeded state (C24):** the constructor (or `ResetPose`) takes a finite
  `CharacterPose { Vector3 Position; Vector3 Up; Vector3 Forward; }` so its settled position / latched
  forward / last-valid-up start from a *known* pose — never from `Vector3.zero` (which would put radial gravity
  at the planet center, exactly where `TryGetGravity` returns `false`, with no last-up to hold). A
  `Tick(Vector2 move, Vector3 viewForward, float speed, float dt)` composes gravity → `CharacterMotor.TryStep`
  → grounding → final-frame orientation and returns the settled pose. The reusable driver every grounded actor
  shares (C19).

*Player/planet host (the thin Unity adapter — player-only behavior lives here):*
- `Assets/Scripts/Planet/Character/PlanetCharacterController.cs` — MonoBehaviour host. Resolves input + camera
  services, composes `RadialGravityProvider` + `PlanetSurfaceGrounding`, owns the movable child + grass +
  console lifecycle, and each `Update` forwards **plain intent** (`move`, camera-forward, speed, dt) into the
  `SurfaceCharacterController`, then applies the returned pose to the child. It contains no surface math of its
  own. Needs `OnEnable`/`OnDisable` (grass) + `Update`.
- `Assets/Scripts/Planet/Character/PlanetCharacterService.cs` (or fold into the host) — owns the
  `[ConsoleCommand]` spawn/toggle.
- A placeholder visual: a primitive capsule mesh (code-created) on the movable child. No art.

*Tests:*
- `Assets/Tests/EditMode/CharacterMotorTests.cs` — the seven pure-motor cases **and** the provider +
  plain-controller cases: `RadialGravityProvider` direction+magnitude, `PlanetSurfaceGrounding` placement, and
  **`SurfaceCharacterController` driven by a `ConstantGravityProvider` + `PlanarGroundingProvider` fake** with
  no planet supplied — the second implementation that makes these real seams and makes "reusable across actors"
  an executable test, not an assertion (C19/C22). (Split into a second fixture only if the file outgrows ~1 screen.)

**In scope — touch minimally:**
- **Creation path (required — a MonoBehaviour does not instantiate itself).** A `MonoTargetType.Single`
  console command FAILS if no live instance exists (`CommandExecutor.cs:125-131`), and `IWorldServiceRegistrar`
  only registers an *existing* scene object — it never creates one. Path: `SceneBootstrap.EnsureComponent<
  PlanetCharacterController>()` at boot (already used for WaterWakeController / ScaleReferenceMarkers,
  `SceneBootstrap.cs:87-88,110-120`). **Critical:** `EnsureComponent` `AddComponent`s onto the *bootstrap
  GameObject itself* (`:119`), so the controller must NOT move its own transform — it is an inert-but-ACTIVE
  **host** that creates and owns a **separate movable child** GameObject (the capsule + pose); grass, camera,
  grounding, and facing all read/move the child, never the host/bootstrap transform. Because the host is a
  live active component after boot, `character.spawn` is a **`MonoTargetType.Single`** command (it resolves
  the host via `FindObjectsInactive.Exclude` — hence host must stay active, `CommandExecutor.cs:197`). The
  child is destroyed with the world.
- **Free-camera input arbitration (required).** Add the smallest suspend surface to
  `IFreeCameraService`/`FreeCameraController`: an `InputSuspended` flag that early-returns from the input
  block of `Update` (gates `HandleLook`/`HandleMovement`/`HandleShortcuts`, `FreeCameraController.cs:126-136`)
  while the character owns control. Do **not** use `enabled=false` — `OnDisable` (`:73-77`) unsubscribes
  `PlanetGeneratedEvent` and drops the `ICameraRigContext` registration. Restore prior state on toggle-off
  and on every disable/destroy path.
- Camera follow itself is a small transform-follow (Step 4); do not fork a whole new camera system.

**Out of scope — do NOT build (STOP and report if a step seems to need these).** The gravity/grounding
*seams* are in scope (above); their non-MVP *implementations* and everything downstream are not:
- **Flight / anti-gravity / jump / gravity arcs / falling** — no flight or anti-grav `IGravityProvider`, no
  ungrounded/airborne handling. The MVP wires only `RadialGravityProvider`; the grounded driver always
  ground-snaps this pass. (Flight is a *new airborne driver* + new gravity impls reusing `CharacterMotor` +
  `IGravityProvider` — NOT a provider swap on this grounded driver, per C23; built later.)
- The behavior **state machine** and NPC actors (bird/bear/dragon/knight) — the seams make them compose in,
  but no FSM and no second actor are built now (one behavior, zero transitions — see the Appendix decision).
- Marching-cubes or mesh-collider physics. No `Rigidbody`, no `CharacterController`, no `Physics.Raycast`
  against terrain — the only grounding implementation built is the analytic sampler wrapper.
- Slope-aware locomotion, stamina, footstep audio, animation, IK.
- Persistence / saving character position across world loads.
- Any change to the free-fly camera's default behavior when the character is OFF.
- Any new runtime dependency or package.

## Steps (ordered; each ends with a verification)

### Step 1 — Pure motor math + tests (no Unity scene work yet)
Create `CharacterMotor` as a static class. It knows nothing about planets or gravity sources — it takes the
local `up` (gravity-down negated) as a parameter and returns the tangent-plane step; the caller supplies `up`
(from the gravity provider) and grounds the result (Step 2). A constant-`up` "flat world" is just a different
argument. **Reusable-API contract (C17):** because arbitrary callers use this, it must not rely on a
planet-specific precondition — expose a `TryStep` that fails cleanly on a zero-length or non-finite `up`:
```csharp
// Walk along the tangent plane defined by `up`, at `speed`, for `dt`, given a camera-derived forward.
// `next` = displaced world position BEFORE grounding (the adapter re-projects it to the surface).
// Returns false and leaves `next = currentPos` if `up` is zero-length or non-finite (no NaN escapes).
public static bool TryStep(
    Vector3 up, Vector3 currentPos, Vector2 moveInput,
    Vector3 cameraForward, float speed, float dt, out Vector3 next)
```
Logic: if `!IsFinite(up) || up.sqrMagnitude < 1e-12` → `next = currentPos; return false;`. Else
`up = normalize(up)`; project `cameraForward` onto the tangent plane (`forward - up*dot(forward,up)`,
normalize; if degenerate — camera looking along `up` — fall back to an arbitrary tangent); `right =
cross(up, forward)`; `move = right*input.x + forward*input.y`; clamp `|move|<=1`;
`next = currentPos + move*speed*dt; return true`. Allocation-free and deterministic.

Write `CharacterMotorTests.cs` following an existing EditMode test as the pattern
(`Assets/Tests/EditMode/ScatterHashTests.cs` — copy its `[Test]` structure and asserts; predeclare numeric
tolerances). Cover both spherical and **non-spherical** (proves the seam):
- (a) the step lies in the tangent plane: `abs(dot((next-currentPos).normalized, up)) <= 1e-5`;
- (b) zero input returns `currentPos` unchanged;
- (c) forward then backward returns near start;
- (d) input perpendicular to forward moves along `right`;
- (e) camera-looking-along-`up` (degenerate) doesn't NaN;
- (f) **flat-world portability:** with `up = (0,1,0)` the motor produces planar motion (y constant) — same
  motor, no planet center/radius supplied;
- (g) **degenerate `up` (C17):** `up = Vector3.zero` (and a non-finite `up`) returns `false` with
  `next == currentPos` — no NaN.

Provider correctness is tested separately: `RadialGravityProvider` gives `up` toward-center-negated at several
positions and never NaNs; sampling *at* the center fails cleanly (no invented direction); `PlanetSurface
Grounding` places a point at `center + dir*(radius+footOffset)`. These use the `ConstantGravityProvider` /
`PlanarGroundingProvider` fakes to prove the same controller math runs with no planet.

**Verify (build protocol, C22):** (1) let Unity **import** the new scripts and regenerate project files, with
no new compile errors in the console; (2) `dotnet build ProceduralPlanets.Planet.csproj` (the character files
live in the `ProceduralPlanets.Planet` assembly, `Assets/Scripts/Planet/ProceduralPlanets.Planet.asmdef`; it
references Core so Core builds transitively). A pre-import `dotnet build` is **falsely green** — the generated
csproj sets `EnableDefaultItems=false` and compiles only explicitly-listed files, so un-imported new scripts
are silently skipped. (3) EditMode tests green in Test Runner — report the pass count.

### Step 2 — Locomotion driver + host: spawn, compose, move (no grass/trail yet)
Two objects (C19): the plain **`SurfaceCharacterController`** does the surface math; the **host** MonoBehaviour
owns the Unity/player side and forwards plain intent into it.

**Host (`PlanetCharacterController`, inert-but-active on the bootstrap GameObject).** `character.spawn` (a
`MonoTargetType.Single` command — resolves the live host) creates the movable **child** at the camera's ground
point and activates it; the host never moves its own transform. Compose ONCE at spawn (not per frame): resolve
`IPlanetSurfaceSampler` + planet center/radius (from the already-live `ICameraRigContext`, since spawn can run
*after* `PlanetGeneratedEvent`; also subscribe `PlanetGeneratedEvent` to re-compose on regeneration), build
`RadialGravityProvider` + `PlanetSurfaceGrounding`. **Seed a deterministic pose FIRST (C24):** compute the
camera-radial candidate (`up = -gravity` at the camera's ground point; ground it via `PlanetSurfaceGrounding`),
build a finite `CharacterPose`, construct/`ResetPose` the `SurfaceCharacterController` with it, and apply that
pose to the child **before its first rendered frame** — so the driver never first-ticks from `Vector3.zero`
(the invalid planet-center case). Cache the camera-forward source (`ICameraRigContext.CameraTransform`) + the
`Move` action. On **respawn** and on **planet regeneration** (`PlanetGeneratedEvent`), rebuild the providers
and `ResetPose` the driver, so stale position/facing state never carries into a new provider frame. Each
`Update` the host reads `move` + camera-forward, calls `SurfaceCharacterController.Tick(move, camForward,
speed, dt)`, and applies the returned pose (position + up + forward) to the child. The host does no surface
math itself.

**Driver (`SurfaceCharacterController.Tick`).** Planet-agnostic; asks the injected capabilities — **no**
`planetCenter`/radius reconstruction, **no** direct sampler call (C14):
1. `_gravity.TryGetGravity(pos, out accel)` → `up = -accel.normalized`. On failure, hold the last valid `up`.
2. `CharacterMotor.TryStep(up, pos, move, viewForward, speed, dt, out next)`; if `false`, keep the prior pose.
3. `_grounding.TryGround(next, -up, footOffset, out ground)`; if `false`, keep the prior settled pose. Else
   `pos = ground.Position` and **`finalUp = ground.Normal`** (radial up for the planet impl — C20). Because
   `finalUp` is recomputed at the *settled* position (after the snap moved it), this fixes the pre-step-`up`
   one-frame lean (C15). `footOffset` = capsule half-height (Unity primitive origin is its center — NOT
   "eyeHeight").
4. Orient from the FINAL frame: project the latched forward onto the `finalUp` tangent plane; return
   `(pos, finalUp, forward)`. Idle-facing latch: store the last non-zero tangent forward, seed from the
   projected view forward; never build a rotation from a zero vector (model on `FreeCameraController.cs:201-209`).

`RadialGravityProvider` + `PlanetSurfaceGrounding` are the only planet-specific pieces; `CharacterMotor` and
`SurfaceCharacterController` are planet-agnostic. Log with `ILogger`/`LoggerProvider`. Comment only where WHY
is non-obvious (degenerate-gravity guard, final-frame `up`, idle-facing latch).

**Verify (play-mode):** `character.spawn` drops a capsule that WASD-walks the surface at terrain height (hugs
terrain, no sink/float, no NaN; the bootstrap object does not move); no visible one-frame lean at speed; idle
keeps a stable facing. The `dot(child.up, finalUp) >= 0.9999` check (at small radius + high speed) is an
EditMode assertion on the driver, since `SurfaceCharacterController` is testable without Unity.

### Step 3 — Grass bend
Implement `IGrassInteractor` on the host. **Boot-safe (C21):** `EnsureComponent` runs the host's `OnEnable`
at boot — *before* the first `character.spawn` creates the child — and `GrassInteractorRegistry.Register`
**immediately** snapshots `WorldPosition` (`GrassInteractorRegistry.cs:87` → `GrassInteractorDtos.From:32`,
before any `IsActive` gate), so a `WorldPosition => child.transform.position` throws an NRE at boot. Therefore:
- `WorldPosition` returns a valid fallback while unspawned (host position, or the last settled child pose) —
  it must be null-safe on its own, since the `IsActive` skip happens strictly *after* the `WorldPosition` read;
- `IsActive => spawned && child != null`;
- `Radius` ~1.5-2.5 m, `Strength` ~0.8, `ReleaseSeconds` ~0.6.

**Lifecycle (C16):** `GrassInteractorRegistry.Register(this)` in the host's `OnEnable`, `Unregister(this)` in
`OnDisable` (both static). The host stays enabled for the whole scene, so despawn merely flips `IsActive =
false`; do **NOT** `Unregister` on despawn. The registry skips inactive sources before packing a GPU slot and
emits a release on the active→inactive edge so grass springs back (`GrassInteractorRegistry.cs:151,153-162`);
`Register` is idempotent (`:83`). Unregister-on-despawn would leave the *next* spawn unregistered — the
always-active host's `OnEnable` never re-runs. Model on `DebugGrassInteractor.cs:17-39`.

**Verify:** (1) **fresh boot, no spawn** — no NRE from `Register`/`WorldPosition` during `SceneBootstrap`
(C21). (2) **first spawn** — grass bends around the character and springs back behind. (3) **spawn → off →
spawn** — grass bends on the *second* spawn too; while off the active GPU-slot count returns to zero;
`GrassInteractorRegistry.RegisteredCount` does not grow across the cycle.

### Step 4 — Third-person camera follow + free-camera suspend
Two parts, both required (position-follow alone is insufficient — the free camera keeps consuming input):

1. **Suspend the free camera.** Set the `InputSuspended` flag (Scope) so `FreeCameraController.Update`'s
   input block early-returns while the character is active. This is what actually prevents the conflict:
   a `LateUpdate` position-follow does mask the free cam's *position* (last writer wins), but the free
   camera otherwise still applies **rotation** (look `:236-237`, roll `:280-282`) and fires **shortcuts**
   (FaceSun/ToggleOrbit/FrameStorm `:149-163`) that yank the aim/pose. The flag gates all of look/move/
   shortcuts. Restore prior state on toggle-off and every disable/destroy path. Keep the event subscription
   + `ICameraRigContext` registration intact (do not `enabled=false`).
2. **Follow.** Each `LateUpdate` set the camera transform to `charPos + up*height - tangentForward*dist`,
   `LookAt(charPos, up)`.

(Real path, noted not required: a `ThirdPersonRig` that *registers* `ICameraRigContext` so
`DebugCaptureMetadataBuilder` and capture tooling read the character pose — `FreeCameraController` registers
that interface at ≈line 62. MVP moves the transform + suspends; the rig swap is a follow-up.)

**Verify:** play mode — spawn character, camera sits behind/above and follows the walk; free-fly look/roll/
shortcuts are inert while the character is active; toggling the character off fully restores free-fly.
Screenshot the third-person view.

### Step 5 — Foot trail (path wear) — OPTIONAL gate past the Step-4 MVP boundary
The clean MVP is Steps 1-4. Run Step 5 as an **optional integration gate**, not core: a continuously
walking character is an unbounded stamp producer and touches two open audit findings (F03 synchronous
whole-ledger saves, F10 5s full-mask replay while regrowing — `docs/audit/2026-07-22-consolidated-code-audit.md`).
Do not silently expand this spike into a fix for F03/F10.

Three corrections over the naive call:
- **`saveStamp:true`, not false.** `createdUnixSeconds`/`regrowSeconds` are recorded ONLY inside the
  `saveStamp` branch (`SurfaceEditController.cs:72-93`), and regrowth/replay/flush all operate on `_stamps`.
  `saveStamp:false` is worse than a no-op — the next flush or regrow tick rebuilds the mask from `_stamps`
  and **erases** the un-stamped paint. The trail has no source of truth unless the stamp is saved.
- **Batch the disk write.** Pass `saveImmediately:false` (the per-step whole-ledger `File.WriteAllText` is
  tied to `saveImmediately`, default true — that's the F03 hitch). Flush once when the stroke ends (input
  ~0 for a moment) or on toggle-off, mirroring `SurfacePathMousePainter` (`saveStamp` default true +
  `saveImmediately:false` + `FlushPendingSave`/`FlushSurfacePathEdits`).
- **Local direction, via the service.** The brush consumes a planet-*local* unit direction. Convert with
  `ISurfacePathBrushService.TryGetSurfacePathLocalDirection(worldPoint, out localDir)`
  (`SurfaceEditController.cs:300-311`, `InverseTransformPoint`) — do NOT feed raw `normalize(pos-center)`.
  (World-radial is correct only while the planet transform is unrotated, which it is today; using the
  service avoids baking that latent assumption.)

Throttle strictly (paint only after moving > ~half a brush radius) AND **bound the ledger** — cap or
decimate footstep stamps, or use a runtime-only non-persistent wear channel; unbounded growth is the F03
failure mode. Corrected call:
`TryPaintDisc(localDir, radiusMeters≈0.6, strength≈0.35, regrowSeconds≈30, saveStamp:true, out _,
invalidateGrass:true, saveImmediately:false)`.

**Verify:** walking leaves a visible faint trail; standing still stops adding to it; the trail regrows over
`regrowSeconds`; **and** record stamp count + flush duration at a fixed walk length. If either produces a
visible hitch, stop/defer the trail (it's the optional gate). Screenshot a walked loop.

### Step 6 — Toggle hygiene + final pass
Confirm: character OFF = zero effect on the existing free-fly workflow (no input capture, no camera
takeover, no grass slot, no stamps). One console command spawns/toggles. Remove any temporary debug
logging. Re-read the diff for scope creep and change-history comments.

**Verify:** full loop — `character.spawn` → walk a great-circle segment → grass bends, trail forms,
camera follows → toggle off → free-fly restored. Build green, EditMode tests green.

## Test plan

- **EditMode (automated):** `CharacterMotorTests.cs` — the **seven** motor cases (a)-(g) in Step 1 (incl. the
  non-spherical flat-world case and the degenerate-`up` `TryStep` case); the provider cases
  (`RadialGravityProvider` direction + **non-zero magnitude**, `PlanetSurfaceGrounding` placement); the
  `SurfaceCharacterController` driven by the `ConstantGravityProvider` + `PlanarGroundingProvider` fakes
  (proves the reusable driver runs with no planet) + the `dot(up, finalUp) >= 0.9999` orientation check; **and
  the C24 lifecycle cases** — seeded first `Tick` from a finite `CharacterPose` (never `Vector3.zero`);
  `TryGetGravity == false` before any successful sample (driver holds the seeded pose, no NaN); `ResetPose` to a
  new center leaves no stale position/facing. These are the cheaply-automatable part; the rest is inherently
  visual/gameplay. Run in Test Runner or MCP.
- **Play-mode (manual/MCP, screenshot each):** spawn on surface (Step 2), grass bend (Step 3), camera
  follow (Step 4), foot trail (Step 5), toggle-off restores free-fly (Step 6). Use a fixed pose for
  repeatability (`camera.teleport` to a saved grassland spot, freeze time at noon) so before/after
  frames are comparable — see the run/operate + step-test harness in `.agent-skills/`.

## Done criteria (machine-checkable where possible)

- After Unity imports the new scripts, `dotnet build ProceduralPlanets.Planet.csproj` exits 0 (C22 — a
  pre-import build is falsely green; the character files live in the Planet assembly).
- `CharacterMotorTests` all pass in Test Runner (report the pass count), incl. the flat-world, degenerate-`up`,
  provider, and fake-driven `SurfaceCharacterController` cases.
- Play-mode: `character.spawn` produces a surface-walking capsule; WASD moves it along terrain; grass
  bends around it; a third-person camera follows and free-fly look/roll/shortcuts are inert while active;
  toggling off restores free-fly. (Foot trail is the optional Step-5 gate, not a core done-criterion.)
  Evidence = labelled screenshots.
- **Numeric ground check (falsifiable "no sink/float"):** a `character.status` command (or capture
  metadata) reports `abs(|position - center| - sampledSurfaceRadius - capsuleFootOffset)`; predeclare a
  tolerance (≤ 0.05 m while grounded) and confirm it holds across a walk.
- No coroutines / `async void` / `Task.Run`; no `[DefaultExecutionOrder]`; no new
  `RuntimeInitializeOnLoadMethod`; no `.inputactions` asset; no `Rigidbody`/`CharacterController`/physics
  raycast against terrain; no new package. (`grep` the new files to confirm.)
- The character contributes nothing when not spawned.

## STOP conditions (report back, do not improvise)

- `TryGetSurfaceRadius` returns false at the character's position (planet not generated, or sampler not
  registered) — don't invent a fallback ground; report and confirm the boot order with Bryan.
- The `Move` input action doesn't exist or is bound to camera-only semantics that can't be reused —
  report before adding a new input map, so we agree on the binding surface.
- Grass bend or path-stamp requires touching `GrassInteractorRegistry` / `SurfaceEditController`
  internals (not just calling their public methods) — that's beyond this spike; report the blocker.
- Third-person camera can't be done by repositioning the existing transform and genuinely needs an
  `ICameraRigContext` swap — stop and confirm scope before building a camera system.
- Any step pulls in flight, jumping, physics, or persistence — out of scope; report.

## Maintenance notes

- **Ground model will change.** When marching-cubes / collidable terrain lands (Phase 9), the analytic
  `TryGetSurfaceRadius` ground-snap is where real collision replaces the height sample. Keep the ground
  query isolated (one method) so it's a single swap point.
- **Camera.** If this graduates past a spike, replace the transform-follow with a `ThirdPersonRig` that
  registers `ICameraRigContext` — capture/debug tooling reads pose through that interface and will
  otherwise be blind to the character camera.
- **Input.** Bindings are code-built in `InputMapService`; a future rebinding/settings pass touches that
  one place. Don't introduce a `.inputactions` asset without a project-wide decision.
- **Review watch-items:** per-frame `ServiceLocator` calls (must be cached at init), unthrottled path
  stamps (perf), grass interactor left registered after despawn (leak), and any tangent math that can
  NaN when the camera looks straight down/up.

---

## Codex review feedback — 2026-08-08

**Verdict: HOLD for plan corrections.** The collider-free locomotion slice is the right first move,
and the pure-math seam is appropriately small. The following items must be resolved in the plan before
implementation; they are design/API mismatches in the current tree, not optional polish.

### Blocking corrections

**C1 — Specify how the controller instance comes into existence and who owns it.** Console scanning
discovers the command *type*, but a `MonoTargetType.Single` command still fails unless a live component
already exists (`CommandExecutor.cs:190-198`). `IWorldServiceRegistrar` registers an existing object; it
does not create one. The scope currently names only scripts and no scene/bootstrap edit, prefab, or
static factory. Choose one explicit path:

- add a dedicated, enabled-but-unspawned character root to `Assets/Scenes/Planet.unity`; or
- make `character.spawn` a static command that creates one scene-owned root after a world is ready.

Whichever path is chosen must be destroyed with the world and must work when the command is first run
after `PlanetGeneratedEvent` has already fired. Do not add a character world-service interface unless a
real consumer needs to resolve it; the command target alone is not a reason for service registration.

**C2 — Add explicit free-camera input arbitration.** `FreeCameraController.Update()` unconditionally
calls look, movement, and shortcuts (`FreeCameraController.cs:126-136`), and its movement path reads the
same `IInputMapService.Move` action proposed for the character (`:264-282`). A character `LateUpdate`
that merely overwrites the camera transform will fight a free camera that is still moving earlier in
the frame. Add the smallest ownership surface to `IFreeCameraService`/`FreeCameraController` that
suspends free-camera controls while preserving its event subscription and `ICameraRigContext`; restore
the previous state on toggle-off and every disable/destroy path. Do not disable the whole gameplay map,
because the character needs that map's `Move` and `Sprint` actions.

**C3 — The proposed foot-trail call cannot regrow.** Step 5 passes `saveStamp:false`, but
`SurfaceEditController.TryPaintBrush` adds `createdUnixSeconds` and `regrowSeconds` only inside the
`saveStamp` branch (`SurfaceEditController.cs:72-93`). `TickRegrowth` operates only on `_stamps`
(`:419-433`), and `FlushPendingSave` rebuilds the mask from those stamps (`:283-295`). The mouse painter
does **not** pass `saveStamp:false`; it uses the interface default `saveStamp:true` with only
`saveImmediately:false` (`SurfacePathMousePainter.cs:224-227`). Resolve `ISurfacePathBrushService` and
mirror that exact contract: `saveStamp:true`, `saveImmediately:false`, then flush at stroke end.
Otherwise the mask can be painted transiently but the requested 30-second regrowth has no source of
truth.

**C4 — Use the path-brush conversion contract, not `normalize(pos - center)`.** The brush expects a
planet-*local* unit direction. `ISurfacePathBrushService.TryGetSurfacePathLocalDirection` delegates to
`planetTransform.InverseTransformPoint` (`SurfaceEditController.cs:300-311`); raw world radial math is
only equivalent while the planet transform is identity. Convert through the interface before every
stamp. Continue using the world direction for `IPlanetSurfaceSampler`, whose contract is explicitly
world-space.

### Scope and evidence amendments

**C5 — Put the foot trail behind its own performance/data-integrity gate.** Correcting C3 makes the
character append durable stamps and flush the full ledger. Open audit F03 records synchronous,
whole-ledger, non-atomic saves, and F10 records a full surface-mask replay every five seconds while any
stamp is regrowing (`docs/audit/2026-07-22-consolidated-code-audit.md:132-160,316-336`). A continuously
walking character is a much heavier producer than the mouse tool. The clean MVP boundary is Step 4;
run Step 5 as an optional integration gate. If retained, record stamp count, flush duration, and the
five-second replay hitch at a fixed walk length, then stop/defer the trail if either produces a visible
hitch. Do not silently expand this spike into a fix for F03/F10.

**C6 — Seed and refresh dependencies at lifecycle boundaries.** Resolve the application input service
once. Resolve the current world's sampler, brush service, camera rig, and free-camera service when the
controller is created/spawned; refresh world-owned references on the repository's world/generation
boundary rather than in `Update`. A statically spawned controller must initialize from the already-live
`ICameraRigContext` because it will have missed the earlier `PlanetGeneratedEvent`; the event remains
necessary for later regeneration.

**C7 — Preserve facing when input is zero.** The plan says orientation uses the tangent move direction,
but that direction is zero while idle. Store the last valid tangent forward and use the projected camera
forward only for the initial value; never call `Quaternion.LookRotation` with a zero vector. The existing
free-camera fallback pattern at `FreeCameraController.cs:201-209` is a suitable stable-axis model.

**C8 — Make the play-mode height check numeric.** Add a cheap `character.status` value or equivalent
capture metadata reporting
`abs(|position-center| - sampledSurfaceRadius - capsuleFootOffset)`. Predeclare a tolerance (for example
`<= 0.05 m` while grounded) so “no sink/float” is falsifiable. Use capsule half-height/foot offset, not an
ambiguous `eyeHeight`, because Unity's primitive origin is at its center.

### Minor plan drift

- The drift-check grep for `public void Register|Unregister` fails at the stamped commit because the
  current methods are `public static void` (`GrassInteractorRegistry.cs:81,99`). Fix the anchor.
- `DebugGrassInteractor.cs:17-39`, not `CameraFollowGrassInteractor`, is the complete self-registering
  `IGrassInteractor` exemplar. The follow component only moves the debug object.
- The EditMode project and NUnit pattern are current and correctly referenced; keep the five focused
  math tests and state their numeric tolerances.

---

## Claude verification of Codex feedback — 2026-08-08

I re-read each cited range in the current tree. **Every Codex item C1-C8 + both minor drift items is
CONFIRMED.** Details and my additions:

| Item | Verdict | My addition / nuance |
|------|---------|----------------------|
| **C1** instance bootstrap | CONFIRMED | `MonoTargetType.Single` resolves via `FindAnyObjectByType(..., Exclude)` and *fails* at `CommandExecutor.cs:125-131` when no live instance exists; `IWorldServiceRegistrar` only registers existing scene MBs. Codex framed the fix as binary (scene-root **or** static factory) — there's a cleaner **third** in-repo path: `SceneBootstrap.EnsureComponent<T>()` `AddComponent` at boot (`SceneBootstrap.cs:110-120`, already used for WaterWakeController / ScaleReferenceMarkers at `:87-88`). Use that + a static `character.spawn`. |
| **C2** camera input arbitration | CONFIRMED | `FreeCameraController.Update()` gates only on `input==null`, then runs look/move/shortcuts unconditionally. Refinement: a LateUpdate *position* follow does **not** visibly fight (last writer wins on position); the real conflict is **rotation** (look `:236-237`, roll `:280-282`) + **shortcuts** (FaceSun/ToggleOrbit/FrameStorm `:149-163`). So the suspend must gate the whole input block, and must **not** be `enabled=false` — `OnDisable` (`:73-77`) unsubscribes `PlanetGeneratedEvent`. |
| **C3** foot-trail regrowth | CONFIRMED (no caveat) | `createdUnixSeconds`/`regrowSeconds` are written only inside the `if (saveStamp)` branch; `TickRegrowth`/`FlushPendingSave` act solely on `_stamps`. My `saveStamp:false` is worse than a no-op — the next flush/tick rebuilds the mask from `_stamps` and **erases** the un-stamped paint. |
| **C4** local-direction conversion | CONFIRMED | Convert world→local via `ISurfacePathBrushService.TryGetSurfacePathLocalDirection` (`SurfaceEditController.cs:300-311`, `InverseTransformPoint`). Precision Codex missed: the break condition is planet **rotation** only — `normalize(pos-center)` already subtracts translation and uniform scale cancels, and today's planet root is identity + non-rotating, so raw radial is **correct now**. It's a latent contract bug, not a live break; fix it so the plan doesn't bake the identity assumption. |
| **C5** durable-stamp cost (F03/F10) | CONFIRMED | F03/F10 exist verbatim. **Key synthesis with C3:** the whole-ledger `File.WriteAllText` + full rebuild is tied to `saveImmediately` (default true), **not** to `saveStamp`. Resolved design → `saveStamp:true` + `saveImmediately:false` + flush at stop + a **bounded/decimated** footstep ledger (or runtime-only non-persistent wear). A walker is an unbounded producer. F10's 5s-replay is an *unprofiled* mechanism (MED confidence) — instrument it, don't assume. Codex is right that **Step 4 is the clean MVP boundary**; keep Step 5 optional + gated. |
| **C6** lifecycle seeding | CONFIRMED | Sound. A post-event spawn must init from the already-live `ICameraRigContext` (missed the earlier `PlanetGeneratedEvent`); keep the event for later regeneration. |
| **C7** idle facing | CONFIRMED | Real hole in my Step 2 ("forward = tangent move dir" is zero when idle → `LookRotation(zero)` NaN). Store last-valid tangent forward; seed from projected camera forward. Model on `FreeCameraController.cs:201-209`. |
| **C8** numeric height check | CONFIRMED | Add `character.status` reporting `abs(|pos-center| - sampledRadius - footOffset)` with a predeclared tolerance (≤0.05 m). Use capsule foot offset, not "eyeHeight" (primitive origin is center). |
| **minor** grep | CONFIRMED | Methods are `public static void` — drift grep must be `public static void Register\|public static void Unregister`. |
| **minor** exemplar | CONFIRMED | Self-registering `IGrassInteractor` exemplar is `DebugGrassInteractor.cs:17-39`, **not** `CameraFollowGrassInteractor` (which doesn't implement/register the interface). |

### Required body edits before execute — APPLIED 2026-08-08

All seven corrections below are now folded into the plan body above (drift check, Current state, Scope,
Steps 2/3/4/5, Done criteria). Retained as the change record for Codex's second-opinion pass.

1. **Drift grep** → `public static void Register\|public static void Unregister`.
2. **Current-state "Grass bend"** exemplar → `DebugGrassInteractor.cs:17-39`.
3. **Scope / Step 2** → name the creation path (`SceneBootstrap.EnsureComponent<PlanetCharacterController>()`
   at boot, or a scene-placed root); `character.spawn` is a **static** command that enables the boot-created
   root; init from the live `ICameraRigContext` on a post-event spawn.
4. **Step 2** → add idle-facing (store last-valid tangent forward; never `LookRotation(zero)`) — C7.
5. **Step 4** → replace "just reposition the transform" with: add a minimal `InputSuspended` early-return
   to `FreeCameraController`'s input block (gates look/move/shortcuts), restore on toggle-off + every
   disable/destroy path; **do not** use `enabled=false`.
6. **Step 5** → `saveStamp:true`, `saveImmediately:false`, flush at stop; bound/decimate the ledger;
   instrument stamp count + flush ms; keep Step 5 as an optional gate past the Step-4 MVP boundary.
7. **Done criteria** → add the C8 numeric height check + tolerance.

Substance of the plan (collider-free analytic locomotion, pure-math seam + EditMode tests, toggle-gated
spike) stands. These are corrections, not a redesign.

---

## Codex re-review feedback — 2026-08-08

**Verdict: HOLD for one architectural revision and two consistency fixes.** C2-C8 are substantially and
correctly folded into the executable steps. The new portability requirement exposes a missing seam in the
movement design, and the selected bootstrap path currently moves the wrong transform.

### Required architecture revision

**C9 — Make gravity a composable capability, with radial gravity as the first provider.** The plan currently
hard-codes `up = normalize(position - planetCenter)` in `SphereLocomotion` and explicitly excludes gravity.
That builds a spherical-planet controller, not a controller that can be used somewhere else. Add the
smallest useful contract, for example:

```csharp
public interface IGravityProvider
{
    bool TryGetGravity(Vector3 worldPosition, out Vector3 acceleration);
}
```

Implement `RadialGravityProvider` as a plain class whose acceleration points from the sampled world
position toward the configured planet center. Inject the provider into the motor/controller and derive
local `up` from `-acceleration.normalized`; do not let generic locomotion calculate a planet radial. The
acceleration vector preserves a future magnitude without requiring this ground-snapped MVP to integrate
vertical velocity, jumping, or falling. A different environment can supply constant, directional, or
custom gravity without changing locomotion code.

Keep this an internal injected service unless a second subsystem genuinely needs to resolve gravity. If it
is registered as a world service, keep it optional while the character is toggle-gated; do not add it to
every `SceneBootstrap.RequiredWorldServices` profile merely to support this spike. Resolve/inject once at
spawn or world initialization, never through `ServiceLocator` per frame.

**C10 — Abstract grounding separately; gravity injection alone does not make the controller portable.** The
current controller still calls the planet-specific `IPlanetSurfaceSampler` and reconstructs a point from
center + radius. Add one small grounding capability consumed by the same motor, for example a method that
takes the proposed position, gravity-down direction, and foot offset and returns grounded position plus
ground normal. The first implementation, `PlanetSurfaceGrounding`, wraps
`IPlanetSurfaceSampler.TryGetSurfaceRadius`; a future flat/collider environment supplies another
implementation. Keep the two responsibilities distinct:

- gravity answers "which way is down, and how strong is it?";
- grounding answers "where is the supporting surface?"

The reusable pure class should therefore be `CharacterMotor`/`SurfaceLocomotion`, not a
`SphereLocomotion` API that accepts `planetCenter` and `surfaceRadius`. `PlanetCharacterController` remains
the Unity/world adapter that composes `RadialGravityProvider` + `PlanetSurfaceGrounding` for this scene.

**C11 — Prove the seam with non-spherical tests.** In addition to the existing sphere cases, require:

- radial gravity at several positions points toward an arbitrary non-origin center and never returns NaN;
- sampling exactly at the gravity center fails cleanly instead of inventing an arbitrary direction;
- tangent movement has no component along the provider's local up before grounding (predeclare a dot-product
  tolerance, for example `abs(dot(displacement.normalized, up)) <= 1e-5`);
- the same motor passes with a constant-down gravity fake and planar-grounding fake, with no planet center
  or radius supplied to the motor;
- radial gravity and analytic grounding refresh correctly after `PlanetGeneratedEvent`, and
  `character.status` reports the active provider plus the existing numeric ground error.

### Remaining executable contradictions

**C12 — Do not move the `SceneBootstrap` transform.** `SceneBootstrap.EnsureComponent<T>()` searches scene
roots and, when missing, calls `gameObject.AddComponent<T>()` on the bootstrap object
(`SceneBootstrap.cs:110-120`). Step 2 then positions `PlanetCharacterController.transform`, which would
move the bootstrap root and every sibling component on it. Keep the ensured component as an inert host and
have it create/own a separate movable child root for the capsule and character pose. The child is destroyed
with the scene/world; grass position, camera follow, grounding, and facing all read the child pose.

Once `EnsureComponent<PlanetCharacterController>()` has run, the host is a live active component, so
`character.spawn` should be a `MonoTargetType.Single` instance command. Use a static command only if the
command itself creates the missing host; combining boot creation with a static command adds a second
ownership route and leaves the plan silent about how the static method finds the instance.

**C13 — Remove the stale path-stamp instructions.** Current state lines 127-132 still say moving callers
pass `saveStamp:false` and describe the local brush direction as raw `normalize(position - planetCenter)`.
Both statements contradict corrected Step 5. Change that paragraph to
`saveStamp:true, saveImmediately:false`, flush at stroke end, and obtain local direction through
`ISurfacePathBrushService.TryGetSurfacePathLocalDirection`. Until that text is corrected, the plan still
contains executor-visible C3/C4 bugs despite marking them "APPLIED."

With C9-C13 folded into the body, Steps 1-4 remain a focused MVP: composable local gravity, composable
ground snap, tangent movement, grass interaction, and camera ownership. Gravity *arcs* remain out of scope;
the gravity provider does not.

---

## Claude verification of Codex re-review — 2026-08-08

Verified against the tree (3 parallel agents). **C12 + C13 CONFIRMED.** C9-C11 is a design decision (not a
factual claim) — my position + a question to Bryan are below.

| Item | Verdict | Detail / my position |
|------|---------|----------------------|
| **C12** creation path | CONFIRMED | `EnsureComponent<T>()` does `AddComponent` on the **bootstrap GameObject itself** (`SceneBootstrap.cs:119`) — moving the controller's transform moves bootstrap + siblings. Fix: ensured component is an inert host owning a **separate movable child**; grass/camera/grounding/facing read the child pose. **Reconcile my own contradiction:** Scope/Step 2 currently say a *static* `character.spawn`; with a boot-created host it should be **`MonoTargetType.Single`** (static only if the command creates the host). **Caveat:** `Single` uses `FindObjectsInactive.Exclude` (`CommandExecutor.cs:197`) → the host must be inert-but-**ACTIVE**. |
| **C13** stale foot-trail text | CONFIRMED | Current-state lines 127-128 still say `saveStamp:false` + raw `normalize(pos-center)`, contradicting corrected Step 5. Rewrite the excerpt to `saveStamp:true, saveImmediately:false` + `ISurfacePathBrushService.TryGetSurfacePathLocalDirection`. (My "APPLIED" marker was premature for this paragraph.) |
| **C9/C10** composable gravity + grounding | DESIGN CALL | Codex wants injected `IGravityProvider` + grounding interfaces (RadialGravityProvider / PlanetSurfaceGrounding). **My position:** take the *seam* — make the pure motor **planet-agnostic via plain parameters** (`up` + ground data; rename `SphereLocomotion` → `CharacterMotor`) — but NOT one-implementation interfaces on a one-planet project unless portability is a real near-term requirement. That's a YAGNI vs. reuse fork → asked Bryan. Interfaces are a trivial later extract from the plain-parameter motor. |
| **C11** non-spherical tests | PARTIAL | The valuable test cases (motor passes with a constant-down + planar-ground fake; radial gravity never NaNs; sampling at the center fails cleanly; tangent motion has no up-component) are all achievable with the **plain-parameter** motor — no interfaces required. Adopt the test list regardless of the C9/C10 outcome. |

### Body edits — APPLIED 2026-08-08 (Bryan chose "planet-agnostic params")

All three folded into the body above; retained as the change record.

1. **C13 (applied):** Current-state "Foot trail" paragraph now says `saveStamp:true, saveImmediately:false`
   + flush at stroke end + local direction via `TryGetSurfacePathLocalDirection`.
2. **C12 (applied):** Scope/Step 2 → inert-but-**active** host owns a separate movable child;
   `character.spawn` is `MonoTargetType.Single`; everything moves/reads the child, never the bootstrap
   transform (active-host caveat noted for `FindObjectsInactive.Exclude`).
3. **C9-C11 (applied — option A):** motor renamed `SphereLocomotion` → **`CharacterMotor`**, planet-agnostic
   (`Step(up, …)` returns the tangent step, no planet center/radius); the controller computes radial `up` +
   ground-snaps. Test list includes the non-spherical flat-world case + degenerate-center clean-fail. No
   `IGravityProvider`/`IGrounding` interfaces added (one-implementation-each deferred until a 2nd world type
   is real — trivial later extract).

---

## Appendix — prior-art harvest: the "State Machine" project (2026-08-08)

Bryan's earlier, unfinished character-controller experiment at `C:\Users\Bryan\Source\Repos\Magikorp\State
Machine` (inspected: branch `main`, commit `95b0dfc159254d6a99b203089b68902104689b24`). Read all ~95 scripts
via 6 parallel readers. **Reality: it's a WIP skeleton — the MOTOR is
unfinished** (`CharacterMotor.ApplyMotion` is an empty stub; `CharacterMotorRefactored`,
`PlayerCharacterControllerRefactored`, `SlopeMotorBehavior`, `StepUpMotorBehavior`, `StepDetectionSensor`,
`CharacterMotionContext`, and every `ICharacter*Context` interface are 0-byte files). So there is **no
motion code to lift — harvest patterns, not code.** It is also entirely Unity-physics/collider based
(Rigidbody + CapsuleCollider + `Physics.Raycast`), which PP's collider-free analytic terrain cannot use.

### What it VALIDATES about this plan (confirmation, no work)
- **Producer/consumer split.** Their states only write `Intent.DesiredVelocity/Rotation`; one motor is the
  sole applier (`CharacterLocomotionState.cs:8-21`). That is exactly this plan's pure `CharacterMotor.Step`
  (produces the tangent step) + adapter (sole applier / ground-snap). Option-A arrived at the same seam.
- **Flat-world is the #1 sphere trap — the `up`-parameter dodges it.** Their code bakes world-up everywhere:
  `camForward.y=0` flatten (`PlayerInputProvider.cs:30-33`), `LookRotation(dir)` with no up
  (`CharacterLocomotionState.cs:19`), slope via `Vector3.Angle(Vector3.up, normal)`, fall via `position.y`.
  On a sphere `up = normalize(pos-center)` varies continuously. This plan's `Step(up, …)` +
  `LookRotation(dir, up)` + tangent-plane projection already avoid every one — the harvest confirms that was
  the load-bearing decision.

### MVP folds (small, adopted into the steps)
- **Prime the ground once at spawn** (Step 2) — sample + snap before the first `Update` so frame 0 isn't
  mis-placed (`BaseCharacterController.cs:127` primes the sensor before the first tick).
- **Adapter pipeline order** read-input → sense-ground → decide (`Step`) → apply/snap, collapsed to a single
  `Update` (no `FixedUpdate`/physics). Already this plan's shape; now explicit.

### Graduation backbone (Phase 10 — what the spike grows INTO, NOT the MVP)
The genuinely valuable, portable asset is the **generic hierarchical FSM** — adopt it when a 2nd mode
(flight), then build/dig, actually exist; PP's "split when adding a responsibility" rule gates it until then.
- `AdaptiveStateMachine<TContext>` — generic, context injected per-update (FSM stores no context); one cached
  state instance per type per `AdaptiveStateMachine` instance (`_stateCache` is an instance dict,
  `AdaptiveStateMachine.cs:20` — states are per-FSM, not static/global singletons). Port as
  `AdaptiveStateMachine<PpCtx>` where **PpCtx is a
  PP-owned readonly struct** (radial up, sampler results, input DTO, **dt/time**). Their FSM reads static
  `Time.deltaTime`/`Time.time` (`AdaptiveStateMachine.cs:128`, JumpState) — PP must thread dt via context.
- **Hierarchical composites** — `Grounded{Walk/Run/Sprint}` vs `Airborne{Fly/Fall}` parent states, each a
  sub-FSM (`CompositeState`), later `Building`/`Digging` sub-trees.
- **Context-adaptive `ResolveTo(from, ctx) => Type`** — the standout idea: one "leave grounded" edge resolves
  to Fly vs Fall vs Slide from context (`GroundedTransitionBuilder.cs:112`, `PickMovementState`). Decouples
  "when to leave" from "where to go."
- **`EvaluateExit` self-exit** — a state signals its own completion → the home for "dig-complete /
  place-block-complete / landing-recovery-done."
- **Robustness:** `WithNullDefault` + `ErrorState` sink; a `BlockTimeout` watchdog force-unblocks a stuck
  uninterruptible action (jump/dig anim) so it can't soft-lock.
- **Declarative transition TABLE + fluent builder** (`.WithStates().WithTransitions().WithInitialState()`) —
  the whole nested machine wired in one readable expression.

### Adopt-later, with caveats
- **Context taxonomy** (transient Input/Intent vs persistent Motor/Sensor vs tuning Config) — port the
  *taxonomy*, not the classes. Config → a PP SO→DTO; per-frame state stays a small mutable struct (PP's
  immutable-DTO rule governs *settings*, not per-frame state). Do NOT import their mutable god-`CharacterContext`.
- **Rules predicate DSL** (`IRule` + implicit→`Func` + And/Or/Not/Threshold/Func) — clean and EditMode-testable,
  but **optional / lean-skip**: lift only the ~6 core combinators *if* the transition set grows; default to
  inline `Func<ctx,bool>`/lambdas. Their class-per-predicate `Character/` rules (nine one-method singletons)
  are the anti-pattern to avoid (one interface / one impl vs PP style) and are physics-bound anyway.
- **Adaptive sensor throttling** (`SensorUpdateMode` + `SensorTransition`: idle NPCs sample every 6th frame,
  airborne every frame) — a real per-frame-cost LOD lever once many analytic-ground NPCs exist. No physics.
- **Speed tiers** (Idle/Walk/Run/Sprint by input magnitude), **coyote time**, **landing severity by
  fall-distance**, **Override\* physics-term flags** (reframed as radial-vs-tangent ownership),
  **vitals/stamina gates** — all Phase-10, all need axes re-expressed against radial up.

### SKIP (dupes / anti-patterns / dead)
- Their **EventBus** (dupe; theirs is process-global, no world scoping), **Singleton/PersistentSingleton**
  (PP forbids; use `IWorldServiceRegistrar`/WorldContext), **Logwin** logger (→ `ILogger`),
  **PlayerInputProvider** (dupe of PP input service; also flat-world + `Camera.main`).
- **All physics/collider motor+sensor code** (Rigidbody, CapsuleCollider, `Physics.Raycast`/`OverlapCapsule`,
  `RequireComponent`) — replaced wholesale by `IPlanetSurfaceSampler`.
- **The empty `ICharacter*Context` interfaces** — PP's "no interface with one implementation" *agrees*; don't
  resurrect them.
- **`LocomotionSettings` static-const + `CharacterStats` mutable POCO** (two overlapping config surfaces) →
  one PP SO→DTO. The reflection+attribute Config bridge is a weaker dupe of SO→DTO — skip.
- Dead code: `ApplyMotion` empty; `HardLanding` has no exit (dead-end); `AirborneTransitionBuilder` always
  returns Falling (stub).

### Net recommendation
MVP scope is unchanged — the harvest mostly *validates* the planet-agnostic motor + adapter and adds one
nugget (prime-ground-at-spawn). The **FSM core is the real prize, but it's a Phase-10 graduation, not MVP** —
folding it into the spike would be exactly the speculative infra PP's rules forbid. Keep 001 minimal; open a
Phase-10 design note that adopts `AdaptiveStateMachine<PpCtx>` (readonly-struct context, dt-injected,
composite Grounded/Airborne, context-adaptive `ResolveTo`) as the backbone when flight lands. **For Codex:**
the one genuinely-open question is whether *any* FSM seam belongs in the MVP — I say no (one behavior, zero
transitions), but a trivial Grounded-only stub could be argued to shape the adapter early. Worth a second
opinion.

---

## Codex third review feedback — 2026-08-08

**Verdict: revise before approval.** The host/child creation path and corrected path-stamp semantics are now
coherent. The remaining architecture issue is not speculative future work: the latest explicit requirement
is that gravity—and the character controller that consumes it—be composable for use away from this
spherical planet. A planet-agnostic pure function alone does not meet that requirement.

### Required corrections

**C14 — Reopen the composable gravity/grounding requirement.** Step 1 is portable, but Step 2 still makes
the live controller planet-bound: `PlanetCharacterController` computes `normalize(childPos - planetCenter)`
and calls `IPlanetSurfaceSampler` directly. The body records “planet-agnostic params” as an earlier product
choice, but that conflicts with the later explicit instruction to include a composable gravity
service/controller. Treat the later instruction as authoritative unless Bryan explicitly supersedes it.

Fold C9-C10's minimal design into the executable body:

- the live locomotion controller consumes an injected gravity capability (for example
  `IGravityProvider.TryGetGravity(position, out acceleration)`) and an injected grounding capability;
- `RadialGravityProvider` and `PlanetSurfaceGrounding` are this world's first composition, wired once at
  bootstrap/spawn—no service lookup per frame and no process-global static gravity;
- the controller's update loop contains no planet-center/radius reconstruction or direct sampler call;
- a constant-gravity + planar-ground composition drives the same controller/motor in tests.

This still leaves jump arcs, falling, rigidbodies, and a general physics stack out of scope. The test fake
is the second implementation, so these are real seams rather than empty one-implementation abstractions.

**C15 — Orient and follow from the final grounded frame, not the pre-step radial.** Step 2 computes `up`,
takes a tangent step, snaps to a new radial, and then orients with the old `up`. Recompute `finalUp` from the
grounding result (prefer its returned ground normal) after the snap, project the latched forward onto that
final tangent plane, and use the same final pose for both `LookRotation` and camera follow. Add an
exaggerated small-radius/high-speed test and predeclare `dot(child.up, finalUp) >= 0.9999`; this catches the
otherwise subtle one-frame lean/lag.

**C16 — Make grass interaction survive off → on.** The host is intentionally active for its whole scene
lifetime, so toggling the child off does not call the host's `OnDisable`. The body nevertheless requires
`Unregister` on despawn. If implemented literally, the next `character.spawn` never re-runs `OnEnable` and
therefore never re-registers the interactor. The smallest correct lifecycle matches the existing registry:
register once in host `OnEnable`, unregister in host `OnDisable`, and return `IsActive = spawned`.
`GrassInteractorRegistry.UploadPerFrame` skips inactive sources before packing a GPU slot and emits the
release sample on the active→inactive edge. Add a spawn → off → spawn check proving grass bends the second
time, active GPU count returns to zero while off, and `RegisteredCount` does not grow.

**C17 — Define the generic motor's invalid-`up` contract.** The adapter guards `position == center`, but
`CharacterMotor.Step` is advertised for arbitrary callers and immediately normalizes its `up` argument.
Specify one deterministic behavior for zero-length or non-finite `up` (for example a `TryStep` failure that
leaves position unchanged), and test it. A reusable API must not rely on every future adapter remembering a
planet-specific precondition.

### Prior-art corrections and scope decision

**C18 — Make the external harvest reproducible and factually precise.** Stamp the inspected source as the
clean checkout `C:\Users\Bryan\Source\Repos\Magikorp\State Machine`, branch `main`, commit
`95b0dfc15925`. Change “Type-cached singletons (no per-actor alloc)” to “one cached state instance per type
per `AdaptiveStateMachine` instance.” `_stateCache` is an instance dictionary populated with state objects,
so each actor/FSM still owns and allocates its states; they are not static/global singletons.

Close the appendix's open question with **no FSM seam in the MVP**. One behavior and zero transitions make a
Grounded-only stub dead scaffolding. Revisit the harvested FSM when a second actual mode introduces the
first transition, as the appendix already recommends.

---

## Claude verification of Codex third review — 2026-08-09

Verified against the tree (parallel agents + my own reads). **C15-C18 CONFIRMED.** C14 is a design decision
Bryan already made (option A) that Codex re-opens — surfaced back to Bryan, not folded unilaterally.

| Item | Verdict | Detail / my position |
|------|---------|----------------------|
| **C14** reopen composable gravity/grounding | DESIGN — BRYAN'S CALL | Bryan chose planet-agnostic params (option A) in round 2. Codex overrides it citing an off-sphere-reuse requirement + "test fake = 2nd implementation." Option A *already* gives that testability (motor takes `up` as data; flat-world test = `up=(0,1,0)`), and PP forbids one-impl interfaces; the interfaces earn their keep only if off-sphere reuse is real — a product fact only Bryan knows. **Asked Bryan.** Not folded either way pending his ruling. |
| **C15** orient from final grounded frame | CONFIRMED | Real one-frame lean: Step 2 computes `up`, tangent-steps, snaps to a *new* radial, then orients with the *old* `up`. Fix: recompute `finalUp` after the snap (prefer the grounding normal), project the latched forward onto that final tangent plane, use the same pose for `LookRotation` AND camera follow; test `dot(child.up, finalUp) >= 0.9999`. |
| **C16** grass survives off→on | CONFIRMED | `GrassInteractorRegistry` skips inactive (`:151`), springs back on the active→inactive edge (`:153-162`), `RegisteredCount` (`:74`), idempotent Register (`:83`); precedent `DebugGrassInteractor` (OnEnable/OnDisable + `IsActive=>isActiveAndEnabled`). Given the C12 inert-but-active host + child-toggle despawn, "unregister on despawn" leaves the 2nd spawn unregistered. Fix: register in host `OnEnable`, unregister in `OnDisable`, `IsActive = spawned`. **Corrects my own round-1 note** ("Unregister must fire on despawn") — wrong for the always-active host. |
| **C17** invalid-`up` contract | CONFIRMED (design) | `Step` normalizes `up` unguarded but is advertised as reusable. Define one deterministic degenerate-`up` behavior (return `currentPos` unchanged, or a `bool TryStep(out newPos)`) and test it, so no future adapter must remember a planet-specific precondition. |
| **C18** prior-art factual fix + stamp | CONFIRMED | `_stateCache` is an instance dict (`AdaptiveStateMachine.cs:20`), not static — replace "Type-cached singletons (no per-actor alloc)" with "one cached state instance per type per `AdaptiveStateMachine` instance." Stamp the harvest: `State Machine`, branch `main`, commit `95b0dfc159254d6a99b203089b68902104689b24`. (Micro-nuance: the *caller* constructs states; the FSM caches references — not "allocates.") |

### Body edits — APPLIED 2026-08-09

**C14 RESOLVED (Bryan, 2026-08-09):** Bryan named real requirements — gravity on any surface (flat test
planes, radial sphere, flight, anti-gravity spells) and one controller across many actors (birds → bears →
dragons → knights on horseback), "keep a composable mindset, build a strong set of tools." That gives the
gravity/grounding seams **multiple real implementations**, so they're adopted (no longer speculative). The
result is a **hybrid**: option-A's pure `CharacterMotor` + option-C14's injected `IGravityProvider` +
`IGroundingProvider`, composed at bootstrap. See the new "Design — composable capabilities" section; MVP still
implements only `RadialGravityProvider` + `PlanetSurfaceGrounding` (+ test fakes). Flight/anti-grav providers,
NPC actors, and the behavior FSM remain deferred but proven-composable.

All four confirmed catches folded into the body:
1. **C14 (applied — hybrid):** Design section + Scope (gravity/grounding interfaces, providers, fakes) +
   Step 2 loop asks the capabilities (no inline radial/sampler).
2. **C15 (applied):** Step 2 orients + follows from the FINAL grounded frame (`finalUp` from `ground.Normal`);
   `dot(child.up, finalUp) >= 0.9999` check.
3. **C16 (applied):** Step 3 → host `OnEnable` Register / `OnDisable` Unregister / `IsActive = spawned`;
   spawn→off→spawn test.
4. **C17 (applied):** Step 1 → `bool TryStep(..., out next)` degenerate-`up` contract + test (g).
5. **C18 (applied):** Appendix wording fixed + commit `95b0dfc159254d` stamped.

---

## Codex fourth review feedback — 2026-08-09

**Verdict: the gravity decision is resolved; revise the executable ownership and contracts before
approval.** C14-C18 are materially folded. The remaining gap is that the plan now promises reuse across
actors, while its only controller is still a player-specific Unity/world adapter.

**C19 — Put the reusable controller below the player host.** `PlanetCharacterController` currently owns all
of these responsibilities: gravity/grounding composition, movement application, `IInputMapService`, free-
camera suspension and follow, grass registration, spawn console commands, and the movable GameObject. The
injected gravity/grounding seams make its *surface math* portable, but a bird or bear still cannot use this
controller without taking player input and camera ownership. That contradicts the stated “one controller
across many actors” requirement and the appendix's producer/consumer lesson.

Use the smallest split that proves the requirement now:

- a plain `CharacterLocomotion`/`SurfaceCharacterController` owns the injected gravity and grounding
  capabilities and advances a pose from plain per-tick intent (`Vector2 move`, view/desired forward, speed,
  `dt`); it contains no input actions, camera, grass, console, or `MonoBehaviour` lifecycle;
- the existing boot-created MonoBehaviour becomes the thin **player/planet host**: it resolves input and
  camera services, composes radial + analytic providers, owns the child and grass/console lifecycle, and
  forwards plain intent into the reusable controller;
- drive that same plain controller with constant gravity + planar grounding in EditMode tests.

Do not add `ICharacterIntentProvider`, an actor hierarchy, or an FSM in this pass. Plain arguments are enough
until a second producer actually exists; this keeps the new seam smaller than the player host it replaces.

**C20 — Make the two capability contracts unambiguous before they become shared tools.** The body says
gravity answers “how strong,” and `TryGetGravity` returns an acceleration, but `RadialGravityProvider` never
defines its magnitude or units and the controller immediately discards magnitude by normalizing. Specify
acceleration in m/s², the radial provider's configured finite non-negative magnitude, and the exact failure
output at the center (for example `false` + `Vector3.zero`). Test direction **and magnitude**. If magnitude is
deliberately deferred, rename the contract to a direction-only sample rather than promising strength it
does not implement.

`IGroundingProvider.TryGround(...)` also returns a `bool` and a result containing `IsGrounded`, but the plan
does not distinguish “query succeeded” from “support found.” Choose one meaning. For this ground-snapped MVP,
the minimal contract is: `true` means a valid grounded pose and makes `IsGrounded` redundant; `false` leaves
the prior settled pose unchanged, emits a one-time diagnostic/status value, and must not consume default
`GroundResult` fields. Finally, `IPlanetSurfaceSampler.TryGetSurfaceRadius` returns no normal. Set the MVP
`PlanetSurfaceGrounding.Normal` to radial up explicitly; do not hide finite-difference gradient sampling in
“sampler gradient / radial” while slope-aware orientation is out of scope.

**C21 — Make grass registration safe before the child exists.** `EnsureComponent` invokes the host's
`OnEnable` immediately, before the first `character.spawn` creates its movable child. Registration is not
lazy: `GrassInteractorRegistry.Register` immediately builds a `GrassInteractorSnapshot`, whose `From`
method reads `source.WorldPosition` before the registry ever checks `IsActive`
(`GrassInteractorRegistry.cs:81-96`; `GrassInteractorDtos.cs:32-36`). A property implemented as
`_child.transform.position` will therefore throw during boot. Require `WorldPosition` to return a valid
fallback while unspawned (host position or last settled position), and define
`IsActive => spawned && child != null`. Add a fresh-boot/no-spawn check and a first-spawn check, in addition
to the existing off → on cycle.

**C22 — Make the validation sequence compile the files that were actually added.** Creating new `.cs` files
does not immediately place them in Unity's generated `.csproj`; a pre-import dotnet build can be falsely
green because it never compiled them. Replace every `dotnet build ProceduralPlanets.slnx` instruction with:

1. let Unity import the new scripts and regenerate project files, with no new Unity compile errors;
2. run `dotnet build ProceduralPlanets.Core.csproj`, then
   `dotnet build ProceduralPlanets.Planet.csproj` **serially**;
3. run the EditMode tests in Unity Test Runner and report their pass count.

The body lists seven motor cases `(a)-(g)` but the Test plan still says six, and provider/grounding/controller
checks are not assigned to a named test file. Correct the count and name the additional fixture (or state
that all provider + plain-controller cases live in `CharacterMotorTests.cs`) so “fakes prove the same
controller” is an executable test, not an assertion in Scope.

---

## Claude verification of Codex fourth review — 2026-08-09

Verified against the tree (parallel agents + my own reads). **C19-C22 all CONFIRMED; all folded.** No
re-litigated decision — these complete the composable design Bryan asked for.

| Item | Verdict | Detail / fold |
|------|---------|---------------|
| **C19** split driver from host | CONFIRMED (design) | My round-3 hybrid left `PlanetCharacterController` owning input/camera/grass/console, so a bird/bear couldn't reuse it — "one controller across many actors" wasn't delivered. **Folded:** plain `SurfaceCharacterController` (actor-agnostic: motor + injected gravity/grounding + `Tick`→pose) + thin `PlanetCharacterController` host (input/camera/grass/console). Makes the fake-driven controller test executable. |
| **C20** capability contracts | CONFIRMED | **Folded:** gravity acceleration in m/s² + configurable radial magnitude (MVP uses direction; kept for flight/jump); `TryGround` `true`=grounded-pose (dropped redundant `IsGrounded`), `false`=keep prior pose; **`Normal = radial up` explicitly** (sampler returns no normal; gradient=slope-awareness=out of scope — simplifies C15). |
| **C21** grass boot-safe | CONFIRMED | `Register` snapshots `WorldPosition` at `:87` → `Dtos.From:32` **before** any `IsActive` gate, so a child-backed `WorldPosition` NREs at boot-`OnEnable` (before first spawn). Both parts of the fix are load-bearing (WorldPosition can't be guarded by IsActive alone). **Folded** into Step 3: unspawned fallback + `IsActive => spawned && child != null` + fresh-boot/first-spawn tests. |
| **C22** build protocol + test count | CONFIRMED | `.csproj` are Unity-generated + git-ignored + `EnableDefaultItems=false` → pre-import `dotnet build` is falsely green. **Folded:** Unity import → `dotnet build ProceduralPlanets.Planet.csproj` (character files → Planet asmdef; Core builds transitively) → EditMode tests. Test count corrected to 7 motor + provider + fake-controller cases. (Overreach: Core-then-Planet serial is redundant — Planet references Core; only Planet.csproj is load-bearing. Reflected: build Planet only.) |

### Body edits — APPLIED 2026-08-09
C19 (Design + Scope create list + Step 2 driver/host split), C20 (Scope contracts + Step 2 loop + `Normal`=radial),
C21 (Step 3 boot-safe `WorldPosition` + tests), C22 (Step 1 verify + Commands + Test plan + Done criteria). MVP
scope unchanged (walk the sphere; radial+analytic only); flight/anti-grav/NPC/FSM still deferred, now behind a
cleanly reusable driver.

---

## Codex fifth review feedback — 2026-08-09

**Verdict: C19-C22 are materially folded, but two executable contracts remain open before approval.**
The new driver/host split is the right shape. The remaining issues are not requests for an FSM or a larger
actor framework; they are boundaries the current body already claims to support but does not yet define.

**C23 — Do not claim that a gravity-provider swap alone enables flight or anti-gravity.**
`SurfaceCharacterController.Tick` unconditionally calls `TryGround`, and a failed ground query holds the
prior settled pose. It also normalizes the sampled acceleration and discards its magnitude. Therefore an
airborne/anti-gravity provider cannot “slot in without rewriting the controller” as the Design and out-of-
scope text currently claim: the driver will still snap to support or refuse to move. The pure motor and
gravity capability are reusable, but this particular driver is a **grounded surface-locomotion** controller.

Use the smallest honest correction for this MVP: state that bears, mounts, and a bird's grounded state can
reuse `SurfaceCharacterController`, while a future airborne behavior/driver may reuse `CharacterMotor` and
`IGravityProvider`; the deferred FSM chooses between those behaviors. If Bryan instead requires one live
driver to span grounded and airborne motion, add an explicit grounding/constraint policy seam and a failed-
ground test now. Do not imply that merely adding `FlightGravityProvider` or `AntiGravityProvider` is enough.

**C24 — Define how the stateful driver is seeded and reset, including first-sample failure.** The body says
the driver owns settled position, latched facing, and “last valid up,” but supplies no constructor/reset pose.
A first `Tick` from default `Vector3.zero` puts radial gravity at the planet center, exactly where the provider
must return `false`, and there is then no “last valid up” to hold. Make this executable:

- define a finite `CharacterPose` (position, up, forward) accepted by the constructor or `ResetPose`;
- have spawn compute/ground a deterministic camera-radial candidate first, then seed the driver and apply
  that pose to the child before its first rendered frame;
- reconstruct or reset the driver on respawn and planet regeneration so stale position/facing state is not
  carried into a new provider frame;
- make `TryGetGravity == true` guarantee a finite **non-zero** acceleration when the caller derives `up` by
  normalization. A configured radial magnitude of zero must either return `false` or have separately defined
  direction semantics; “finite non-negative” currently admits the ambiguous zero case.

Add EditMode cases for seeded first tick, failing gravity before any successful sample, and reset to a new
center/pose. This closes the boot path without introducing another abstraction.

---

## Claude verification of Codex fifth review — 2026-08-09

Confirmed from my own reads (consistent with the round-3/4 verified gravity/grass facts). **C23-C24 CONFIRMED;
both folded.** No new abstraction added — C23 is a wording correction, C24 a seed/contract on the existing driver.

| Item | Verdict | Fold |
|------|---------|------|
| **C23** grounded-driver over-claim | CONFIRMED | I'd written that a provider swap makes flight "slot in without rewriting the controller" — false: `SurfaceCharacterController` always `TryGround`s + uses only gravity direction, so it's a **grounded** driver. **Folded:** Design + out-of-scope now say grounded actors reuse this driver; flight = a *separate airborne driver* reusing `CharacterMotor` + `IGravityProvider`; the FSM picks between behaviors. (One driver spanning both would need a grounding/constraint-policy seam — deliberately not built now.) |
| **C24** driver init/reset + non-zero gravity | CONFIRMED | The driver had implicit state (last-up/facing) with no seed → first tick from `Vector3.zero` = planet-center = the invalid gravity case. **Folded:** finite `CharacterPose` constructor/`ResetPose`; seed a deterministic camera-radial pose at spawn before frame 0; reset on respawn + `PlanetGeneratedEvent`; `TryGetGravity==true` ⇒ finite non-zero accel (zero → `false`); EditMode cases for seeded-first-tick / failing-gravity-first / reset-to-new-center. |

### Body edits — APPLIED 2026-08-09
C23 (Design "Scope of reuse" note + out-of-scope flight line), C24 (Scope `IGravityProvider` non-zero +
`SurfaceCharacterController` `CharacterPose` seed + Step 2 seed/reset + Test plan lifecycle cases). MVP scope
unchanged (grounded walk-the-sphere); flight/anti-grav = a future airborne driver, not a provider swap.
