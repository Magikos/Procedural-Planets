# 2026-06-03 — Debug Console Slice CONSOLE-1: Overlay + Lifecycle

**Status:** Shipped. Awaiting Bryan validation (backtick opens an empty console; Esc/backtick closes; gameplay input gated while open).

**Design doc:** [docs/design/2026-06-03-debug-console.md](../design/2026-06-03-debug-console.md) — slice CONSOLE-1.

**Goal:** Backtick opens an empty console with a tinted-glass backdrop + blinking input cursor. Gameplay input is gated. Esc or backtick closes. EventBus fires open/close events. No commands work yet (CONSOLE-2 ships those).

## Files

**New:**

- [Assets/Graphics/Shaders/Hidden/ConsoleOverlay.shader](../../Assets/Graphics/Shaders/Hidden/ConsoleOverlay.shader) — fullscreen tinted-glass backdrop with border
- [Assets/Scripts/Core/Console/IConsoleService.cs](../../Assets/Scripts/Core/Console/IConsoleService.cs) — service contract (Print/RunCommand/Clear stubbed for slice 1)
- [Assets/Scripts/Core/Console/ConsoleAnchor.cs](../../Assets/Scripts/Core/Console/ConsoleAnchor.cs) — enum + bounds-rect math (Top/Bottom/Left/Right)
- [Assets/Scripts/Core/Console/ConsoleEvents.cs](../../Assets/Scripts/Core/Console/ConsoleEvents.cs) — `ConsoleOpenedEvent` / `ConsoleClosedEvent` (both `IGameEvent`)
- [Assets/Scripts/Core/Console/ConsoleRenderer.cs](../../Assets/Scripts/Core/Console/ConsoleRenderer.cs) — owns the overlay material + input-line `SDFTextRenderer`; `Render(cmd, alpha, anchor, line)`
- [Assets/Scripts/Core/Console/ConsoleController.cs](../../Assets/Scripts/Core/Console/ConsoleController.cs) — MonoBehaviour, owns lifecycle, drives map swap + alpha fade + RenderPipelineManager hook
- [Assets/Scripts/Core/Console/DebugConsoleBootstrap.cs](../../Assets/Scripts/Core/Console/DebugConsoleBootstrap.cs) — static `Initialize()`, checks `--allowDebug` gate, creates `[DebugConsole]` GameObject, registers `IConsoleService`

**Modified:**

- [Assets/Scripts/Core/Interfaces/IInputMapService.cs](../../Assets/Scripts/Core/Interfaces/IInputMapService.cs) — added `OpenConsole` + `CloseConsole`
- [Assets/Scripts/Core/Services/InputMapService.cs](../../Assets/Scripts/Core/Services/InputMapService.cs) — `OpenConsole` on Gameplay map (backtick), `CloseConsole` on Console map (backtick + Esc)
- [Assets/Scripts/Core/Services/GameBootstrap.cs](../../Assets/Scripts/Core/Services/GameBootstrap.cs) — calls `DebugConsoleBootstrap.Initialize()` after services register
- `ProceduralPlanets.Core.csproj` — 6 new `<Compile Include=...>` entries for console files (Unity will regenerate on next Editor refresh; added manually for headless `dotnet build` to find them)

## How the input map swap works

The trick that makes backtick open AND close cleanly without double-firing:

- `OpenConsole` (backtick) lives on the **Gameplay** map
- `CloseConsole` (backtick + Esc) lives on the **Console** map
- Console closed → Gameplay enabled, Console disabled → only `OpenConsole` fires on backtick
- Console open  → Gameplay disabled, Console enabled → only `CloseConsole` fires on backtick or Esc

No state-machine guards or per-frame dedupe needed — the maps handle the dispatch. When `Open()` runs it calls `DisableGameplay() + EnableConsole()`; `Close()` does the reverse.

## Visual design

- Backdrop: top 1/3 of screen, ~`(0.05, 0.06, 0.08, 0.78)` near-black with slight blue tint, 78 % alpha
- Border: 1-pixel-ish cyan glow `(0.4, 0.8, 1.0, 0.6)` along the edge
- Fade in/out: 120 ms (`Time.unscaledDeltaTime` so it works while paused)
- Input line: `> _` with cursor blinking at 2 Hz
- Anchor: hardcoded to `Top` for slice 1. `ConsoleAnchor` enum + bounds math already supports all four; slice 5's `console.anchor` command flips it.

## Build gate

`DebugConsoleBootstrap.IsConsoleAllowed()` returns true if:

- `Debug.isDebugBuild` (debug build), OR
- `Application.isEditor` (Editor playmode), OR
- Command-line args contain `--allowDebug`

In a release build without the flag, `Initialize()` logs "Debug console disabled..." and returns null. No GameObject created, no service registered, no input wiring, no shader load. Callers using `ServiceLocator.TryGet<IConsoleService>` handle gracefully.

## Stub behavior for slice 1

`IConsoleService` is fully shaped but most methods stub for later slices:

| Method            | Slice 1 behavior                                                     |
| ----------------- | -------------------------------------------------------------------- |
| `IsOpen`, `Anchor`, `Open()`, `Close()`, `Toggle()` | Fully working                              |
| `Print` / `PrintLine` | Logs via `LoggerProvider` (Info level)                           |
| `PrintError`      | Logs via `LoggerProvider` (Error level)                              |
| `RunCommand`      | Logs a Warning that the registry ships in CONSOLE-2                  |
| `Clear`           | No-op (scrollback ships in CONSOLE-4)                                |

This means anyone wiring `IConsoleService` integration today can call all of it without crashing; output just routes to the Unity Console for now.

## Renderer pattern

Mirrors `LoadingManager`:

- `RenderPipelineManager.endCameraRendering` callback
- Only renders if `_currentAlpha > 0.001` (skips when closed)
- Only renders on `Camera.main` (no editor-camera ghosting)
- Single `CommandBuffer` per frame: `DrawProcedural` backdrop, then `DrawMesh` input-line text
- `ctx.ExecuteCommandBuffer(cmd); ctx.Submit();`

The input line uses `SDFTextRenderer` (same infra as the loading screen's percent label). Em size 0.022 (~24px at 1080p). Positioned at the anchor's bottom-left corner with a small padding inset.

## Risks / open items

- **Shader needs to be in "Always Included Shaders"** — `Shader.Find("Hidden/ConsoleOverlay")` returns null at runtime in a build if the shader isn't either referenced by a material in the build OR listed in Project Settings → Graphics → Always Included Shaders. Bryan will need to add it once via the Editor (only matters for built players, Editor works fine without). Same caveat applies to `Hidden/LoadingOverlay` and `Hidden/SDFText` — already in the Always Included list per the loading screen's history.
- **No mouse-cursor handling when console opens** — if the cursor was locked from right-mouse camera look, opening the console doesn't release it. Acceptable for slice 1 because the `LookHold` action is on the now-disabled Gameplay map, so right-mouse-drag camera stops being polled. Cursor visibility/lock will be revisited if it becomes a problem.
- **Border-edge antialiasing** — the border is a sharp 1-pixel cut, no AA. Looks fine on integer-scaled screens; may shimmer on fractional UI scales. Cosmetic; can fix with a smoothstep on the border distance later.
- **Backtick conflict with US keyboard layouts** — `<Keyboard>/backquote` should map to the key labeled `~ ` ` `. Non-US layouts may map differently; we'll add a `console.bind-open <key>` later if it becomes an issue.

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning in `DebugCaptureController.cs`)
- 7 new files + 3 modified
- Net `+340` lines (vs. design doc estimate `~400`)

## Validation guidance for Bryan

In Play mode:

1. **Press backtick (` `` `)** — the top 1/3 of the screen should tint dark-blue with a faint cyan border, fading in over ~120ms. A blinking `_` cursor appears after the `> ` prompt.
2. **Try to move the camera with WASD** — should NOT respond
3. **Try mouse-look (hold right mouse)** — should NOT respond
4. **Try F10** — should NOT trigger a capture
5. **Try M / T / P** — should NOT fire those commands
6. **Press Esc** — console fades out over ~120ms; cursor disappears
7. **WASD now works again**, F10 captures again, etc.
8. **Press backtick again** — opens
9. **Press backtick (while open)** — closes (backtick toggles cleanly)
10. **Repeat open/close several times** — no flicker, no input "stuck" in either map

If any of those fail, check the Unity Console log for `"Debug console initialized"` or `"Debug console disabled"`. If you see neither, the bootstrap didn't run (check the order of `EarlyInitialize` chains).

## What's next

**CONSOLE-2** (per design doc, ~450 lines): reflection scan, `CommandData`/`ParameterData`, attribute set, the three `MonoTargetType` modes (Static/Single/Registry), argument parsers (int/float/bool/string/Vector2/Vector3/Color/enum), command executor (sync only — async ships in CONSOLE-4). After CONSOLE-2 you can type `echo hello` and see "hello" come back in the Unity Console (Print routes there until CONSOLE-4 scrollback lands).

But first: confirm slice 1 validates. If it does, CONSOLE-2 starts on your word.
