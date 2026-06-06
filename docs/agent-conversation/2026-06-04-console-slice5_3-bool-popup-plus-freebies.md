# 2026-06-04 — Debug Console Slice CONSOLE-5.3: Bool Popup + Already-Public-Field Decoration

**Status:** Shipped. Quick polish slice triggered by Bryan's Phase 2a feedback.

## Files

**New (1):**

- [`Console/Intellisense/BoolCompletionProvider.cs`](../../Assets/Scripts/Core/Console/Intellisense/BoolCompletionProvider.cs) — returns `true` / `false` with substring matching + prefix-ranked-first, same shape as `EnumCompletionProvider`

**Modified (3):**

- [`IntellisenseEngine.GetProvider`](../../Assets/Scripts/Core/Console/Intellisense/IntellisenseEngine.cs) — unwraps `Nullable<bool>` and auto-attaches `BoolCompletionProvider` for `bool` / `bool?` params
- [`Planet/WeatherManager.cs`](../../Assets/Scripts/Planet/WeatherManager.cs) — added `weather.wind-speed [float?]` (wraps already-public `Speed`) and `weather.wind-direction [Vector3?]` (wraps `WindDir`)
- [`Planet/CelestialManager.cs`](../../Assets/Scripts/Planet/CelestialManager.cs) — added `time.speed [float?]` (wraps `DayLengthSeconds`)

## Validation

1. **Bool popup:** `time.freeze ` (with trailing space) → popup with `true` / `false`. Type `t` → narrows to `true`. Tab accepts.
2. **`weather.wind-speed`** → reads current. `weather.wind-speed 3.0` → faster wind, clouds visibly move quicker.
3. **`weather.wind-direction`** → reads current `Vector3`. `weather.wind-direction 0,1,0` → wind blows upward (test). `weather.wind-direction 1,0,0.3` → restore.
4. **`time.speed 30`** → day cycle compresses to 30 real seconds (visible sun motion speeds up).

## Categorization tracker (for future `[DebugOnly]` filtering)

| Category | Commands so far |
| -------- | --------------- |
| **Debug-only** (strip at release) | `scale.*`, `debug.*`, `weather.diagnostics`, `test.console.*` |
| **Settings** (could survive release) | `camera.*`, `quality.*`, `time.freeze`, `time.speed`, `weather.wind-speed`, `weather.wind-direction`, `console.*`, `quit`, `clear`, `echo`, `help` |
| **Console internal** | `console.abandon`, `console.cancel` (always available during release if console is enabled) |

When we get to release-prep, the filter is one line in `ConsoleRegistry.Scan` — skip commands whose attribute carries `[DebugOnly]` or whose prefix is in a debug-prefix set.

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning)
- ~40 net lines

## What's next

**Phase 2b proper** — per-system surveys + extensions for atmosphere / cloud / precipitation / lightning / grass / action / time.set-local / time.moon-phase / weather.precipitation / quality.cloud-steps.

I'll start by reading one subsystem at a time, listing what's publicly mutable vs. what needs extension, and proposing a per-subsystem mini-slice. First on the list: **atmosphere + cloud** (related visual systems).
