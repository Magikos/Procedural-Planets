# 2026-06-03 — Debug Console Slice CONSOLE-0: Input System Migration

**Status:** Shipped. Awaiting Bryan validation (WASD camera move, F10 capture, M markers, Shift+M clear, T teleport).

**Design doc:** [docs/design/2026-06-03-debug-console.md](../design/2026-06-03-debug-console.md) section "Prerequisite — Slice CONSOLE-0".

**Goal of this slice:** Stand up the InputAction infrastructure that CONSOLE-1 needs in order to gate gameplay input when the console opens. Pure refactor — no behavioral change.

## Files

**New:**

- [Assets/Scripts/Core/Interfaces/IInputMapService.cs](../../Assets/Scripts/Core/Interfaces/IInputMapService.cs) — interface, 19 typed action accessors + map toggles
- [Assets/Scripts/Core/Services/InputMapService.cs](../../Assets/Scripts/Core/Services/InputMapService.cs) — implementation; builds the asset programmatically (no `.inputactions` JSON file or `.meta` to maintain)

**Modified:**

- [Assets/Scripts/Core/Services/GameBootstrap.cs](../../Assets/Scripts/Core/Services/GameBootstrap.cs) — registers `IInputMapService` in `EarlyInitialize`, disposes in `OnDestroy`
- [Assets/Scripts/Core/Services/FreeCameraController.cs](../../Assets/Scripts/Core/Services/FreeCameraController.cs) — reads from actions; legacy `Input.*` fallback blocks removed; `RefreshInputDevices` helper removed
- [Assets/Scripts/Core/Services/DebugInputRelay.cs](../../Assets/Scripts/Core/Services/DebugInputRelay.cs) — reads from actions; shift detection for `Shift+M` still uses `Keyboard.current.shiftKey.isPressed` (modifier check, not a gateable action)
- `ProceduralPlanets.Core.csproj` — added two new `<Compile Include=...>` entries for the new files (Unity will regenerate on its next Editor refresh; entries added manually so the headless `dotnet build` can verify)

**Untouched on purpose:**

- [Assets/Scripts/Planet/Atmosphere/AtmosphereDiagnostics.cs](../../Assets/Scripts/Planet/Atmosphere/AtmosphereDiagnostics.cs) — F12 stays on `Keyboard.current.f12Key.wasPressedThisFrame`. It's a debug screenshot dump, not gameplay input — no reason to gate it through the console map, and adding it to `IInputMapService` would create churn for no benefit.
- `Assets/InputSystem_Actions.inputactions` — Unity's sample asset from the new Input System package. Not referenced by any code. Leaving in place; Bryan can delete if he wants to declutter.

## Asset structure

`InputMapService` builds an `InputActionAsset` in memory via `ScriptableObject.CreateInstance` + `AddActionMap` / `AddAction` / `AddBinding`. Two maps:

### Gameplay map (currently enabled by default)

| Action                | Type    | Binding                            |
| --------------------- | ------- | ---------------------------------- |
| `Move`                | Value   | WASD + arrows (2DVector composite) |
| `VerticalMove`        | Value   | E (+) / Q (-) (1DAxis composite)   |
| `Roll`                | Value   | Z (+) / C (-) (1DAxis composite)   |
| `Look`                | Value   | `<Mouse>/delta`                    |
| `Scroll`              | Value   | `<Mouse>/scroll`                   |
| `LookHold`            | Button  | `<Mouse>/rightButton`              |
| `Sprint`              | Button  | LeftShift + RightShift             |
| `ToggleOrbit`         | Button  | Space                              |
| `FaceSun`             | Button  | Backspace                          |
| `FrameStorm`          | Button  | R                                  |
| `ToggleDebugOverlay`  | Button  | F6                                 |
| `CycleCaptureSet`     | Button  | F7                                 |
| `ToggleSunFreeze`     | Button  | F8                                 |
| `DumpWeather`         | Button  | F9                                 |
| `TogglePrecipitation` | Button  | P                                  |
| `TriggerCapture`      | Button  | F10                                |
| `ToggleProfiling`     | Button  | F11                                |
| `DropScaleMarker`     | Button  | M                                  |
| `TeleportToMarkers`   | Button  | T                                  |

### Console map (empty for slice 0)

Will be populated by CONSOLE-1 with backtick / Esc / Tab / alphanumeric / arrow keys / page up/down. The map exists so `IInputMapService.EnableConsole()` / `DisableConsole()` is wired up already — CONSOLE-1 just adds actions.

## Design notes

### Why programmatic instead of a `.inputactions` JSON asset

Two reasons:

1. **No `.meta` headaches** — a `.inputactions` file requires Unity to generate a `.meta` with an `InputActionImporter` reference. Without it the asset won't load. Building in code sidesteps the whole asset import dance.
2. **Diffable & reviewable** — bindings live as code in `InputMapService.cs`. Adds, removes, renames all show up as line diffs in PRs. No GUI round-trips.

The downside is no Unity Input Actions editor UI — but this is dev tooling input, not a player-facing rebinding screen, so the editor isn't useful for our case.

### Why typed action accessors instead of `service.GetAction("Move")`

19 actions, all used from a handful of files. Typed properties give IDE auto-complete and compile-time rename-safety; string lookups don't. The extra ~50 lines of property declarations in `IInputMapService` + `InputMapService` are a fair price.

### `Shift+M` modifier handling

Kept as-is — `DebugInputRelay` polls `Keyboard.current.shiftKey.isPressed` directly at the moment `DropScaleMarker` fires. The Input System has `OneModifier` composite that would let me model this as a separate `ClearScaleMarkers` action with a Shift+M binding, but:

- The existing pattern works
- When the Gameplay map is disabled (console open), `DropScaleMarker` won't fire at all, so the shift check is dead-coded out automatically
- One less action in the registry

Not a hill worth dying on.

### Service caching pattern

`FreeCameraController` is a scene component — its `Awake()` runs before `GameBootstrap.EarlyInitialize`, so the service isn't registered yet. Solution: cache lazily on first `Update()` via `GetInput()`, which calls `ServiceLocator.TryGet`. If the service still isn't ready (shouldn't happen but defensive), `Update()` no-ops for that frame.

`DebugInputRelay` is created by `EnsureComponent<>` inside `GameBootstrap.EarlyInitialize` — its `Update()` will not fire until next frame, by which time the service is registered. But uses the same defensive `TryGet` pattern for symmetry.

### Legacy fallback cleanup

`FreeCameraController` had `#if ENABLE_LEGACY_INPUT_MANAGER` fallback blocks calling `Input.GetKey` / `Input.GetMouseButton` / `Input.GetAxisRaw` if `Keyboard.current` / `Mouse.current` were null. These were dead code in practice — the project is committed to the new Input System — so they're gone. Net `-30` lines of conditional code.

## Validation guidance for Bryan

The user-facing behavior should be **identical** to before:

1. **Camera movement** — WASD or arrow keys move the camera forward/back/left/right
2. **Vertical movement** — E up, Q down
3. **Roll** — Z and C rotate the camera around the forward axis
4. **Sprint** — hold LeftShift or RightShift, movement multiplied by `FastMultiplier`
5. **Mouse look** — hold right mouse button, mouse delta rotates the camera
6. **Scroll forward/back** — mouse scroll wheel
7. **Shortcuts** — Space (orbit/surface toggle), Backspace (face sun), R (frame storm)
8. **Debug F-keys** — F6/F7/F8/F9/F10/F11 fire their respective debug events
9. **Markers** — M drops a scale reference marker at the look target; Shift+M clears all markers; T teleports the camera to the marker chain
10. **F12** — still captures atmosphere diagnostics (unchanged path)

If anything above no longer works, the action binding for that key is wrong in `InputMapService` and needs fixing before CONSOLE-1 starts.

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning in `DebugCaptureController.cs`)
- `dotnet build ProceduralPlanets.Planet.csproj` — clean (only pre-existing `CS0414` warning in `Planet.cs`)
- Zero remaining `Input.GetKey` / `GetMouseButton` / `GetAxis*` / `mousePosition` / `mouseScrollDelta` calls in `Assets/Scripts/`

## Line tally

Approximate lines added/removed:

| File                         | Lines net |
| ---------------------------- | --------- |
| `IInputMapService.cs` (new)  | +42       |
| `InputMapService.cs` (new)   | +116      |
| `GameBootstrap.cs`           | +9        |
| `FreeCameraController.cs`    | -64       |
| `DebugInputRelay.cs`         | -15       |
| `ProceduralPlanets.Core.csproj` | +2     |
| **Total**                    | **+90**   |

Design doc estimated `~150`. Under-budget because the existing code was already partially on the new Input System (direct `Keyboard.current` polling), so the migration was more compress-and-route than rewrite.

## What's next

CONSOLE-1 (next slice) builds on this foundation:

- Adds backtick / Esc / Tab / alphanumeric / arrow / page up/down actions to the `Console` map (mostly bindings, ~80 lines of `IInputMapService` growth)
- Creates `ConsoleController` MonoBehaviour with an `Update()` that calls `_input.DisableGameplay() + _input.EnableConsole()` on open, reverse on close
- Stands up `ConsoleOverlay.shader` + `ConsoleRenderer` to draw the backdrop quad
- Wires `IConsoleService` + `ServiceLocator` registration in `DebugConsoleBootstrap`
- Publishes `ConsoleOpenedEvent` / `ConsoleClosedEvent`

CONSOLE-2..5 follow the design doc's slice plan.

## Asking Bryan

After validation:

1. **All 10 verification items above work** → I proceed to CONSOLE-1
2. **Something is broken** → tell me which item; the binding fix is one line in `InputMapService`
3. **Open questions from design doc still pending** — answers needed before CONSOLE-3 (intellisense) and CONSOLE-5 (built-in commands), not blocking the next slice
