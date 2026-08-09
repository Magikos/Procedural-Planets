# Build status — character controller MVP (autonomous overnight, 2026-08-09)

Branch **`character-controller-mvp`** (off `main` @ `c54fc72`). Three commits, **not pushed**.

| Commit | What |
|--------|------|
| `2a50425` | `feat(character)`: reusable locomotion core (motor + gravity/grounding capabilities + driver) + 15 EditMode tests |
| `874208e` | `docs(plans)`: 001 + 002 + README (the 5-round review) + related memory |
| `75a9367` | `feat(character)`: Unity host + `character.spawn`/`despawn` + free-camera suspend |

## Verified (objective, autonomous)
- **Compiles clean** (0 errors) via Unity import + the Planet assembly.
- **78/78 EditMode tests pass**, including all 15 new `CharacterMotorTests` (motor a–g, providers,
  planet grounding, flat-world portability, sphere lean guard, and the C24 seed/reset/failing-gravity cases).
- **Runtime smoke in play-mode on a real generated planet (radius 5293):**
  - `character.spawn` works; capsule spawns **grounded** on the sampled surface, `up·radial = 1.0000`
    (final-frame radial orientation — no lean), collider stripped (no physics), pose finite.
  - Simulated ~1 s forward walk: moved **6.00 m** at 6 m/s, stayed grounded (radial distance ~constant,
    tracked terrain), up stayed radial, no NaN.
  - Grass interactor **registers boot-safe** (registered at `OnEnable` with no child yet — no NRE, C21
    validated live) and goes active on spawn.
  - Free camera **suspended** on spawn; third-person follow positions the camera behind the capsule.
  - `Update`/`LateUpdate` run every frame with **zero exceptions** (only pre-existing compute-shader warnings).

## What only YOU can verify (visual / input — needs a human)
1. `character.spawn`, then **WASD** — does it *feel* right walking around the sphere? (I injected the move
   programmatically; the keyboard path is identical but unexercised.)
2. Does grass **visibly** bend around the capsule and spring back?
3. Does the third-person camera *look* good (distance/height/lag)? Constants are guesses:
   `CameraDistance=6`, `CameraHeight=3`, `MoveSpeed=6` in `PlanetCharacterController.cs`.
4. `character.despawn` restores free-fly.

**Try it:** open console (`` ` ``), fly to land first (e.g. `scatter.goto Grassland`), then `character.spawn`.

## Known quirks / notes
- **Spawn location = camera aim.** It grounds on the *solid* surface under the camera. Over ocean that's the
  **ocean floor** (below sea level → underwater). Aim at land / `scatter.goto <Biome>` before spawning.
  Walking on water/swimming is future scope.
- **Plan deviation (worth your eyeball): Static command, not `EnsureComponent` at boot.** The 5-round plan
  settled on a `MonoTargetType.Single` command + `SceneBootstrap.EnsureComponent<PlanetCharacterController>()`
  at boot. That is **cross-assembly-impossible**: `SceneBootstrap` is in `ProceduralPlanets.Core`, which cannot
  reference the Planet-assembly host. So I used Codex's *other* sanctioned option — a **`MonoTargetType.Static`
  `character.spawn` that find-or-creates the host** (`CharacterCommands.cs`). The host is created on first
  spawn (a standalone GameObject), not at boot. This is the correct implementation given the assembly boundary;
  the review rounds never caught it because it's an implementation-time fact. If you'd rather the host be
  scene-placed/pre-existing, that's a scene edit — say the word.
- **Deferred:** foot-trail (optional Step 5), and any camera-feel/constant tuning (needs your eyes).
- **002 (terrain-relief) not started** — it is entirely interactive play-mode + visual A/B judgement, which
  needs you. Ready to run when you are.

## Suggested next
Play-test the walk; if it feels right, I'll tune camera/speed to your taste, add the foot-trail, and we run
002. If the assembly-boundary/static-command deviation bugs you, I'll switch the host to a scene-placed
component instead.

---

## Round 2 — play-test fixes (commit `97f00e8`, 2026-08-09)

Fixed the two issues from your first play-test, all **runtime-verified in play-mode**:
- **Underground spawn → fixed.** Root cause: it grounded on the *solid* surface, which over ocean is the
  sea floor (below sea level 5000, verified at 4974 = underwater). Now: spawn **raycasts along the camera
  forward** (spawn where you look) and grounding is **floored at sea level** — over ocean the character walks
  on the water surface (placeholder until swimming), never underwater. Verified: spawn dist **5001** (sea 5000).
- **A/D spin → fixed.** The driver faced the *travel* direction, so strafing turned it. Now the character
  **faces the look direction**; W/S walk along it, A/D strafe sideways. Verified: strafe `facing_dot = 1.0000`
  (no rotation), forward moves along facing.
- **Controls now fly-cam style + jump/crouch/sprint:** mouse looks (yaw + pitch), third-person camera follows,
  **W/S** fwd/back, **A/D** strafe, **Space** jump (verified airborne→land), **LeftShift** sprint (2×),
  **LeftCtrl** crouch (0.45× speed). Cursor locks while active, releases when the console is open.

**Try it now:** fly near land, `character.spawn`, then WASD + mouse. `character.despawn` to exit.

### Still yours to judge (feel/tuning — my constants are guesses)
`WalkSpeed=5`, `SprintMult=2`, `CrouchMult=0.45`, `LookSensitivity=0.12`, `CamDistance=5.5`, `MinPitch=-70`,
`MaxPitch=75`, `JumpHeight=1.6` (in `PlanetCharacterController.cs` / `SurfaceCharacterController.cs`). Tell me
what feels off and I'll dial it.

### Minor known items
- Crouch is speed-only (no capsule squash/height change yet).
- Host is created on first `spawn`, so it misses the *first* `PlanetGeneratedEvent`; it falls back to the
  camera-rig radius (works). A tidier version reads `IPlanet.LastGeneratedRadius` directly — low priority.

## Console-command overhaul (your other ask — separate task, noted)
You want the console reorganized **function-based** (not dev-feature-based) — e.g. `scatter.goto` really
"move the view to a location," so it belongs under `teleport`/`camera`, not `scatter`. That's a good cleanup
but a **separate focused pass** (touching many command classes + `[CommandPrefix]`s). I did NOT start it —
say the word and I'll survey all ~193 commands, propose a function-based taxonomy for your review, then move
them (keeping the old names as aliases during transition).
