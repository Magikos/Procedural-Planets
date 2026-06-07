# 2026-06-04 — Debug Console (as shipped)

**Status:** Phase 2 + cleanup pass complete. ~60 commands across 13 prefixes, all infrastructure in place.

**Supersedes:** [`2026-06-03-debug-console.md`](2026-06-03-debug-console.md) (the original design proposal — kept for historical reference). 22 implementation slices + 3 cleanup slices arrived at the design described below.

**Slice log archive:** [`docs/agent-conversation/2026-06-0[34]-console-slice*.md`](../agent-conversation/) — full per-slice decision history.

## Architecture

```text
GameBootstrap (EarlyInitialize)
  └─ DebugConsoleBootstrap.Initialize()
       ├─ IsConsoleAllowed() — checks Debug.isDebugBuild || Application.isEditor || --allowDebug arg
       ├─ ConsoleRegistry.Scan() — reflection-walks every assembly for [CommandPrefix] + [ConsoleCommand]
       ├─ new GameObject("[DebugConsole]") + DontDestroyOnLoad
       ├─ AddComponent<ConsoleController>()
       └─ ServiceLocator.Register<IConsoleService>(controller)

ConsoleController (MonoBehaviour, persistent)
  ├─ Owns ConsoleRenderer (backdrop + popup + scrollbar via RenderPipelineManager.endCameraRendering)
  ├─ Owns ConsoleScrollback (ring buffer with stable Ids for in-place updates)
  ├─ Owns ConsoleInputBuffer (cursor-aware text buffer)
  ├─ Owns ConsoleHistory (PlayerPrefs persistence)
  ├─ Owns IntellisenseEngine (suggestion generation)
  └─ Subscribes IInputMapService (Gameplay / Console action map swap)

ConsoleRegistry (static)
  ├─ Dictionary<string, CommandData> — built once at boot
  ├─ Dictionary<Type, object> — explicit registry instances
  └─ CommandParser.Tokenize + TryBind → CommandExecutor.Execute
```

## File layout

```text
Assets/Scripts/Core/Console/
├── ConsoleController.cs           Lifecycle, input loop, modal state machine, async tracking
├── ConsoleRenderer.cs             Backdrop + popup + scrollbar; reads ConsoleTheme palette
├── ConsoleTheme.cs                [CreateAssetMenu] SO with ~30 color/styling fields
├── ConsoleScrollback.cs           Ring buffer of ConsoleMessage { Id, Text, Type }
├── ConsoleInputBuffer.cs          Cursor-aware text buffer (Insert/Delete/MoveLeft/...)
├── ConsoleHistory.cs              PlayerPrefs JSON, cycle-recall with draft preservation
├── ConsoleDebugModule.cs          F10 sidecar contributor (Open/Anchor/Pending diagnostics)
├── ConsoleAnchor.cs               Top/Bottom/Left/Right enum + bounds-rect math
├── ConsoleEvents.cs               ConsoleOpenedEvent / ConsoleClosedEvent (IGameEvent)
├── DebugConsoleBootstrap.cs       --allowDebug gate + GameObject creation
├── IConsoleService.cs             Public interface + ConsoleDiagnostics struct
│
├── Registry/
│   ├── ConsoleRegistry.cs         Reflection scan + instance lookup
│   ├── CommandData.cs             Resolved command metadata (Alias, Method, Parameters, IsAsync, ...)
│   ├── ParameterData.cs           Parameter metadata + FormatCommandSignature helper
│   ├── CommandParser.cs           Tokenize (quoted strings) + TryBind
│   ├── CommandExecutor.cs         ResolveTarget + Invoke + async hand-off to IConsoleService.BeginAsync
│   ├── ConsoleArgumentParsers.cs  int/float/bool/string/Vector2/Vector3/Color/enum (+ Nullable unwrap)
│   ├── ExpressionEvaluator.cs     Recursive-descent (+ - * /, parens, unary) for int/float args
│   ├── ConsoleCommandAttribute.cs
│   ├── CommandPrefixAttribute.cs
│   ├── ParamDescriptionAttribute.cs
│   ├── CompletionSourceAttribute.cs
│   ├── MonoTargetType.cs          Static / Single / Registry
│   └── IConsoleArgumentParser.cs
│
├── Intellisense/
│   ├── IntellisenseEngine.cs            Suggestion generation, provider cache, param-slot detection
│   ├── Suggestion.cs                    DisplayText + CompletionText + MatchStart/Length
│   ├── IConsoleCompletionProvider.cs
│   ├── CompletionRanker.cs              Shared "prefix-first, then substring" string ranker
│   ├── BoolCompletionProvider.cs        Auto-attached for bool / bool?
│   ├── EnumCompletionProvider.cs        Auto-attached for enum / Nullable<enum>
│   ├── CommandNamesProvider.cs          For `help <name>`
│   ├── DebugModeNamesProvider.cs        For `debug.mode <name>`
│   └── DebugCaptureSetNamesProvider.cs  For `debug.capture-set <name>`
│
├── Commands/
│   ├── ConsoleBuiltins.cs               echo, clear, help, quit, console.abandon/cancel/anchor/scrollback-size
│   └── TestConsoleCommands.cs           test.console.colors/async/async-result/async-fail/async-cancellable/error/types/spam/enum
│
└── Text/
    ├── TextSpan.cs                      (Color, string) pair for the renderer
    ├── ConsoleColorTagParser.cs         <color=name> / <color=#rrggbb> markup → TextSpan[]
    └── ConsoleColors.cs                 Shared named-color dict (used by parser AND arg parser)

Assets/Scripts/Core/Services/Commands/
└── ActionCommands.cs                    action.undo/redo/history/clear

Assets/Graphics/Shaders/Hidden/
└── ConsoleOverlay.shader                Backdrop + border + clip-rect text shader
```

**Per-system commands live on the existing controller class**, not in dedicated Commands/ files. Tagged with `[CommandPrefix("foo")]` at the class level. See `AtmosphereController.cs`, `CelestialManager.cs`, `Planet.cs`, etc.

## Public API — IConsoleService

```csharp
public interface IConsoleService
{
    bool IsOpen { get; }
    ConsoleAnchor Anchor { get; set; }
    int ScrollbackCapacity { get; set; }

    void Open();
    void Close();
    void Toggle();

    void RunCommand(string commandLine);
    void Print(string text);
    void PrintLine(string text);
    void PrintWarning(string text);
    void PrintError(string text);
    void Clear();

    void BeginAsync(string alias, object awaitable, bool isCancellable);
    void AbandonPending();
    void RequestCancelPending();
    void Confirm(string question, Action onYes, Action onNo = null);

    ConsoleDiagnostics GetDiagnostics();
}
```

Reachable via `ServiceLocator.TryGet<IConsoleService>(out var c)` from anywhere. Other systems gate camera/movement on `c.IsOpen`.

## Authoring commands

Decorate methods on any class with `[ConsoleCommand]`. Optionally add `[CommandPrefix("name")]` at the class level so all commands in that class get the prefix.

```csharp
[CommandPrefix("weather")]
public class WeatherManager : MonoBehaviour
{
    [ConsoleCommand("wind-speed", "Get or set wind speed (m/s).", MonoTargetType.Single)]
    string WindSpeedCmd(
        [ParamDescription("new wind speed in m/s; omit to read current value")]
        float? speed = null)
    {
        if (speed == null) return $"wind speed: {_windSpeed} m/s";
        _windSpeed = Mathf.Max(0f, speed.Value);
        return $"wind speed set to {_windSpeed} m/s";
    }
}
```

**MonoTargetType** controls instance resolution:
- `Static` — method is `static`, no instance needed
- `Single` — `Object.FindAnyObjectByType<DeclaringType>()` at invocation time (most common for controllers)
- `Registry` — explicit instance via `ConsoleRegistry.RegisterInstance<T>(instance)` (for non-MonoBehaviours)

**Parameter conventions:**
- Nullable types (`int?`, `float?`, `MyEnum?`) are auto-unwrapped by the parser. They double as the get/set idiom — pass `null` to read, pass a value to write.
- `CancellationToken` as the **last** parameter is hidden from the user-facing signature and auto-injected at invocation. Used for cancellable async commands.
- `[ParamDescription("...")]` appears in `help <command>` output.
- `[CompletionSource(typeof(MyProvider))]` overrides the default completion provider for a parameter. Default providers exist for `enum` (any), `bool`, and via reflection auto-attach.

**Async commands** return `Awaitable` or `Awaitable<T>`. The executor detects this at scan time (`CommandData.IsAsync`) and hands off to `IConsoleService.BeginAsync`, which:
- Appends a "running ..." line to scrollback with a stable Id
- Updates it every 200ms with a spinner + elapsed seconds
- On completion: replaces with "completed in X.XXs"; on exception: red error line; on cancel: warning line

**Return values:** `string` is printed as a single output line. `Awaitable<string>` prints its result after completion. `void` produces no output beyond what the command explicitly prints.

## Commands shipped (Phase 2 total: ~60)

| Prefix | Commands | Source |
| ------ | -------- | ------ |
| `camera` | speed, sensitivity, fast-multiplier, position, surface-view, teleport, save-teleport, remove-teleport, teleports | `FreeCameraController.cs` |
| `time` | freeze, speed, set-local, moon-phase | `CelestialManager.cs` |
| `quality` | get, list, set, cloud-steps | `QualityController.cs` |
| `scale` | drop, clear, teleport | `ScaleReferenceMarkers.cs` |
| `debug` | overlay, water-details, profiling, precipitation, cycle-capture-set, mode, capture-set, capture | `DebugCaptureController.cs` |
| `weather` | diagnostics, wind-speed, wind-direction | `WeatherManager.cs` |
| `atmosphere` | sun-intensity, rayleigh, mie, scale | `AtmosphereController.cs` |
| `cloud` | density, altitude, thickness | `CloudController.cs` |
| `precipitation` | intensity, debug-mode | `PrecipitationController.cs` |
| `lightning` | enable, delay, intensity | `WeatherLightningController.cs` |
| `action` | undo, redo, history, clear | `Core/Services/Commands/ActionCommands.cs` |
| `planet` | seed, resolution, generate | `Planet.cs` |
| `console` | abandon, cancel, anchor, scrollback-size | `Commands/ConsoleBuiltins.cs` |
| (no prefix) | echo, clear, help, quit | `Commands/ConsoleBuiltins.cs` |
| `test.console` | colors, async, async-result, async-fail, async-cancellable, error, types, spam, enum | `Commands/TestConsoleCommands.cs` |

## UX design

### Intellisense

- Type ≥ 1 character → suggestion popup of matching aliases (substring matching, prefix matches ranked first, alphabetical within each rank)
- After the first space → parameter-value suggestions for the current slot (via auto-attached or `[CompletionSource]` provider)
- `Tab` / `Enter` accepts the active suggestion; `Shift+Tab` cycles backward; `Up`/`Down` arrows move active suggestion or recall history
- `Escape`: dismisses popup → clears input → closes console (three-tier)
- **Ghost completion** fires only when there's exactly one suggestion that is a true PREFIX of typed text. Otherwise the popup shows (so the user can see distant substring matches).

### Submission rule

Single rule: *if there's an active suggestion that differs from typed text, accept it; else submit raw.* Replaces the older "popup-mode vs ghost-mode submit dichotomy" which led to occasional wrong-submission bugs.

### Async UX

| Situation | Behavior |
| --------- | -------- |
| Async command running | Scrollback shows "running alias … (1.2s)" with spinner; updates every 200ms |
| User types another command | Rejected with warning; lists abandon/cancel options |
| `console.abandon` | Stops tracking; background work continues silently |
| `console.cancel` (non-cancellable cmd) | Warning: "use console.abandon instead" |
| `console.cancel` (cancellable cmd) | Y/N modal → confirm → signals CancellationToken; command bails cooperatively |
| Command completes / fails / cancels | Line replaced in-place with completion / red error / warning |

### Modal Y/N

`IConsoleService.Confirm(question, onYes, onNo)` opens a modal in the input line region. Tab or Left/Right toggles Yes/No. Enter activates. Esc = No. Used by `quit` and `console.cancel`.

### Border-pulse for new messages

When user is scrolled back and new messages arrive, the border pulses amber. (Original design proposed a "N new" badge — pulse was chosen as less visually noisy.)

### Math expressions in numeric args

`int` and `float` args route through `ExpressionEvaluator` — supports `+ - * /`, parens, unary. Lets you write `time.speed 60*60` to set 1 hr/s, or `planet.generate 42 5000+1000` for radius 6000. Vector parsers do NOT (yet) route through expressions.

### Reproducible camera locations

Camera viewpoints used for visual debugging can be saved and restored:

```text
camera.save-teleport Grass Face Seam
camera.teleport Grass Face Seam
camera.remove-teleport Grass Face Seam
camera.teleports
```

The final string parameter consumes the remaining command text, so quotes are
optional when typing multi-word names. The completion popup inserts quotes when
needed.

`LastDebugCapture` is a reserved location updated once when an F10 capture
starts. `LastDebugPrint` is an alias for the same location. User locations and
the last capture persist through `PlayerPrefs`. When no persisted last capture
exists yet, the editor imports the newest F10 sidecar from
`local-only/debug-screenshots` as `LastDebugCapture`.

`camera.save-teleport` overwrites an existing name. The built-in debug sites
`Grass Face Seam A`, `Grass Face Seam B`, and `Terrain Texture Oblique` come
from representative F10 captures. Saving one of those names creates a user
override; removing that override reveals the built-in site again.

Locations store camera position relative to the planet transform when one is
available, exact camera rotation, and surface/orbit mode. This keeps a saved
view valid if the planet object moves while preserving the original framing.

Multiline command results are split into physical scrollback rows before
rendering. This keeps list commands aligned with the renderer's row budget and
makes every returned item visible and independently scrollable.

### Color tag markup in printed output

Any string passed to `Print*` may contain `<color=red>...</color>` or `<color=#rrggbb>...</color>` markup. Escape with `\<`. Single-level only (no nesting). Named colors live in `ConsoleColors.Named` (shared with the `Color`-typed argument parser).

## Theming

All ~30 colors live on `ConsoleTheme` (ScriptableObject). The renderer loads `Resources.Load<ConsoleTheme>("ConsoleTheme")` and falls back to `ConsoleTheme.CreateDefault()` (in-memory instance with built-in values) if the asset doesn't exist.

To customize: *Create → ProceduralPlanets → Console → Theme*, save as `Assets/Resources/ConsoleTheme.asset`, tune in inspector, restart Play mode.

## Build gating

```csharp
DebugConsoleBootstrap.IsConsoleAllowed()
  => Debug.isDebugBuild || Application.isEditor
  || Environment.GetCommandLineArgs().Contains("--allowDebug")
```

Release build without `--allowDebug`: no GameObject, no reflection scan, `IConsoleService` not registered. Callers using `TryGet` handle gracefully.

## Diagnostics

F10 capture sidecar appends:
```text
--- DebugConsole ---
Open: True, Anchor: Top
Scrollback: 87/1000
History: 23 entries
Commands registered: 60
Pending: 'planet.generate' running 3.42s, cancellable=True
```

Via `ConsoleDebugModule.AppendMetadata` → `IConsoleService.GetDiagnostics`.

## Input handling

`IInputMapService` exposes a `Gameplay` and a `Console` map. Open swaps Gameplay→Console (gating camera/etc). Close reverses. Backtick (`<Keyboard>/backquote`) is bound to both `OpenConsole` and `CloseConsole` actions so it toggles regardless of which map is active.

Keys handled in `Console` map:
| Key | Action |
| --- | ------ |
| Backtick | Toggle (also handled in Gameplay map) |
| Esc | Dismiss popup → clear input → close |
| Enter | Accept suggestion if active differs from typed, else submit |
| Tab / Shift+Tab | Cycle suggestion forward / back |
| Up / Down | Move active suggestion OR cycle history when no popup |
| PageUp / PageDown | Scroll output ±5 lines |
| Home / End | Cursor to start/end of input line |
| Left / Right | Move cursor (key-repeat) |
| Backspace / Delete | Delete (key-repeat) |
| Ctrl+V | Paste from clipboard (control chars stripped) |

Text input is hooked via `Keyboard.current.onTextInput` (auto-handles shift/locale).

## Known polish items (deferred)

From [the CONSOLE-6 audit](../agent-conversation/2026-06-04-console-slice6-audit.md), Tier 5:

- **F16:** `ConsoleRegistry.Scan` walks every assembly with no filter (cold-path-only cost, ~unmeasured).
- **F17:** `IntellisenseEngine.MaxSuggestions = 200` silently truncates; no "(more...)" indicator.
- **F18:** `Vector2`/`Vector3` parsers don't route through `ExpressionEvaluator` (no `1+1,2,3`).
- **D7:** No `PrintEscaped` for user-supplied strings that might contain `<color>`. Low risk today — all printers are internal code.
- **D8:** No per-line timestamps; no `console.wrap-mode` toggle. Long lines wrap by SDF text renderer's default behavior.
- **F36:** `BuildInputSpans` rebuilds every frame (cursor blink, spinner). Could cache. Minor GC pressure.

## Off-arc, separately tracked

- **Mouse scroll wheel** → scrollback / popup (estimated ~50 lines).
- **Mouse click** → accept popup row (~100 lines).
- **Shift+arrow text selection + Ctrl+C/X/A** — only if selection is built (Bryan's note).
- **Floating anchor + drag/resize** — deferred indefinitely.
- **`[DebugOnly]` attribute** — discussed and skipped as YAGNI. `--allowDebug` already gates the whole subsystem; finer-grained stripping only earns its keep if we ever want to ship release-with-console-but-filtered-commands. Revisit if that requirement appears.
- **Wind-line visualization shader** — diagnostic for the grass-vs-cloud direction mismatch Bryan flagged. Off-arc.
- **Grass wind v2** — current algorithm too uniform / robotic.
- **Grass.* commands** — would need `IGrassQualitySettings` refactor (read-only interface today). Deferred per Bryan's "agreed with your recommendation."

## Slice history

22 implementation slices (CONSOLE-0 through CONSOLE-5.13) + 3 cleanup slices (CONSOLE-6 audit, CONSOLE-6.1 dedup, CONSOLE-6.2 renderer cleanup, CONSOLE-6.3 ConsoleTheme SO). Full per-slice decision logs at [`docs/agent-conversation/2026-06-0[34]-console-slice*.md`](../agent-conversation/).

Key design pivots captured in the slice logs (not re-explained here):
- Two-mode submit dichotomy → single "accept if different else submit" rule
- "N new" badge → border pulse
- Reject-second-async → abandon + cancel + Y/N modal
- `time.freeze` rename ambiguity → kept; means sun freeze
- Substring + prefix-first matching for everything (aliases, params, enums)
- `planet.seed` lazy mutation (set doesn't auto-regen — batch then explicit `planet.generate`)
- `[CompletionSource]` design for `debug.mode` and `debug.capture-set` (DebugRegistry registered in ServiceLocator so providers can enumerate)
- `ConsoleTheme` SO with `Resources.Load` + `CreateDefault` fallback (zero-config out of box, opt-in tunability)
