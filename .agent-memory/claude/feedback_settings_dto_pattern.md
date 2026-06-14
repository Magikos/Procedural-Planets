---
name: feedback-settings-dto-pattern
description: "Settings ScriptableObjects are editor surface only — runtime consumers must read immutable snapshot DTOs, never the SO directly. Prevents god-object cross-coupling."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
---

**Rule:** Editor-authored Settings ScriptableObjects (BiomeSettings, AtmosphereSettings, CloudSettings, PlanetSettings, future TextureBlendConfig, etc.) are **editor-side only**. Runtime consumers MUST receive an immutable snapshot DTO (`readonly struct` with only the fields they need), not the SO itself.

**Why:** Established 2026-06-06 after the BiomeSettings refactor pain. The original pattern was every consumer reads `settings.X` directly (`VoronoiBiomeField.Build(_biomeSettings, ...)`, `TemperatureProvider.Initialize(settings.X, settings.Y, ...)`, console commands doing `settings.Field = value`). Result: BiomeSettings grew to 19 fields with **37 direct read sites across 4 files**. Reorganizing/renaming any field became a wide refactor; passing the whole SO down made consumer dependencies opaque (you couldn't tell from a method signature what fields a function actually depended on). Bryan called this out as the **cross-interdependence trap** he preaches against and the reason a cosmetic "nested struct" cleanup became risky.

**How to apply:**

Three roles, decoupled:

```csharp
// 1. Editor surface — Unity SO. Fields free to grow/reorganize/rename.
public sealed class BiomeSettings : ScriptableObject { ... }

// 2. Runtime contract — immutable DTO. Consumer only knows about THIS.
public readonly struct VoronoiFieldConfig
{
    public readonly int SeedCount;
    public readonly float SeedJitter;
    // ... only what Voronoi needs
}

// 3. Composition root — ONE place that knows both sides.
static VoronoiFieldConfig BuildVoronoiConfig(BiomeSettings src) => new(...);

// Consumer — signature-honest about what it depends on.
VoronoiBiomeField.Build(in VoronoiFieldConfig config, ...);
```

When this kicks in:
- **Any new Settings-like SO** — author the DTO + builder at the same time as the SO. Don't ship the SO without them.
- **Any new consumer of an existing SO** — extract the DTO it needs at that point. Don't read `settings.X` directly even if the precedent in older code does.
- **Console command setters** — they're the editor-side path; they can write to the SO. But they should never depend on internal subsystem fields not in the editor authoring surface.

**Status 2026-06-11:** Retrofit complete for all four hub SOs (Atmosphere, Cloud, Biome, Planet) via the [[project-code-refactor-arc]]. The pattern matured during the Biome+Planet arc — use `sealed record` (not `readonly struct`) so commands can do `dto with { Field = value }` and route through `SettingsProvider.Update<TDto>`. Pure helper classes (e.g. `ClimateCurves`) handle reference-type fields like AnimationCurve. CLAUDE.md captures the full mechanics.

For NEW Settings-style SOs: author DTO + `From(SO)` + register-if-not-registered boot at the same time as the SO. Don't ship the SO without them.

Cost per subsystem after the pattern is internalized: ~30-50 lines (record + factory + boot wiring). Pays back the first time you reorganize fields or want to test a consumer in isolation.

Related: [[project-console-arc]] (where the original sin pattern is most visible — `settings.SunIntensity = value`-style command setters that touch SO fields directly).
