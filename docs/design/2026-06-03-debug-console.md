# 2026-06-03 — Debug Console Design

> **HISTORICAL — superseded by [`2026-06-04-debug-console-as-shipped.md`](2026-06-04-debug-console-as-shipped.md).**
>
> This document captures the original design proposal. Reality diverged across 22 implementation slices + 3 cleanup slices. The as-shipped doc describes the actual current implementation; this one is kept for design-history reference (helpful for understanding *why* certain decisions were made or revisited).
>
> Specific divergences from this proposal are catalogued as D1-D11 in [`docs/agent-conversation/2026-06-04-console-slice6-audit.md`](../agent-conversation/2026-06-04-console-slice6-audit.md).

**Status:** Design proposal awaiting approval. Author: Claude Code (Opus 4.7). Reviewer: Codex + Bryan.

**Discussion thread:** Brainstorming happened in chat 2026-06-03. Key decisions:

- Reuse existing `SDFTextMeshBuilder` + `LoadingOverlay`-style backdrop shader
- Attribute-based commands (Quantum-style) with class-level `[CommandPrefix]` + method-level `[ConsoleCommand]`
- Input system migration runs as prerequisite slice (CONSOLE-0)
- Full intellisense + tab complete from v1
- 5 Quantum features adopted up front: `[CommandPrefix]`, async commands, `[ParamDescription]`, auto-scroll-unless-scrolled, persistent history
- Build gated behind `--allowDebug` command-line arg in release

## Goals

Runtime command interface for the game with these properties:

1. **Toggle with backtick** (`` ` ``), close with Esc. Game input gated while open.
2. **Attribute-decorated commands** discovered via reflection. Commands live on the target class (e.g., `WeatherManager.SetWindSpeed` decorated in place), not in separate wrapper files.
3. **Full Quantum-style intellisense**: live suggestions while typing, tab cycling, parameter type hints, per-parameter value completion (e.g., enum values).
4. **Tinted glassy backdrop + SDF text** rendering via the existing infra.
5. **Configurable anchor**: top/bottom/left/right (floating + drag deferred to v2).
6. **Color-tagged output** via `<color=red>...</color>` syntax (matches Unity rich text convention).
7. **Async command support** for commands returning `Awaitable` / `Awaitable<T>`.
8. **Persistent command history** across runs.
9. **Console-triggered captures** (`debug.capture <set>` complements F10 hotkey).
10. **Build gated** by `--allowDebug` CLI arg in release builds.

## Non-goals (v1)

- Real Gaussian blur on the backdrop (tinted glass via shader math only)
- Mouse drag for floating anchor (v2+)
- Mouse wheel scroll (v2+)
- Variables / aliases (`set foo=bar; cmd $foo`)
- Key binding (`bind F5 "..."`)
- C# REPL / runtime compilation
- Ctrl+C cancellation of async commands (add when first long-running command needs it)
- Lambda command registration outside attributes

## Prerequisite — Slice CONSOLE-0: Input System Migration

Done before any console code. Independent and small (~2-3 hours).

**Scope:**

- Convert all `Input.GetKey` / `Input.GetButton` / `Input.mousePosition` usages to InputAction equivalents:
  - [FreeCameraController.cs](../../Assets/Scripts/Core/Services/FreeCameraController.cs) (WASD + mouse, currently legacy fallback)
  - Any remaining sites surfaced by `grep "Input\." Assets/Scripts/`
- Define an `InputActionAsset` with two maps:
  - `Gameplay` — camera, F10, F8, M, T, etc
  - `Console` — backtick (also defined in Gameplay so console can open during gameplay), Esc, alphanumeric input, tab, arrow keys, page up/down
- Console map is **disabled** by default; gameplay map is enabled
- Console controller toggles maps on open/close

**Verification:** F10 still captures, M still drops markers, T teleports, WASD moves camera. No behavioral change.

## Architecture Overview

```text
GameBootstrap (EarlyInitialize)
  └─ DebugConsoleBootstrap
       ├─ Check --allowDebug CLI arg (skip if release build without flag)
       ├─ Create ConsoleController GameObject + DontDestroyOnLoad
       ├─ Build CommandRegistry via reflection scan
       └─ ServiceLocator.Register<IConsoleService>(controller)

ConsoleController (MonoBehaviour, persistent)
  ├─ Owns ConsoleRenderer (backdrop + text mesh draw via RenderPipelineManager hook)
  ├─ Owns ConsoleInput (InputAction map swap, key handling)
  ├─ Owns ConsoleScrollback (ring buffer, color tag parser)
  ├─ Owns ConsoleHistory (input line recall, persistent via PlayerPrefs)
  ├─ Owns IntellisenseEngine (live suggestions over CommandRegistry)
  └─ Delegates command execution to CommandRegistry

CommandRegistry (static)
  ├─ Built once at startup via assembly scan
  ├─ Dictionary<string, CommandData>
  ├─ Parser: tokenize input → bind args by type
  └─ Executor: resolve instance (Static/Single/Registry) → invoke method
```

## Rendering Layer

### Backdrop shader

New `Assets/Graphics/Shaders/Hidden/ConsoleOverlay.shader`. Mirrors [LoadingOverlay.shader](../../Assets/Graphics/Shaders/LoadingOverlay.shader) structure: fullscreen triangle, controllable color via uniforms.

Uniforms:

- `float4 _BackdropColor` — backdrop tint (default ~`(0.05, 0.06, 0.08, 0.78)` — near-black with slight blue, ~78% alpha)
- `float4 _BorderColor` — border tint (default `(0.4, 0.8, 1.0, 0.6)` — cyan glow)
- `float _Alpha` — global alpha multiplier (animates 0→1 on open)
- `float4 _BoundsRect` — `(xMin, yMin, xMax, yMax)` in normalized screen space [0,1]
- `float _BorderThickness` — normalized units, e.g., `0.0025`
- `float _ScanlineStrength` — `0` for clean, `>0` for retro feel

Fragment math:

- Inside bounds: backdrop color
- On border (within thickness of bounds edge): border color
- Outside bounds: alpha = 0 (transparent — game shows through outside console)
- Optional scanlines: `uv.y * resolution.y % 2 == 0 ? slightly darker` based on `_ScanlineStrength`

No blur in v1 — tinted color + low alpha is the "glassy" effect.

### Text rendering — reuse `SDFTextMeshBuilder`

Uses [SDFTextMeshBuilder.BuildScreen](../../Assets/Scripts/Core/Text/SDFTextMeshBuilder.cs) for all text. One mesh per logical "draw" — output history is rendered as one mesh (all visible lines concatenated with `\n`), input line is another, intellisense popup is another.

Font asset: `Resources.Load<SDFFontAsset>("DefaultFont")` (already used by LoadingManager).

Layout math (per anchor):

- `top` (default): backdrop from `(0, 0.66)` to `(1, 1)` — top 1/3 of screen
- `bottom`: `(0, 0)` to `(1, 0.34)`
- `left`: `(0, 0)` to `(0.34, 1)`
- `right`: `(0.66, 0)` to `(1, 1)`
- `floating` (v2+): position + size both stored as state, drag to reposition

Within the backdrop:

- Output area: bottom 90% (leaves room for input line)
- Input line: bottom 10%, drawn as `> {currentInput}_` with blinking cursor
- Intellisense popup: anchored to input line, expands upward, shows top N matches

Render hook: `RenderPipelineManager.endCameraRendering` — same pattern as LoadingManager. Renders backdrop first, then text meshes on top. Console-aware F10 capture (see [F10 integration](#console-triggered-captures)) wraps the capture call with close/reopen so console doesn't appear in screenshots.

### Color tag rendering

Output strings may contain `<color=red>error</color>` markup. Parser splits the string into `(color, text)` spans before passing to the mesh builder.

`SDFTextMeshBuilder` currently takes one `Color` per call. **Required extension:** new overload `BuildScreen(IList<TextSpan> spans, ...)` where `TextSpan { Color color; string text; }`. Internally just iterates spans, accumulating glyphs at the running x cursor. ~30 lines.

Supported tags (v1):

- `<color=red>` / `<color=#rrggbb>` — opens a color span
- `</color>` — closes back to default
- Named colors: red, green, blue, yellow, cyan, magenta, white, grey, orange (small lookup table)
- Hex: `#rrggbb` and `#rrggbbaa`
- Escape: `\<` for literal `<`

Tags can NOT nest in v1 (matches Quantum behavior). Outer `</color>` always returns to default.

## Input Handling

### IConsoleService interface

```csharp
public interface IConsoleService
{
    bool IsOpen { get; }
    void Open();
    void Close();
    void Toggle();
    void RunCommand(string commandLine);
    void Print(string text);
    void PrintLine(string text);
    void PrintError(string text);
    void Clear();
}
```

Registered with `ServiceLocator` by bootstrap. Other systems gate on `IsOpen`:

```csharp
if (ServiceLocator.TryGet<IConsoleService>(out var c) && c.IsOpen) return;
```

### EventBus integration

```csharp
public readonly struct ConsoleOpenedEvent { }
public readonly struct ConsoleClosedEvent { }
```

Published on open/close. Lets systems react (pause AI, mute music, etc) without polling `IsOpen` each frame.

### Backtick + Esc behavior

- `` ` `` toggles open/close (always — even when console is open, backtick closes)
- `Esc` closes if open
- `Enter` executes current input line, adds to history, clears input
- `Up` / `Down` cycles command history
- `Tab` accepts current intellisense suggestion
- `Shift+Tab` cycles BACKWARD through intellisense suggestions
- `PageUp` / `PageDown` scrolls output scrollback
- `Home` / `End` jumps to top / bottom of scrollback
- `Ctrl+L` clears scrollback (like Unix terminals)
- `Ctrl+U` clears input line
- Standard alphanumeric + symbols → input line
- `Backspace` / `Delete` / `Left` / `Right` / `Home` / `End` on input line → standard editing

### Input action map swap

On open: disable `Gameplay` map, enable `Console` map. On close: reverse. Camera/movement stops getting input while console is open. New systems written against InputActions get this for free.

## Command Discovery

### Attributes

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandPrefixAttribute : Attribute
{
    public readonly string Prefix;
    public CommandPrefixAttribute(string prefix) { Prefix = prefix; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ConsoleCommandAttribute : Attribute
{
    public readonly string Alias;
    public readonly string Description;
    public readonly MonoTargetType TargetType;
    public ConsoleCommandAttribute(string alias, string description = null,
        MonoTargetType targetType = MonoTargetType.Static)
    {
        Alias = alias;
        Description = description ?? "";
        TargetType = targetType;
    }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParamDescriptionAttribute : Attribute
{
    public readonly string Description;
    public ParamDescriptionAttribute(string description) { Description = description; }
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CompletionSourceAttribute : Attribute
{
    public readonly Type ProviderType;
    public CompletionSourceAttribute(Type providerType) { ProviderType = providerType; }
}
```

### MonoTargetType (v1)

```csharp
public enum MonoTargetType
{
    Static = 0,    // Method is static. No instance resolution. Default.
    Single = 1,    // Find first active MonoBehaviour of declaring type via FindAnyObjectByType.
    Registry = 2,  // Explicit instance registered via ConsoleRegistry.RegisterInstance<T>(instance).
}
```

Modes deferred to v2: `All`, `Singleton`, `Argument`, `ArgumentMulti`, `SingleInactive`.

Use patterns:

- `Static`: built-in commands like `help`, `clear`, `echo`. Or wrapper commands for things that don't have a natural class owner.
- `Single`: on MonoBehaviour classes — `WeatherManager`, `AtmosphereController`, `Planet`. The reflection layer calls `Object.FindAnyObjectByType<T>()` to resolve.
- `Registry`: for services that aren't MonoBehaviours but need instance methods. Registered by their owning bootstrap code via `ConsoleRegistry.RegisterInstance<IFoo>(myInstance)`.

### Reflection scan

At startup (after services register but before first frame):

```csharp
foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
{
    foreach (Type type in asm.GetTypes())
    {
        string prefix = type.GetCustomAttribute<CommandPrefixAttribute>()?.Prefix ?? "";
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance))
        {
            foreach (ConsoleCommandAttribute attr in method.GetCustomAttributes<ConsoleCommandAttribute>())
            {
                string alias = string.IsNullOrEmpty(prefix) ? attr.Alias : $"{prefix}.{attr.Alias}";
                CommandData data = BuildCommandData(alias, attr, method, type);
                ConsoleRegistry.Add(data);
            }
        }
    }
}
```

Time cost: <50ms at our scale. Run once at boot.

### CommandData / ParameterData (typed structures)

```csharp
public sealed class CommandData
{
    public string Alias;
    public string Description;
    public MonoTargetType TargetType;
    public Type DeclaringType;
    public MethodInfo Method;
    public ParameterData[] Parameters;
    public Type ReturnType;
    public bool IsAsync; // ReturnType is Awaitable or Awaitable<T>
}

public sealed class ParameterData
{
    public string Name;
    public Type Type;
    public bool HasDefault;
    public object DefaultValue;
    public string Description;
    public Type CompletionProvider; // null if no [CompletionSource] attribute
}
```

Single source of truth used by:

- Parser (tokenize → bind args by `Parameters[i].Type`)
- Executor (resolve target via `TargetType`, invoke `Method`)
- Intellisense (suggest aliases, show signature, ask `CompletionProvider` for value suggestions)
- Help command (format Description + ParameterData for output)

## Command Execution

### Parser

Input: raw line like `weather.wind-speed 3.5`.

Tokenize by whitespace, respecting quoted strings (`weather.state "very stormy"`). First token is the command alias; remaining tokens are positional args.

```csharp
TokenizeResult Tokenize(string line);  // returns alias + arg strings
```

Bind args by parameter type via registered `IConsoleArgumentParser` instances:

```csharp
public interface IConsoleArgumentParser
{
    bool CanParse(Type type);
    bool TryParse(string token, Type type, out object value, out string error);
}
```

Built-in parsers (`Assets/Scripts/Core/Console/ConsoleArgumentParsers.cs`):

- `int`, `float`, `bool`, `string` — primitive types
- `Vector2`, `Vector3` — accept `"1,2"` or `"1 2"` or 2-3 numeric tokens (e.g., `wind-direction 1 0 0`)
- `Color` — accept named or `#rrggbb`
- `Enum<T>` — accept enum name (case-insensitive)

Parameters with defaults can be omitted (positional from left). E.g., `void Foo(int a, int b = 5)` → `foo 3` works, `b` defaults to 5.

### Executor

```csharp
public async Awaitable Execute(CommandData cmd, object[] args)
{
    object target = ResolveTarget(cmd);  // null for Static, instance for Single/Registry
    object result = cmd.Method.Invoke(target, args);

    if (cmd.IsAsync && result is Awaitable awaitable)
    {
        console.Print("<color=grey>running...</color>");
        await awaitable;
        // If Awaitable<T>, also print the result
    }
    else if (result is string s)
    {
        console.PrintLine(s);
    }
    // void returns: no output unless command itself printed
}

object ResolveTarget(CommandData cmd)
{
    return cmd.TargetType switch
    {
        MonoTargetType.Static => null,
        MonoTargetType.Single => Object.FindAnyObjectByType(cmd.DeclaringType, FindObjectsInactive.Exclude),
        MonoTargetType.Registry => ConsoleRegistry.GetInstance(cmd.DeclaringType),
        _ => throw new NotSupportedException()
    };
}
```

Exception during invocation → caught, printed as red error, command does NOT crash the game.

### Async command support

Method returning `Awaitable` or `Awaitable<T>` is detected at reflection time (`CommandData.IsAsync = true`). Executor awaits. Console prints a "running..." indicator while the awaitable is pending. If user issues another command while one is async (rare), enqueue or reject — pick reject for v1.

## Intellisense

State machine:

- After ≥1 character typed: query registry for matches
- Match by: alias contains the typed prefix (case-insensitive substring) — Quantum-style fuzzy
- Sort by: prefix-match-quality (exact prefix > startswith > substring) then alphabetical
- Render top 8 in a popup above the input line
- Highlight matched substring within each suggestion (color tag in render)
- One suggestion is "active" (highlighted background) — Tab accepts, Shift+Tab cycles back, Up/Down arrows within the popup move active

```csharp
public sealed class IntellisenseEngine
{
    public IList<Suggestion> Update(string inputText, int cursorPos);
}

public readonly struct Suggestion
{
    public CommandData Command;        // null if parameter-value suggestion
    public string DisplayText;         // e.g., "weather.wind-speed (float speed)"
    public string CompletionText;      // what to insert if accepted
    public int MatchStart;             // for highlight
    public int MatchLength;
    public ParameterData ParameterContext; // non-null when suggesting param values
}
```

### Parameter value completion

When cursor is positioned in a parameter slot (after the alias + N tokens), if that parameter has a `[CompletionSource]` attribute, invoke the provider:

```csharp
public interface IConsoleCompletionProvider
{
    IEnumerable<string> GetCompletions(string partialValue);
}

// Example:
public sealed class WeatherStateCompletionProvider : IConsoleCompletionProvider
{
    public IEnumerable<string> GetCompletions(string partial)
        => Enum.GetNames(typeof(WeatherState))
            .Where(n => n.StartsWith(partial, StringComparison.OrdinalIgnoreCase));
}
```

Auto-attached for enum parameters (synthesize a default enum provider if no `[CompletionSource]` specified). User-defined for biome names, scene names, etc.

## Output Scrollback

`ConsoleScrollback` is a ring buffer of `OutputLine { TextSpan[] spans; LogLevel level; float timestamp; }`. Default capacity: 1000 lines (configurable via `console.scrollback-size <N>`).

When new line added:

- If user is "at bottom" (scroll position 0 from bottom), auto-scroll to keep new line visible
- If user has scrolled up (>0 from bottom), DON'T auto-scroll. Optionally show a "N new" badge.

PageUp/PageDown moves scroll position by ~10 lines. Home/End jumps to bottom/top.

## Command History

`ConsoleHistory` stores last N input lines (default 100). Up arrow recalls previous; Down arrow recalls next. New input adds to history.

Persistence:

- On `Close`: serialize last 100 lines to `PlayerPrefs.SetString("ConsoleHistory", JsonUtility.ToJson(...))`
- On `Open` (first time per run): deserialize

PlayerPrefs is fine for this — small string, no concurrency, persists across runs.

## Built-in Commands (v1)

`Assets/Scripts/Core/Console/Commands/ConsoleBuiltins.cs` — static methods, no class prefix:

| Command                   | Args                         | Description                        |
| ------------------------- | ---------------------------- | ---------------------------------- |
| `help`                    | `[name]`                     | List all commands, or describe one |
| `clear`                   | —                            | Clear scrollback                   |
| `history`                 | —                            | Show recent input                  |
| `echo`                    | `<text>`                     | Print text back                    |
| `quit`                    | —                            | `Application.Quit()`               |
| `console.resize`          | `<0..1>`                     | Set height fraction                |
| `console.anchor`          | `<top\|bottom\|left\|right>` | Set anchor                         |
| `console.scrollback-size` | `<N>`                        | Set ring buffer size               |

Per-module commands (decorated on existing classes):

| Command                            | Class                  | Description                       |
| ---------------------------------- | ---------------------- | --------------------------------- |
| `weather.wind-speed <float>`       | WeatherManager         | Set wind speed                    |
| `weather.wind-direction <Vector3>` | WeatherManager         | Set wind direction                |
| `weather.state <WeatherState>`     | WeatherManager         | Set weather state (enum)          |
| `time.sun-elevation <degrees>`     | (TBD class)            | Force sun angle                   |
| `time.freeze <bool>`               | (TBD class)            | Freeze/unfreeze sun (replaces F8) |
| `grass.density-mult <float>`       | (TBD class)            | Tweak grass density at runtime    |
| `grass.show-coverage <bool>`       | (TBD class)            | Toggle LOD coverage debug viz     |
| `debug.capture <set>`              | DebugCaptureController | Trigger an F10 capture set        |
| `planet.regenerate`                | Planet                 | Regenerate current planet (async) |
| `planet.seed <int>`                | Planet                 | Set seed for next regen           |

~17 commands. Real demonstrable value on day 1.

## Console-Triggered Captures

`[ConsoleCommand("debug.capture")]` on `DebugCaptureController` (existing class). Implementation pattern Bryan proposed:

```csharp
public async Awaitable CaptureSet(string setName)
{
    var console = ServiceLocator.Get<IConsoleService>();
    bool wasOpen = console.IsOpen;
    if (wasOpen) console.Close();
    try
    {
        await DoCapture(setName);  // existing capture pipeline
    }
    finally
    {
        if (wasOpen) console.Open();
    }
}
```

Console close/open happen within the same async flow. Unity's `ScreenCapture` is synchronous per frame, so the close→capture→reopen completes in one frame, no visible flicker.

**Future note for `DoCapture`:** if we ever move to multi-frame async capture, this pattern needs revisiting (probably an "exclude from capture" render-layer flag instead of close/reopen).

F10 hotkey stays — the command is an additional entry point, not a replacement.

## Build Gating

In `Assembly-CSharp` early init:

```csharp
static bool IsDebugBuild => Debug.isDebugBuild || Application.isEditor;
static bool HasAllowDebugFlag => System.Environment.GetCommandLineArgs()
    .Any(a => string.Equals(a, "--allowDebug", StringComparison.OrdinalIgnoreCase));

static bool ConsoleEnabled => IsDebugBuild || HasAllowDebugFlag;
```

`DebugConsoleBootstrap` checks `ConsoleEnabled` before creating the controller. In a release build without the flag:

- Backtick does nothing
- `IConsoleService` is not registered (callers using `TryGet` handle gracefully)
- No GameObject allocated, no reflection scan run

Distribution: shipped game's launcher script (or shortcut) appends `--allowDebug` for QA / community testing builds. Public release leaves it off.

## File Layout

```text
Assets/Scripts/Core/Console/
  IConsoleService.cs                  Service interface
  DebugConsoleBootstrap.cs            Static bootstrap (called by GameBootstrap.EarlyInitialize)
  ConsoleController.cs                MonoBehaviour: input loop, lifecycle
  ConsoleRenderer.cs                  Backdrop quad + text mesh draw via RenderPipelineManager hook
  ConsoleScrollback.cs                Ring buffer of OutputLine + auto-scroll state
  ConsoleHistory.cs                   Persistent input history (PlayerPrefs)
  ConsoleAnchor.cs                    Enum + bounds-rect math for top/bottom/left/right
  ConsoleColorTagParser.cs            <color> tag → TextSpan[]
  ConsoleEvents.cs                    ConsoleOpenedEvent / ConsoleClosedEvent

Assets/Scripts/Core/Console/Registry/
  ConsoleRegistry.cs                  Static command table + reflection scan
  CommandData.cs                      Typed command metadata
  ParameterData.cs                    Typed parameter metadata
  ConsoleCommandAttribute.cs
  CommandPrefixAttribute.cs
  ParamDescriptionAttribute.cs
  CompletionSourceAttribute.cs
  MonoTargetType.cs
  IConsoleArgumentParser.cs
  ConsoleArgumentParsers.cs           int/float/bool/string/Vector2/Vector3/Color/enum parsers
  CommandParser.cs                    Tokenizer + arg binding
  CommandExecutor.cs                  Target resolution + invocation + async handling
  IConsoleCompletionProvider.cs
  EnumCompletionProvider.cs           Default for enum parameters

Assets/Scripts/Core/Console/Intellisense/
  IntellisenseEngine.cs               Suggestion generation, ranking
  Suggestion.cs                       Struct

Assets/Scripts/Core/Console/Commands/
  ConsoleBuiltins.cs                  help, clear, history, echo, quit, console.*

Assets/Graphics/Shaders/Hidden/
  ConsoleOverlay.shader               Backdrop fullscreen shader

(Per-module command attributes live on the existing target classes,
 not in dedicated Commands/ files. Example: WeatherManager.cs gets
 [CommandPrefix("weather")] and [ConsoleCommand("wind-speed")] inline.)
```

## Implementation Slicing

Each slice ships with `dotnet build` clean + Bryan F10 validation before the next starts. Same cadence as the grass arc.

| Slice     | Scope                                                                                                  | Est. lines | Risk                                         |
| --------- | ------------------------------------------------------------------------------------------------------ | ---------- | -------------------------------------------- |
| CONSOLE-0 | Input system migration (prerequisite — non-console work)                                               | ~150       | Low — refactor only                          |
| CONSOLE-1 | Overlay shader + ConsoleController skeleton + IConsoleService + open/close + input map swap + EventBus | ~400       | Medium — first ship of the visual layer      |
| CONSOLE-2 | Reflection scan + CommandData + 3 target modes + arg parsers + executor (sync only)                    | ~450       | Medium — typed registry needs careful design |
| CONSOLE-3 | Intellisense engine + tab cycle + parameter completion providers + color tag parser                    | ~350       | Medium-High — UX-heavy, lots of edge cases   |
| CONSOLE-4 | Scrollback + auto-scroll + history + persistence + page scroll + async commands                        | ~300       | Medium                                       |
| CONSOLE-5 | Built-in commands (5 console + 12 game) + console-triggered F10 + --allowDebug gate                    | ~250       | Low                                          |

**Total: ~1900 lines (~9 files in 4 folders + ~12 attribute additions on existing classes + 1 shader).**

Each slice gives a usable increment:

- After CONSOLE-1: backtick opens an empty console
- After CONSOLE-2: commands work but no autocomplete
- After CONSOLE-3: intellisense + color works
- After CONSOLE-4: scrollback + history persist; async commands run
- After CONSOLE-5: full game integration

## F10 Stats / Diagnostics

`--- DebugConsole ---` block in F10 sidecar:

```text
Controller: active=True
State: open=False, anchor=top, height=0.33
Commands: registered=23, builtins=5, game=18
History: lines=87, scrollback=412/1000
```

Useful for confirming the registry built correctly and verifying state in captures.

## Risks / Open Questions

### Risks

1. **Reflection scan robustness.** Some assemblies (Editor-only, third-party) may throw on `GetTypes()`. Wrap in try-catch per-assembly, log warnings, continue.
2. **PlayerPrefs string size.** History at 100 lines × 200 chars = 20KB. PlayerPrefs handles this fine but worth keeping the cap reasonable.
3. **Text mesh rebuild cost.** Output mesh rebuilds whenever scrollback changes. Could rebuild every frame in a worst case (rapid output). Mitigate: rebuild on demand only when scrollback dirty, throttle to one rebuild per frame.
4. **Async command queuing.** v1 rejects subsequent commands while one is async. Could feel weird. Solution: only reject _async_ commands; sync commands can still execute (most common case).
5. **Input map conflict.** If a future feature uses backtick for something else, the map design needs to keep backtick always-routed-to-console. Document this.
6. **Color tag escape.** If a command prints user-provided text (e.g., chat name) that contains `<color`, it could be interpreted as markup. Add a `Print` overload `PrintEscaped` that escapes `<` first.

### Open questions for review

1. **Per-line timestamp display?** Useful but adds visual noise. Default off, expose as `console.show-timestamps <bool>`?
2. **Output line truncation.** Very long lines (e.g., a 500-char log) wrap or truncate? Wrap in v1, with `console.wrap-mode <wrap|truncate>` toggle.
3. **Should `IConsoleService.RunCommand` be exposed to non-console callers** (so other systems can programmatically issue commands)? Useful for scripted sequences. Default: yes, public on the interface.
4. **Output color defaults.** Info = white, Warn = yellow, Error = red. Standard. Confirm.

## Acceptance Criteria

Slice CONSOLE-5 complete when:

1. **Backtick toggles**, Esc closes, gameplay input gated while open.
2. **EventBus events fire** on open/close.
3. **All 17+ commands** execute correctly. Bad commands print red error without crashing.
4. **Intellisense suggests** as you type. Tab accepts. Substring + prefix matching both work.
5. **Color tags render** correctly. Red errors visible. Help text highlights.
6. **PageUp/Down scrolls.** Auto-scroll re-engages on Home or End.
7. **History persists** across game restart.
8. **`debug.capture grass` captures cleanly** with no console in the screenshot.
9. **Release build without `--allowDebug`** has no console (verify via temporary release build).
10. **F10 sidecar shows `--- DebugConsole ---` block.**

## Approvals needed

- [ ] Bryan: greenlight prerequisite slice CONSOLE-0 (input migration) → CONSOLE-1..5 sequence
- [ ] Codex: review architecture, push back on anything missed
- [ ] Bryan: answer open questions 1-4 above (or "your call" and I pick defaults)
- [ ] Bryan: confirm the 17 v1 commands list (add/remove before CONSOLE-5)

Once aligned, slice CONSOLE-0 starts.
