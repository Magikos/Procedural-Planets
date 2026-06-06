# 2026-06-04 — Debug Console Slice CONSOLE-5.4: Math in Numeric Arguments

**Status:** Shipped. Awaiting Bryan validation on `time.speed 60*60`-style expressions.

**Trigger:** Bryan asked how Quantum supports math in console args. Tiny dedicated slice to add the same.

## Files

**New (1):**

- [`Console/Registry/ExpressionEvaluator.cs`](../../Assets/Scripts/Core/Console/Registry/ExpressionEvaluator.cs) — recursive-descent evaluator for `+ - * /`, parens, unary +/-. ~120 lines, zero dependencies.

**Modified (1):**

- [`Console/Registry/ConsoleArgumentParsers.cs`](../../Assets/Scripts/Core/Console/Registry/ConsoleArgumentParsers.cs) — `IntParser` and `FloatParser` route the token through `ExpressionEvaluator.TryEvaluate` instead of `int.TryParse` / `float.TryParse`. Simple numbers like `"3.14"` still parse correctly (single-number is a valid expression).

## What works

| Input                              | Result   | Notes |
| ---------------------------------- | -------- | ----- |
| `time.speed 60*60`                 | `3600`   | hour in seconds                                            |
| `time.speed 24*60*60`              | `86400`  | day in seconds                                             |
| `planet.seed 100*100+5`            | `10005`  | int param, integer result                                  |
| `planet.seed -1`                   | `-1`     | unary minus                                                |
| `planet.seed (2+3)*4`              | `20`     | parens + precedence                                        |
| `weather.wind-speed 1.0/3.0`       | `0.333…` | division on float                                          |
| `planet.seed 7/2`                  | **error** | rejected: int param, expression evaluates to 3.5 (non-integer) |
| `time.speed 10/0`                  | **error** | rejected: division by zero                                  |
| `time.speed 5 + 5`                 | **error** | tokenizer splits on whitespace — math expressions must be a single token (no spaces) |

## Why no whitespace inside expressions

The console's tokenizer splits arguments on whitespace. `time.speed 5 + 5` would parse as three arg tokens, not one expression. Matches Quantum's behavior.

If you genuinely need spaces for readability, quote the expression: `time.speed "5 + 5"` (the StringTokenizer keeps quoted text together). Untested but should work — the expression evaluator does `SkipWhitespace` between operators.

## Grammar (for reference)

```
expression := term (('+' | '-') term)*
term       := factor (('*' | '/') factor)*
factor     := '(' expression ')' | ('+' | '-') factor | number
number     := digits ('.' digits)?
```

No constants (`pi`, `e`), no functions (`sin`, `sqrt`), no exponentiation (`^` or `**`). YAGNI; add when first needed.

## Categorization tracker (unchanged)

| Category | Commands so far |
| -------- | --------------- |
| **Debug-only** | `scale.*`, `debug.*`, `weather.diagnostics`, `test.console.*` |
| **Settings** | `camera.*`, `quality.*`, `time.freeze`, `time.speed`, `weather.wind-*`, `console.*`, `quit`, `clear`, `echo`, `help` |
| **Console internal** | `console.abandon`, `console.cancel` |

## Build status

- `dotnet build ProceduralPlanets.Core.csproj` — clean (only pre-existing `CS0162` warning)
- ~150 net lines

## Out-of-scope items raised during this slice

Bryan noticed two visual issues while testing `weather.wind-speed`:

1. **Grass wind looks robotic/uniform** — the slice 5a grass wind algorithm is a fixed `worldPhase + clumpEnvelope + bladeJitter` with one wave frequency. Lacks turbulence and gust variability. **Filed for a future grass-wind-v2 slice.**
2. **Grass and clouds move in opposite directions for the same `_WindDirection`** — one of the two shaders is sign-flipping somewhere. **Filed for a wind-sync diagnostic slice + visualization shader.**

Both are off the console arc — separate Phase 8 / shader work.

## What's next

Back to Phase 2b proper. Next survey: **AtmosphereController + CloudController**.
