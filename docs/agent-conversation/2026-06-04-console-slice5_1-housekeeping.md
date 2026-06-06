# 2026-06-04 — Debug Console Slice CONSOLE-5.1: Housekeeping Built-ins + F10 Sidecar

**Status:** Shipped. Awaiting Bryan validation on `quit` confirm, `console.anchor`/`console.scrollback-size` get/set round-trip, and F10 sidecar `--- DebugConsole ---` block.

**Goal:** Land the foundation (nullable arg parsers + interface extensions) and the housekeeping built-ins, so the next sub-phase can decorate game systems freely.

## Files

**New (1):**

- [`Console/ConsoleDebugModule.cs`](../../Assets/Scripts/Core/Console/ConsoleDebugModule.cs) — `IDebugCaptureMetadataProvider` for F10 sidecar

**Modified (6):**

- [`Console/Registry/ConsoleArgumentParsers.cs`](../../Assets/Scripts/Core/Console/Registry/ConsoleArgumentParsers.cs) — unwrap `Nullable<T>` at the top of `TryParse`; every parser now transparently handles `int?` / `float?` / `bool?` / etc.
- [`Console/Intellisense/IntellisenseEngine.cs`](../../Assets/Scripts/Core/Console/Intellisense/IntellisenseEngine.cs) — `GetProvider` unwraps `Nullable<EnumT>` so enum-completion still fires for nullable enum parameters
- [`Console/IConsoleService.cs`](../../Assets/Scripts/Core/Console/IConsoleService.cs) — added `Confirm(question, onYes, onNo)`, `ScrollbackCapacity` (get/set), `GetDiagnostics()` + `ConsoleDiagnostics` struct
- [`Console/ConsoleController.cs`](../../Assets/Scripts/Core/Console/ConsoleController.cs) — public `ScrollbackCapacity`; `Confirm` delegates to existing private `ShowConfirm`; `GetDiagnostics` snapshots state
- [`Console/Commands/ConsoleBuiltins.cs`](../../Assets/Scripts/Core/Console/Commands/ConsoleBuiltins.cs) — added `quit`, `console.anchor`, `console.scrollback-size`
- [`Core/Services/DebugCaptureController.cs`](../../Assets/Scripts/Core/Services/DebugCaptureController.cs) — one line: registers `ConsoleDebugModule`

## Key design call — nullable parsers enable the get/set pattern

Bryan flagged the Quantum convention: bare alias = print current value, alias + arg = set new value. To support that without sentinel hacks, every primitive parser now transparently accepts `T?`:

```csharp
// Top of ConsoleArgumentParsers.TryParse
if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
    type = type.GetGenericArguments()[0];
```

Now a command like:

```csharp
[ConsoleCommand("planet.seed")]
public static string Seed(int? newSeed = null)
{
    if (newSeed == null) return $"current seed: {currentSeed}";
    SetSeed(newSeed.Value);
    return $"seed set to {newSeed.Value}";
}
```

"just works" — `planet.seed` prints, `planet.seed 123` sets. Same applies to `console.anchor`, `console.scrollback-size`, and all Phase 2 commands.

`IntellisenseEngine.GetProvider` also unwraps `Nullable<EnumT>` so the enum-completion popup still fires for nullable enum parameters (`console.anchor` benefits).

## F10 sidecar block

```
--- DebugConsole ---
Open: True, Anchor: Top
Scrollback: 87/1000
History: 12 entries
Commands registered: 23
Pending: 'test.console.async-cancellable' running 3.14s, cancellable=True
```

`ConsoleDebugModule` registers itself as `IDebugCaptureMetadataProvider` and queries `IConsoleService.GetDiagnostics()` at capture time. Graceful when the console is disabled (release build without `--allowDebug`):

```
--- DebugConsole ---
Status: disabled (release build without --allowDebug, or pre-bootstrap capture)
```

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning)
- ~120 net lines across 7 files

## Validation guidance

1. **`console.anchor`** → `current anchor: Top`. **`console.anchor Bottom`** → `anchor set to Bottom`, console snaps to bottom 1/3 of screen. **`console.anchor`** → `current anchor: Bottom`. Set back to Top.
2. **`console.scrollback-size`** → `current scrollback capacity: 1000 lines`. **`console.scrollback-size 50`** → trims to 50, prints confirmation.
3. **`quit`** → modal: `Quit the application?   [ ] Yes   [*] No`. Tab to Yes, Enter → editor exits Play (or built game closes).
4. **F10 sidecar** → press F10, check the `.txt` next to the saved PNG. New `--- DebugConsole ---` block near the end with current state.
5. **`help console.anchor`** → shows the optional `[anchor]` param hint (nullable enum). Type a space — popup shows `Top / Bottom / Left / Right` (enum completion still works for nullable enums).

## What's next

Bryan dropped a substantial Phase 2 inventory covering **time / planet / camera / quality / atmosphere / cloud / weather / precipitation / lightning / debug / grass / scale / action** namespaces. Roughly 40+ commands across 13 subsystems. Next step is a per-system API survey + tiered phase plan.
