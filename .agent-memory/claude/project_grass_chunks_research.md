---
name: grass-chunks-research
description: "Phase 8 grass + chunked LOD planet research SOT; Phase A done, Phase B design doc drafted 2026-05-31"
metadata:
  type: project
---

Deep-dive reading + design synthesis + Bryan's review complete in [docs/research/2026-05-30-grass-and-chunks.md](../../docs/research/2026-05-30-grass-and-chunks.md). Bryan answered all 9 open questions on 2026-05-30 with significant scope expansion.

**Source of truth:** the **Locked-in Design** section at the bottom of the file. The "Proposed Design" section above it is historical (pre-feedback). Don't reread the proposal — go straight to the locked-in section.

**Why:** Audit workflow ([[feedback-audit-workflow]]) — proposal → user review → locked design → design doc → code. Same pattern is being followed for each phase.

**How to apply:** Phase A chunk skeleton done — see [docs/design/2026-05-30-chunk-skeleton.md](../../docs/design/2026-05-30-chunk-skeleton.md) (note the pre-cache pivot section at the bottom is the actual implementation, not the dynamic-subdivision design above). Phase B design doc drafted 2026-05-31 — see [docs/design/2026-05-31-biome-textures.md](../../docs/design/2026-05-31-biome-textures.md). [[project-current-focus]] tracks per-session next-action.

### Locked-in decisions (high level)

- **Two-resolution planet generation:** `IPlanetSurfaceProvider` with `PlanetResolution.Low` (existing per-face path, kept as-is) or `High` (new chunked path). Same generation API.
- **Hot-swappable abstractions everywhere:** `IWindFieldProvider` (v1 = GoT scrolling Perlin, future = CWD-Sim fluid), `IBiomeProvider` (open registry — supports Mushroom Land etc.), `IChunkPersistenceProvider` (v1 = PNG-per-chunk, future = real save system), `IGrassQualitySettings` (in-game settings menu drives blade count, NOT Inspector).
- **Half-chunk face-seam overlap** — Bryan explicitly rejected visible chunk lines at cube-face borders. Cross-face neighbor lookup is required (the LOD-Planets TODO).
- **Surface state is a STACK of 4 textures per chunk:** ForceMap (RGBA8), WeatherState (RGBA8 — wetness/snowDepth/burn/heat), TrackMap (R16 depth), SeasonalState (R8). ~44 KB/chunk × 256 chunks = ~11 MB.
- **v1 scope expanded** (per Bryan's answer to Q9): seasonal grass color, snow accumulation on tips, wetness affecting bend stiffness, footprints in dirt vs grass, burn/scorched grass from fire, dedicated snow system with deep-snow tracks (Phase F).
- **URP tessellation confirmed supported** on D3D11/12, Vulkan, Metal (Bryan's quoted answer to Q2). Still want a hello-world tess shader sanity check before Phase D starts.

### Revised phase plan

- Phase A: Chunk skeleton + Low/High resolution switch
- Phase B: `IBiomeProvider` + per-chunk biome map (parallel with C)
- Phase C: Surface state stack + `IWindFieldProvider` + `IChunkPersistenceProvider`
- Phase D: Grass renderer (JAHRMANN+GoT compute, quality-settings-driven)
- Phase E: Dynamic state API (modification, wetness, burn, footprints)
- Phase F: Snow system with deep-snow tracks

### Remaining open questions (deferred to per-phase design docs, do NOT block chunk skeleton)

1. Snow renderer approach (mesh layer vs POM in terrain shader) — Phase F
2. Fire event sources (lightning? torches? weapons?) — Phase E
3. Season clock global vs per-latitude — Phase B/E
4. Quality settings menu — does one exist, what UI framework? — Phase D
5. Half-chunk seam overlap UV math across cube faces — Phase A design doc (the one we write next)

Related: [[project_current_focus]], [[project_ocean_wave_approach]], [[feedback_async_no_coroutines]], [[feedback_audit_workflow]], [[reference_local_only]].
