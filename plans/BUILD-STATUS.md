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
