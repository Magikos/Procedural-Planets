# 2026-06-04 — Debug Console Slice CONSOLE-5.13: planet.seed / planet.resolution / planet.generate

**Status:** Shipped. Final command slice of Phase 2 — closes out the per-system console-decoration arc.

## Files

**Modified (3):**

- [`Core/Interfaces/ISeedProvider.cs`](../../Assets/Scripts/Core/Interfaces/ISeedProvider.cs) — added `void SetWorldSeed(int)`
- [`Core/Services/SeedProvider.cs`](../../Assets/Scripts/Core/Services/SeedProvider.cs) — `WorldSeed` setter changed to `private set`; `SetWorldSeed` implementation
- [`Planet/Planet.cs`](../../Assets/Scripts/Planet/Planet.cs) — `[CommandPrefix("planet")]` + 3 commands

## Commands shipped (3)

### `planet.seed [int?]` (`MonoTargetType.Single`)

- No arg → prints current `Planet.Seed` AND `ISeedProvider.WorldSeed` (they differ because Planet derives via `GetSeedForSystem("Planet")`)
- With int → calls `ISeedProvider.SetWorldSeed(value)`. **Does NOT auto-regenerate** — user runs `planet.generate` afterward
- Print confirms: `"world seed set to N. Run 'planet.generate' to apply."`

### `planet.resolution [int?]` (`MonoTargetType.Single`)

- No arg → prints `PerFaceResolution`
- With int → clamps to [2, 256], updates `PerFaceResolution`. Same "no auto-regen" pattern.

### `planet.generate [int? seed] [float? radius]` (`MonoTargetType.Single`, async, cancellable)

- Throws if `IsGenerating` (caught by `CommandExecutor`, displays as red error)
- If seed given → applies via `SetWorldSeed`
- If radius given → clamps to ≥100, updates `_planetSettings.PlanetRadius`
- Calls existing `GeneratePlanetAsync(ct)` — re-uses the project's already-cancellable generation pipeline
- Async UX kicks in: spinner with elapsed time + `console.cancel` works mid-generation

Examples:
- `planet.generate` → regen with current seed/radius
- `planet.generate 42` → set seed to 42, regen
- `planet.generate 42 7000` → set seed AND radius, regen

## Design call — set commands don't auto-regenerate

Bryan's earlier framing: `planet.seed 123 -> sets seed`. Didn't specify auto-regen. The safest UX is **lazy mutation** — set the value, defer the heavy work until the user explicitly asks for it. Same model: less surprise, faster individual commands, batching when desired.

```
planet.seed 42
planet.resolution 16
planet.generate              ← one regen with both new values
```

vs the auto-regen alternative which would regenerate twice. Print messages remind the user to run `planet.generate` after setting.

## `_isGenerating` re-entry guard

`GeneratePlanetAsync` already has `if (_isGenerating) return;` — silent bail. For console UX I wanted a visible error, so `GenerateCmd` checks first and throws `InvalidOperationException`. The exception is thrown synchronously before any `await`, so the returned `Awaitable` is faulted. `ObservePending` catches and prints as a red error in scrollback.

Could have called `IConsoleService.PrintWarning` directly and returned, but that produces a confusing "completed in 0.00s" line right after the warning. Throw is cleaner.

## `ISeedProvider.SetWorldSeed` propagation

When `WorldSeed` changes, derived seeds (`GetSeedForSystem`, `GetSeedForChunk`, etc.) immediately compute new values on next call. Subsystems that **cached** derived seeds at their own init (e.g., `CloudController`, `WeatherManager` likely) keep the old derived values until they're re-initialized.

For `planet.generate <newSeed>`, `Planet.Initialize` re-reads `seedProvider.GetSeedForSystem("Planet")` → planet shape uses the new seed. Other subsystems retain their old derived seeds — biomes change with the planet, but cloud noise / weather seeds do not. That's acceptable behavior for "regen planet with new seed" UX. A full-world reseed would need scene reload.

## Categorization tracker (final for Phase 2)

| Category | Commands |
| -------- | -------- |
| **Debug-only** | `scale.*`, `debug.*` (8 commands), `weather.diagnostics`, `precipitation.debug-mode`, `test.console.*` |
| **Settings** | `camera.*`, `quality.*`, `time.*`, `weather.wind-*`, `atmosphere.*`, `cloud.*`, `precipitation.intensity`, `lightning.*`, `console.*`, `quit`, `clear`, `echo`, `help` |
| **Gameplay / world state** | `action.*`, `planet.*` ← NEW |
| **Console internal** | `console.abandon`, `console.cancel` |

## Phase 2 totals

Across slices 5.0 → 5.13:

- **~60 console commands** across **13 prefixes** (camera, time, quality, scale, debug, weather, atmosphere, cloud, precipitation, lightning, action, planet, console)
- **Plus** test.console.* (7), console builtins (echo/clear/help/quit/console.*), and the bootstrap/registry infrastructure from slices 0-4
- **Total slice count for the console arc:** 22 slices (0 through 5.13) with full conversation logs per slice
- **Total net lines:** ~3,500 across console + integration + nullable parsers + completion providers + math evaluator + scrollback + intellisense + popup + confirm modal + key repeat + cursor + paste + ghost vs popup + ...

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (pre-existing CS0162 warnings only)
- `dotnet build ProceduralPlanets.Planet.csproj` — clean (pre-existing CS0414 warning only)
- 3 files modified, ~60 net lines this slice

## Validation guidance

1. **`planet.seed`** → prints current seed values.
2. **`planet.seed 42`** → "world seed set to 42. Run 'planet.generate' to apply." Nothing changes visually.
3. **`planet.generate`** → spinner runs through the existing generation pipeline. Console behavior identical to first-load: terrain → colors → water → ready.
4. **`planet.generate 42`** → combined; sets seed and regenerates in one command.
5. **`planet.generate 42 8000`** → seed 42, radius 8000m. Planet visibly resizes.
6. **`planet.generate 42 100`** → radius clamped to 100m (minimum).
7. **`console.cancel` during `planet.generate`** → modal appears (it's cancellable), Yes interrupts generation cleanly via the existing CancellationToken plumbing.
8. **Run `planet.generate` while one is in progress** → red error "planet generation already in progress".
9. **`planet.resolution 32` then `planet.generate`** → regen with new per-face vertex count.
10. **Math eval:** `planet.generate 42 5000+1000` → radius 6000m.

## What's next

**The console arc is functionally complete.** Bryan flagged a final phase early on:

> we also need to do a code review / cleanup and look for any loose ends / orphan code, areas of consolidation / refactor into better, etc

Recommend a CONSOLE-6 "audit + cleanup" slice that:

1. **Walks every Console/* file** looking for: dead code, duplicated logic, stale comments, naming inconsistencies
2. **Reviews the slice logs** for documented design decisions that diverged from the original design doc — note them, decide if any need reversal
3. **Surveys missed opportunities** — features the design doc proposed but we skipped (e.g., scrollbar polish, `Awaitable.WaitForSecondsAsync` cancellation propagation, color-tag in `help`)
4. **Final design doc update** — the original `docs/design/2026-06-03-debug-console.md` is now several iterations behind reality
5. **Categorization solidification** — convert the "tracker table" into a real `[DebugOnly]` attribute infrastructure so release builds can strip debug commands

Want me to scope CONSOLE-6, or call it done here?
