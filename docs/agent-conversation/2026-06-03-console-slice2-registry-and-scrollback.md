# 2026-06-03 — Debug Console Slice CONSOLE-2: Registry, Executor, Scrollback, Log Capture

**Status:** Shipped. Awaiting Bryan validation (type `echo hello` → "hello" appears; `help` lists commands; Unity `Debug.Log` mirrors into scrollback).

**Design doc:** [docs/design/2026-06-03-debug-console.md](../design/2026-06-03-debug-console.md) — slice CONSOLE-2 (+ early absorption of mini-scrollback from CONSOLE-4 to enable `Debug.Log` mirroring without recursion).

**Goal:** Type commands; they execute; output appears in a scrollback above the input line. Unity `Debug.Log` / `LogWarning` / `LogError` / exceptions all mirror to the scrollback so build-without-Unity testing is viable.

## Scope expansion (vs. design doc)

Bryan asked for `Debug.Log` capture as part of slice 2. Capturing into the existing `Print(...) → LoggerProvider → Debug.Log` route would recurse infinitely, so a real scrollback buffer was required NOW. The minimum scrollback (ring buffer + bottom-up render) is small, so it joined slice 2.

**CONSOLE-4** scope shrinks correspondingly. It now covers: PageUp/Down navigation, auto-scroll-unless-scrolled UX, persistent command history, async command awaiting. The ring buffer + log capture + basic output rendering are done.

## Files

**New (15):**

Registry (under `Assets/Scripts/Core/Console/Registry/`):
- [`ConsoleCommandAttribute.cs`](../../Assets/Scripts/Core/Console/Registry/ConsoleCommandAttribute.cs)
- [`CommandPrefixAttribute.cs`](../../Assets/Scripts/Core/Console/Registry/CommandPrefixAttribute.cs)
- [`ParamDescriptionAttribute.cs`](../../Assets/Scripts/Core/Console/Registry/ParamDescriptionAttribute.cs)
- [`CompletionSourceAttribute.cs`](../../Assets/Scripts/Core/Console/Registry/CompletionSourceAttribute.cs) (forward-compat for CONSOLE-3)
- [`MonoTargetType.cs`](../../Assets/Scripts/Core/Console/Registry/MonoTargetType.cs) — `Static` / `Single` / `Registry`
- [`ParameterData.cs`](../../Assets/Scripts/Core/Console/Registry/ParameterData.cs)
- [`CommandData.cs`](../../Assets/Scripts/Core/Console/Registry/CommandData.cs) — detects `Awaitable` / `Awaitable<T>` return at scan-time
- [`IConsoleArgumentParser.cs`](../../Assets/Scripts/Core/Console/Registry/IConsoleArgumentParser.cs)
- [`ConsoleArgumentParsers.cs`](../../Assets/Scripts/Core/Console/Registry/ConsoleArgumentParsers.cs) — int, float, bool, string, Vector2, Vector3, Color, enum
- [`ConsoleRegistry.cs`](../../Assets/Scripts/Core/Console/Registry/ConsoleRegistry.cs) — assembly scan, alias table, instance registry
- [`CommandParser.cs`](../../Assets/Scripts/Core/Console/Registry/CommandParser.cs) — tokenize (quoted strings supported) + bind
- [`CommandExecutor.cs`](../../Assets/Scripts/Core/Console/Registry/CommandExecutor.cs) — resolve target, invoke, format errors

Console core (under `Assets/Scripts/Core/Console/`):
- [`ConsoleScrollback.cs`](../../Assets/Scripts/Core/Console/ConsoleScrollback.cs) — ring buffer (cap 1000), `Version` counter for renderer dirty-tracking
- [`ConsoleInputBuffer.cs`](../../Assets/Scripts/Core/Console/ConsoleInputBuffer.cs) — `StringBuilder` wrapper with Backspace/Clear

Commands (under `Assets/Scripts/Core/Console/Commands/`):
- [`ConsoleBuiltins.cs`](../../Assets/Scripts/Core/Console/Commands/ConsoleBuiltins.cs) — `echo`, `help`, `clear`

**Modified (4):**

- [`IInputMapService.cs`](../../Assets/Scripts/Core/Interfaces/IInputMapService.cs) — `ConsoleSubmit` (Enter / NumpadEnter) + `ConsoleBackspace` (Backspace) actions
- [`InputMapService.cs`](../../Assets/Scripts/Core/Services/InputMapService.cs) — bindings for the above on Console map
- [`ConsoleController.cs`](../../Assets/Scripts/Core/Console/ConsoleController.cs) — text input hook (`Keyboard.current.onTextInput`), Submit/Backspace handling, scrollback writes, `Application.logMessageReceived` hook, `RunCommand` wired to `CommandExecutor.Execute`
- [`ConsoleRenderer.cs`](../../Assets/Scripts/Core/Console/ConsoleRenderer.cs) — second `SDFTextRenderer` for output text, dirty-tracking via scrollback `Version`, bottom-up layout with newest line just above input
- [`DebugConsoleBootstrap.cs`](../../Assets/Scripts/Core/Console/DebugConsoleBootstrap.cs) — calls `ConsoleRegistry.Scan()` before creating the controller, logs the count
- `ProceduralPlanets.Core.csproj` — 15 new `<Compile Include=...>` entries (Unity will regenerate on next Editor refresh)

## How a command flows end to end

1. User opens console (backtick) → input map swap → `Keyboard.current.onTextInput += OnTextInput` subscribed inside `Open()`
2. User types `echo hello` → each char arrives in `OnTextInput`, appends to `ConsoleInputBuffer`. Backtick/tilde are filtered. Control chars are filtered.
3. User presses Enter → `ConsoleSubmit.WasPerformedThisFrame()` true → `SubmitInputLine()` → scrollback gets `> echo hello`, buffer clears, `CommandExecutor.Execute("echo hello", this)` called
4. Executor calls `CommandParser.Tokenize(...)` → `["echo", "hello"]`
5. `ConsoleRegistry.TryGet("echo", out cmd)` → found (alias is case-insensitive)
6. `CommandParser.TryBind(cmd, ["hello"], out args, out err)` → walks parameters, for each calls `ConsoleArgumentParsers.TryParse(...)` to coerce. String parser takes `"hello"` directly.
7. `ResolveTarget(cmd)` → `MonoTargetType.Static` → returns `null`
8. `cmd.Method.Invoke(null, args)` → `ConsoleBuiltins.Echo("hello")` → returns `"hello"`
9. Executor sees `string` return → `console.PrintLine("hello")` → scrollback gets `"hello"`
10. Renderer notices `Scrollback.Version` changed, rebuilds output mesh, draws newest line just above input

If the user types `weather.wind-speed 3.5` (no such command yet), step 5 returns false → `PrintError("unknown command: 'weather.wind-speed' — try 'help'")` → scrollback gets `[Error] unknown command: ...`. Game keeps running.

## Log capture

`Application.logMessageReceived` is the main-thread variant (vs. `logMessageReceivedThreaded`); subscribed in `OnEnable`, unsubscribed in `OnDisable`. Handler prefixes by severity:

| Unity LogType | Scrollback prefix |
| ------------- | ----------------- |
| Log           | (none)            |
| Warning       | `[Warn]`          |
| Error         | `[Error]`         |
| Exception     | `[Exception]`     |
| Assert        | `[Assert]`        |

The scrollback accumulates even when the console is **closed**. Open the console mid-session and prior `Debug.Log` history is right there.

**No recursion risk** because `Print` writes directly to the scrollback. It does NOT call `LoggerProvider`/`Debug.Log`, so the log hook never feeds back into itself.

## Text input pattern

- `Keyboard.current.onTextInput` provides per-character Unicode events with shift/CapsLock/IME pre-resolved — no need to enumerate every printable key as an action
- Subscription is bound to console open/close lifecycle. Subscribed in `Open()`, unsubscribed in `Close()` (and `OnDisable`)
- Filters in `OnTextInput`:
  - Backtick (`` ` ``) and tilde (`~`) — toggle keys, never appended even if the keypress that opened the console somehow leaks one
  - Control chars (`< 0x20`) and DEL (`0x7F`) — Enter/Backspace come via Input Actions; nothing else needs raw control-char input
- Enter / Backspace via `ConsoleSubmit` / `ConsoleBackspace` Input Actions on the Console map (consistent gating with the rest of the console map swap)

## Output rendering layout

Top anchor, bottom-up:

```
y=1.0   ┌────────────────────────────┐ ← backdrop top, cyan border
        │ [Error] unknown command... │ ← oldest visible (top)
        │ [Warn] some warning        │
        │ > echo hello               │
        │ hello                      │ ← newest line, sits just above input
        │ > _                        │ ← input line (cursor blinks @ 2 Hz)
y=0.66  └────────────────────────────┘ ← backdrop bottom
```

Math:
- Input baseline: `bounds.yMin + 0.008` ≈ `0.668`
- Em size: `0.022` (~24 px @ 1080p)
- Line spacing: `emSize * 1.3` ≈ `0.0286`
- Max visible lines = `floor((bounds.yMax - inputY - lineSpacing) / lineSpacing)` → 10 for Top anchor
- Output text origin Y = `inputY + lineSpacing * visibleCount` — so the bottom-most line always sits one line height above input. When scrollback has fewer than `maxLines`, the output collapses upward, leaving the area between the top edge and the lines blank.

Output mesh rebuilds only when `Scrollback.Version` changes OR `visibleCount` changes (e.g., scrollback grew past `maxLines`). One-frame string join + mesh build cost; SDFTextRenderer dedupes internally as a second guard.

## Built-ins shipped

| Alias    | Args              | Description                                           |
| -------- | ----------------- | ----------------------------------------------------- |
| `echo`   | `[text=""]`       | Print text back. Proof-of-life for the round trip.    |
| `clear`  | —                 | Clear scrollback                                      |
| `help`   | `[name=""]`       | List all commands, or describe one (signature + arg descriptions) |

`help` walks the registry alphabetically. `help echo` shows the signature with parameter types and descriptions.

## Reflection scan

`ConsoleRegistry.Scan()` runs once at bootstrap. For every assembly in `AppDomain.CurrentDomain.GetAssemblies()`:

- Wrap `GetTypes()` in try/catch — `ReflectionTypeLoadException` returns partial type list, other exceptions skip the assembly
- For each type: read optional `[CommandPrefix]`, then for each method scan for `[ConsoleCommand]` attributes
- Build `CommandData` per alias; conflicts log a warning and keep the first registration

Time cost: <50ms at our scale (a few hundred user types + Unity assemblies). Bryan can verify by looking at the log line `"Debug console initialized. ... N commands registered."` on startup.

## Stub-to-real upgrades vs. slice 1

| Method        | Slice 1 (LoggerProvider stub) | Slice 2                                                    |
| ------------- | ----------------------------- | ---------------------------------------------------------- |
| `Print`       | Log via `LoggerProvider.Info` | Append to scrollback directly                              |
| `PrintLine`   | Log via `LoggerProvider.Info` | Append to scrollback directly                              |
| `PrintError`  | Log via `LoggerProvider.Error`| Append to scrollback prefixed `[Error]`                    |
| `RunCommand`  | Warning that registry pending | Echo prompt, then `CommandExecutor.Execute`                |
| `Clear`       | No-op                         | `_scrollback.Clear()`                                      |

`PrintLine` and `Print` collapse to the same behavior for slice 2 (both add a new line). Quantum's distinction (Print appends to current line, PrintLine starts a new one) can be split later if we ever need partial-line output — none of our built-ins need it.

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning in `DebugCaptureController.cs`)
- `dotnet build ProceduralPlanets.Planet.csproj` — clean (only pre-existing `CS0414` warning in `Planet.cs`)
- 15 new files + 5 modified
- Net `+780` lines (vs. expanded design estimate `~600`; slightly over because the executor + argument parsers needed proper error messages to be useful)

## Validation guidance for Bryan

In Play mode:

1. **Open console (backtick)** — backdrop fades in as before
2. **Type `help`, press Enter** — list of registered commands appears (should be at least `clear`, `echo`, `help`)
3. **Type `echo hello world`, press Enter** — line "hello world" appears in scrollback above your input
4. **Type `help echo`, press Enter** — signature + parameter descriptions print
5. **Type `clear`, press Enter** — scrollback wipes; only the cleared `> clear` then the next prompt remains (because the prompt-echo line is added BEFORE clear executes; that's expected — slice 4 can revisit)
6. **Type `bogus`, press Enter** — `[Error] unknown command: 'bogus' — try 'help'` appears in red-ish text (no actual color yet — that's CONSOLE-3)
7. **Type `echo with "quoted spaces"`, press Enter** — should print `with` then... wait, that's two arguments to `echo`, the second one taking the quoted-string. Actually `echo` only takes one optional arg, so `echo "hello world"` should print `hello world` correctly. `echo with "quoted spaces"` will error with "too many arguments". Test both.
8. **Close console (Esc or backtick)**, do some camera movement, watch the **Unity Console** for some normal `Debug.Log` messages (e.g., during planet generation). **Reopen console** — those `Debug.Log` lines should be visible in the scrollback, with `[Warn]` / `[Error]` prefixes where applicable.
9. **Backspace** — should delete last typed character
10. **Enter on empty line** — no-op, no error, just doesn't submit anything

If any of those misbehaves, the slice log captures the design decisions for diagnosis. The most likely friction points:
- **Long log spam fills scrollback** — there's no auto-scroll suppression yet (CONSOLE-4 ships that). For now, scrollback always shows the latest N visible lines.
- **No left/right cursor on input line** — you can only edit at the end with Backspace. Slice 3 (intellisense) adds caret movement.
- **No color tags** — `<color=red>` literally renders as text. CONSOLE-3 ships the color tag parser.

## What's next

**CONSOLE-3** (per design doc, ~350 lines): intellisense engine, tab cycle, parameter value completion providers, color tag parser → `TextSpan[]` (the SDF builder gets a new multi-color overload). After CONSOLE-3, typing `weat` shows suggestions, Tab accepts; errors print in actual red; auto-enum-completion for any `enum` parameter.

But first: confirm slice 2 validates. If it does, CONSOLE-3 starts on your word.
