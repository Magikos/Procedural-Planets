---
name: pp-run-and-operate
description: Use when you need to run or operate the game — enter play mode, open/use the debug console, drive the free camera, teleport to a saved viewpoint, trigger an F10 capture, batch commands with a console script, or find where a screenshot/log/artifact landed. Also for phrasing "ask Bryan to run X" requests and interpreting what comes back. Not for interpreting measurements or choosing debug modes — see pp-diagnostics-and-tooling. Not for Unity/dotnet environment setup — see pp-build-and-env.
---

# Running and operating ProceduralPlanets

Everything here is verified against code on branch `code-refactor` as of 2026-07-06. Repo root: `c:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets`.

**Agents cannot run Unity.** Play mode, captures, and visual checks are executed by Bryan. Your job is to (a) hand him an exact, paste-ready operation sequence and (b) interpret the artifacts that come back (screenshots + `.txt` sidecars in `local-only/debug-screenshots`). Build success (`dotnet build`) is a code-health check only — never claim runtime or visual correctness without in-game evidence (see pp-validation-and-evidence).

## The "ask Bryan to run X" protocol

Phrase every run request as an exact sequence, not a description. Bryan pastes console commands verbatim, so:

1. State the goal in one line ("prove the cloud change didn't alter the Off baseline").
2. Give the exact steps as a numbered list: keys to press and console commands in code spans, in order.
3. Say what artifact you expect back and where it lands (usually "N PNGs + sidecars in `local-only/debug-screenshots`").
4. If the sequence is > ~5 commands or must be repeatable for before/after comparison, write it as a console script file in `Assets/Resources/ConsoleScripts/` instead (format below) and ask Bryan to run `script.run "<Name>"`. Scripts pin camera, lighting, and settings, so before/after captures are actually comparable.

Example request:

> Goal: capture the Cloud Diagnostics set at noon from the saved seam viewpoint.
> 1. Enter play mode, wait for the planet to finish loading.
> 2. Press `` ` `` to open the console, then paste:
>    - `camera.teleport "Grass Face Seam A"`
>    - `time.set-local 0.5`
>    - `time.freeze true`
>    - `debug.capture-set "Cloud Diagnostics"`
>    - `debug.capture`
> 3. Expect 9 PNG+txt pairs prefixed `F10-cloud.*` in `local-only/debug-screenshots`.

`debug.capture` closes the console during the capture so it stays out of the screenshots, then reopens it (`DebugCaptureController.CaptureCmd`). Pressing F10 with the console closed does the same capture without the console dance.

## Run checklist from a cold editor

1. Open the project in Unity **6000.6.0a7** (see pp-build-and-env for install traps).
2. Open `Assets/Scenes/Planet.unity` — the only scene in build settings (`ProjectSettings/EditorBuildSettings.asset`, index 0).
3. Press Play. A loading overlay paints first, then the planet generates. Wait for the overlay to fade before issuing commands.
4. Sanity check: press F6 — a debug HUD appears. Press `` ` `` — the console opens; type `help` for the full command list, `planet.status` for the active recipe.
5. If the console won't open, `IInputMapService` isn't registered yet (a warning is logged by `ConsoleController`); the boot path hasn't finished or failed — check the Editor console / Editor.log.

### What happens at play (one paragraph)

`LoadingManager.CreateInstance` is the single sanctioned `RuntimeInitializeOnLoadMethod` (BeforeSceneLoad): it spawns `[LoadingManager]`, registers `ILoadingManager`, activates a `WorldContext`, and shows the loading overlay before anything else runs. It then sweeps the scene for `IEarlyInitialize` components, orders them by dependency graph + priority, and awaits each; `GameBootstrap.EarlyInitialize` (priority 100) creates `InputMapService` (all keybindings live there, built in code — there is no `.inputactions` asset to edit) and ensures the global MonoBehaviours exist (`ShaderGlobalsController`, `QualityController`, `DebugInputRelay`, `DebugCaptureController`, `SurfacePathMousePainter`) and initializes the console registry. `ILateInitialize` components run next, then `EventBus<WorldReadyEvent>` fires and the overlay fades. Full boot contract: pp-architecture-contract.

## Console anatomy

Custom in-game console (`Assets/Scripts/Core/Console/`), rendered as a URP overlay. **Backquote (`` ` ``) opens it; backquote or Escape closes it.** While open, the Gameplay input map is disabled (camera stops responding) and the Console map is enabled — verified in `ConsoleController.Open()`/`Close()`.

Command shape: `prefix.command arg1 arg2 …`. Commands are methods tagged `[ConsoleCommand]` inside a class tagged `[CommandPrefix("prefix")]`, living on the service that owns the state. Most setters are get/set: no argument reads the current value, an argument sets it (e.g. `camera.speed` prints, `camera.speed 25` sets). Strings with spaces are quoted: `debug.capture-set "Cloud Diagnostics"`.

| Console feature | How it works (verified in `ConsoleInputController.cs` / `ConsoleBuiltins.cs`) |
|---|---|
| Intellisense | Suggestion popup as you type; Up/Down navigate, Tab accepts; single exact-prefix match shows inline "ghost" completion |
| History | Up/Down when no popup is showing; draft text is preserved |
| Help | `help` lists all commands; `help planet.generate` describes one; `help grass` lists all `grass.*` |
| Async commands | Long commands show a spinner; `console.abandon` stops tracking, `console.cancel` cancels (needs a CancellationToken-aware command) |
| Built-ins (no prefix) | `help`, `clear`, `echo`, `quit`, `console.anchor`, `console.scrollback-size` |
| Programmatic runs | Everything Unity logs is mirrored into the scrollback (`Application.logMessageReceived`) |

As of 2026-07-06 there are 159 commands under 20 prefixes (`grep -rc "\[ConsoleCommand(" Assets/Scripts` to recount).

### Prefix table (all 20, verified via `grep -rn "CommandPrefix(" Assets/Scripts`)

| Prefix | Owner (file) | One-line scope |
|---|---|---|
| `action` | `Core/Services/Commands/ActionCommands.cs` | Action history utilities |
| `atmosphere` | `Planet/Atmosphere/AtmosphereController.cs` | Atmosphere params/toggles |
| `camera` | `Core/Services/FreeCameraController.cs` + `CameraTeleportStore.cs` | Speed, sensitivity, position, look-at, teleports |
| `climate` | `Planet/Biomes/ClimateCommands.cs` | Climate/biome map queries |
| `cloud` | `Planet/Clouds/CloudController.cs` | Cloud rendering/evolution knobs |
| `debug` | `Core/Services/DebugCaptureController.cs` | Overlay, debug modes, capture sets, `debug.capture` |
| `grass` | `Planet/Grass/GrassInteractorCommands.cs`, `Planet/PlanetGrassCoordinator.cs`, `Core/Services/GrassDebugModule.cs` | Grass layers, placement, diagnostics |
| `light` | `Planet/LightingDebugCommands.cs` | Sun/lighting state (`light.local-noon`, `light.status`) |
| `lightning` | `Planet/WeatherLightningController.cs` | Lightning triggers/params |
| `path` | `Planet/SurfacePathDebugCommands.cs` | Path wear painting/persistence |
| `planet` | `Planet/Planet.cs` | `status`, `seed`, `resolution`, `generate` (async regenerate) |
| `precipitation` | `Planet/PrecipitationController.cs` | Rain/snow systems |
| `quality` | `Core/QualityController.cs` | `get`, `list`, `set <index>`, `cloud-steps <0.33-1>` |
| `rain-particles` | `Planet/Precipitation/RainParticleController.cs` | Local rain particle knobs |
| `scale` | `Core/Services/ScaleReferenceMarkers.cs` | `drop`, `clear`, `teleport` — 1 m/1.8 m/3 m/10 m/30 m size markers |
| `scorch` | `Planet/SurfacePathDebugCommands.cs` (second class) | Scorch stamp painting/clearing |
| `script` | `Core/Console/Scripting/ConsoleScriptRunner.cs` | `list`, `run <name>` (alias `run-script`) |
| `test.console` | `Core/Console/Commands/TestConsoleCommands.cs` | Console self-tests: colors, async, enum, error paths |
| `time` | `Planet/CelestialManager.cs` | `freeze`, `speed`, `set-local <0-1>` (0.25=sunrise, 0.5=noon), `moon-phase` |
| `weather` | `Planet/WeatherManager.cs` | Weather grid state/overrides |

Note: `planet.seed` and `planet.resolution` do **not** auto-regenerate — run `planet.generate` afterwards (stated in their own command descriptions).

### Console scripts (`script.run`) — the batching tool

Scripts are plain-text `TextAsset`s in `Assets/Resources/ConsoleScripts/` (23 exist as of 2026-07-06, mostly grass/path/scorch probes). `script.list` enumerates them; `script.run "Grass Baseline Sweep"` runs one. Parser: `ConsoleScriptRunner.cs`.

Format — one console command per line, plus directives:

```
@name Grass Baseline Sweep        # display name (else the asset filename)
@timeout 180                      # per-command timeout in seconds (default 120)
@defer grass.enabled true         # runs at script end (reverse order), even on failure
@sidecar camera.position          # extra command whose output is embedded in each capture sidecar
@wait frames 5                    # or: @wait seconds 2.5
# comment lines start with #
debug.capture-set "Grass"
camera.look-at -114.86,164.22,-3400.39 -186.06,129.22,-2403.59
debug.capture
```

A failed command aborts the script (defers still run). Any F10/`debug.capture` taken while a script runs gets the file prefix `F10-<script>-<runid>-stepNNN-…` and a `--- Console Script ---` block in the sidecar (script name, run id, step, command) — this is how capture evidence is tied to the exact script step that produced it (`ConsoleScriptRuntime.GetCaptureFilePrefix` / `AppendMetadata`).

## Camera and navigation

Free camera (`Core/Services/FreeCameraController.cs`) with two modes: **orbit view** (far, fast) and **surface view** (near ground, slow); Space toggles between them and `camera.surface-view true|false` does the same. Speed auto-scales to planet radius on mode change; override with `camera.speed <n>`.

### Keybinding table (all verified in `Core/Services/InputMapService.cs`; bindings are code-built, not asset-authored)

| Key | Action |
|---|---|
| W/A/S/D or arrows | Move |
| E / Q | Up / down |
| Z / C | Roll |
| Right-mouse hold + move | Look (cursor locks) |
| Shift (either) | Sprint (`camera.fast-multiplier`, default 3x) |
| Space | Toggle orbit <-> surface view |
| Backspace | Face the sun |
| R | Frame the strongest storm |
| M / Shift+M | Drop / clear scale reference markers |
| T | Teleport to last marker chain |
| `[` / `]` | Grass interactor distance |
| F6 | Toggle debug overlay HUD |
| F7 | Cycle F10 capture set |
| F8 | Freeze/unfreeze sun (time) |
| F9 | Toggle detailed debug + dump weather diagnostics |
| F10 | Trigger capture of current capture set |
| F11 | Toggle high-FPS profiling target (uncaps to 1000, disables vsync) |
| F12 | Dump atmosphere diagnostics |
| `` ` `` (backquote) | Open/close console |
| P | Toggle the **path paint brush** (`SurfacePathMousePainter`), NOT precipitation |

Gotcha (verified 2026-07-06): the `TogglePrecipitation` input action exists but has **no keyboard binding** in `InputMapService`; the HUD hint "P=Precip" (`DebugOverlayHud.cs:71`) and the `debug.precipitation` description "(P key equivalent)" are stale. Use the console command `debug.precipitation`. P actually toggles the path mouse painter (`SurfacePathMousePainter.cs:102`).

F-keys are read by `DebugInputRelay` (created by `GameBootstrap`) and routed via `EventBus<DebugCommandRequestedEvent>` to `DebugCaptureController` and friends.

### Camera teleports (saved viewpoints)

Owned by `Core/Services/CameraTeleportStore.cs`, commands under `camera.*`:

- `camera.teleports` — list all names.
- `camera.teleport "<name>"` — go there. `camera.teleport LastDebugCapture` jumps to the pose of the most recent F10 capture — the standard way to re-shoot the same view. If no in-memory pose exists, it is **imported from the newest `F10-*.txt` sidecar** in `local-only/debug-screenshots` (Position/Forward/Surface-view lines).
- `camera.save-teleport "<name>"` / `camera.remove-teleport "<name>"` — manage saved poses.
- Persistence is **PlayerPrefs** (key `CameraTeleportLocations.v1`, max 64), not a repo file. Built-ins as of 2026-07-06: `Grass Face Seam A`, `Grass Face Seam B`, `Terrain Texture Oblique` (planet-relative, radius 5293.44).
- `camera.look-at x,y,z tx,ty,tz` — exact deterministic pose; preferred inside scripts (see the Grass Baseline Sweep example) because it doesn't depend on anyone's PlayerPrefs.

Scale markers (`scale.drop` or M) place 1 m / 1.8 m human / 3 m / 10 m / 30 m reference shapes at the camera look target — use them whenever a screenshot needs a size anchor.

## F10 capture workflow, end to end

Terms: a **debug mode** is a named visualization state (e.g. water `VolumeOnly`, cloud `Density`) registered by a per-domain debug module; a **capture set** is a named ordered list of debug modes captured in one run. `DebugRegistry` owns both; `DebugCapturePipeline` executes the run.

1. `debug.capture-set "<Set Name>"` selects the set (F7 cycles; `debug.capture-set` with no arg prints the current one; names are case-insensitive with completion).
2. Press **F10** (or run `debug.capture`, which hides the console first).
3. The pipeline records the camera pose as `LastDebugCapture`, then for each mode: applies the mode, waits ~2 frames + 0.12 s to render, and saves a screenshot. Timed sets (the `Performance *` ones, 60 samples/mode) additionally freeze the sun at local time 0.5, uncap FPS, and collect frame-timing samples per mode before shooting. Everything (mode, FPS cap, vsync, sun time/freeze, cloud step globals) is restored afterwards; the mode is reset to Off.
4. Output per mode, **flat files** (no per-run subfolders) in `local-only/debug-screenshots/` (gitignored):
   - `F10-{modeId}-{modeName}-{yyyyMMdd-HHmmss-fff}.png` — downsampled to max width 960.
   - Same basename `.txt` — the metadata sidecar.
   - Real example from disk: `F10-cloud.00-Off-20260703-091039-572.png`. During a console script the script prefix is inserted: `F10-{script}-{runid}-step{NNN}-{modeId}-…`.
5. **Pruning**: only the newest `MaxCaptureRuns = 6` runs' worth of files are kept (`6 × modes-in-set × 2` files, sorted by write time — `DebugCapturePipeline.SaveScreenshot` + `DebugScreenshotFiles.Prune`). Copy anything worth keeping out of the folder before it rotates away. (There is no `DebugScreenshotMaxRuns` symbol; the constant is `MaxCaptureRuns` in `DebugCapturePipeline.cs`.)

### Reading the sidecar (`DebugCaptureMetadataBuilder.cs`)

Each `.txt` contains, in order: image path + source/saved resolution + mode + capture set + ISO timestamp; console-script block (if any); `--- Camera ---` (position, forward/up/right, projection + frustum planes, surface-view flag, lat/lon, distance-to-center, planet/sea radii, elevation min/max); `--- Runtime ---` (FPS, frame target, vsync, weather-suppression flag, quality level + cloud tier/step multiplier, sun frozen, sun direction/intensity, precipitation state, wind, camera-position temperature); then per-module diagnostic and metadata blocks. The sidecar is the ground truth for "what state produced this pixel" — read it before interpreting any PNG.

### Capture set names (verified registrations, 2026-07-06)

Core (`DebugRegistry.RegisterCoreCaptureSets`): `Performance Baseline`, `Performance Water Isolation`, `Performance Water Volume Stages`, `Performance Weather Stages`, `Performance Cloud Steps` (all timed), `Current Mode Only`, `Full Loop`.

Module-registered: `Biome`, `Cloud Diagnostics`, `Grass`, `Grass Visual`, `Terrain Geography`, `Terrain Textures`, and the water family: `Water Artifact`, `Water/Atmosphere`, `Water Interface`, `Water Precipitation`, `Water Glint`, `Frozen Water`, `Water Caustics`, `Water Foam`, `Water Waves`, `Water Surface Finish`, `Water Surface Isolation`, `Water Night`, `Water Wakes`, `Water Volume Deep Dive`.

Which set to choose for a given investigation: pp-diagnostics-and-tooling.

## Artifact map — what lands where

| Artifact | Location | Produced by |
|---|---|---|
| F10 screenshots + sidecars | `local-only/debug-screenshots/F10-*.png` / `.txt` | `DebugCapturePipeline.SaveScreenshot` (F10 / `debug.capture`) |
| Camera teleports | PlayerPrefs key `CameraTeleportLocations.v1` | `CameraTeleportStore` (`camera.save-teleport`) |
| Console scripts (inputs) | `Assets/Resources/ConsoleScripts/*.txt` | Authored by hand; run via `script.run` |
| Editor log | `%LOCALAPPDATA%\Unity\Editor\Editor.log` | Unity editor (standard location) |
| Player log (built player) | `%USERPROFILE%\AppData\LocalLow\Magikorp\ProceduralPlanets\Player.log` | Unity player (company/product verified in `ProjectSettings.asset`) |
| Knowledge graph | `graphify-out/` | `graphify update .` after code changes |
| Reference dumps, papers, exports | `local-only/` (gitignored, line 107 of `.gitignore`) | Manual; background only — never a load-bearing source |

`ProfilerCaptures/`: exists at repo root but is **empty and referenced by no code** as of 2026-07-06 — a manual Unity Profiler dump location; nothing writes to it automatically. Frame timing evidence comes from the timed capture sets (`FrameTimingCounters`, surfaced in sidecars) — see pp-diagnostics-and-tooling.

## Time and quality quick reference

- `time.freeze` / `time.freeze true|false` — read/set sun freeze (F8 equivalent).
- `time.set-local 0.5` — noon **at the camera's position**; 0.25 sunrise, 0.75 sunset. `light.local-noon` is the lighting-side shortcut used in scripts.
- `time.speed <seconds>` — day length in real seconds.
- `quality.list` then `quality.set <index>`; `quality.get` prints tier + cloud multiplier; `quality.cloud-steps <0.33-1>` tunes cloud raymarch cost (reset by `quality.set`).
- `debug.profiling` (F11) — uncap FPS for timing; `debug.overlay` (F6) — HUD on/off.

## When NOT to use this

- **Choosing what to measure or interpreting counters/frame timings/debug modes** → pp-diagnostics-and-tooling.
- **What counts as evidence, before/after capture protocol** → pp-validation-and-evidence.
- **Installing Unity, csproj builds, asmdefs, graphify setup** → pp-build-and-env.
- **Boot/init architecture in depth (init graph, WorldContext, settings DTOs)** → pp-architecture-contract.
- **Symptom-driven debugging once something looks wrong** → pp-debugging-playbook.
- **Whether you're allowed to change/tune what you're looking at** → pp-change-control.

## Provenance and maintenance

All claims verified 2026-07-06 by reading the cited files on branch `code-refactor`. Re-verify with:

- Scene list: `cat ProjectSettings/EditorBuildSettings.asset`
- Prefixes (expect 20): `grep -rn "CommandPrefix(\"" Assets/Scripts | grep -o '"[^"]*"' | sort -u`
- Command count: `grep -rc "\[ConsoleCommand(" Assets/Scripts` (sum)
- Keybindings: `grep -n "AddBinding\|AddButton\|With(" Assets/Scripts/Core/Services/InputMapService.cs`
- Console toggle: `grep -n "OpenConsole\|CloseConsole" Assets/Scripts/Core/Console/ConsoleController.cs`
- Capture output dir: `grep -n "DebugScreenshotFolder" Assets/Scripts/Core/Services/DebugScreenshotFiles.cs`
- Capture naming + pruning: `grep -n "MaxCaptureRuns\|baseName\|yyyyMMdd" Assets/Scripts/Core/Services/DebugCapturePipeline.cs`
- Capture set names: `grep -rn "RegisterCaptureSet(\|RegisterDefaultCaptureSet(\|RegisterTimedCaptureSet(" Assets/Scripts/Core/Services`
- Sidecar fields: `grep -n "AppendLine" Assets/Scripts/Core/Services/DebugCaptureMetadataBuilder.cs`
- Teleport persistence + built-ins: `grep -n "TeleportPlayerPrefsKey\|BuiltInTeleports" Assets/Scripts/Core/Services/CameraTeleportStore.cs`
- Script directives: `grep -n "case \"" Assets/Scripts/Core/Console/Scripting/ConsoleScriptRunner.cs`
- Script inventory: `ls "Assets/Resources/ConsoleScripts"`
- P-key gotcha still true?: `grep -n "TogglePrecipitation" Assets/Scripts/Core/Services/InputMapService.cs` (no `AddBinding` nearby = still unbound)
- ProfilerCaptures still empty/unreferenced?: `ls ProfilerCaptures` and `grep -rn "ProfilerCaptures" Assets/Scripts`

Additional background (not load-bearing): `.agent-memory/codex/skills/proceduralplanets-water-artifact-debug/SKILL.md` shows the capture workflow used in anger during the water-artifact investigation.
