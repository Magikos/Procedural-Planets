v1

## User Profile

Bryan uses Codex memory to preserve a recurring ProceduralPlanets workflow in `C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets`: Unity/C# implementation, shader/rendering diagnosis, startup/perf refactors, and repo-specific review work against branch docs and recent commits. The strongest pattern is evidence-led routing: prove stage ownership from the actual debug output or audit docs first, then edit only the responsible subsystem. Current high-signal areas are water debugging, water-layer rebuilds, ocean-surface polish, underwater lip/shoreline-gap work, project sequencing/performance guardrails, cloud-weather cube-face seam diagnosis, grass scale-versus-density routing, and the `code-refactor` audit/startup-perf thread. [ad-hoc note]

He repeatedly reruns targeted captures from the viewpoint that best exposes the artifact and expects the next step to follow that evidence, not generic theory. He wants hard diagnostic lines before tuning, accepts small under-terrain overlap when it removes visible seams, and keeps water as the active delivery priority while preserving a planned performance pass immediately afterward. On review/refactor work, he points to the audit/design docs first and expects questions or findings before code changes. Repo conventions that matter repeatedly: prefer `ILogger` over direct `UnityEngine.Debug.Log`, keep incidental cleanup out of active fixes unless explicitly justified, and treat build success as separate from Unity visual validation. [ad-hoc note]

## User preferences

- Keep the next step driven by the latest F10 evidence; Bryan repeatedly reruns capture bundles and expects the modes that still light up to decide the branch. [ad-hoc note]
- When Bryan asks for a review of recent refactor commits, start as an audit/review first: use the named audit docs, summarize findings by theme, and ask questions before assuming a fix plan.
- If the review docs say "Findings only" or otherwise set a read-only boundary, do not roll directly from audit into edits without explicit approval.
- When debugging ProceduralPlanets rendering, Bryan explicitly wants "hard diagnostic lines: isolate the cause first, then fix that cause" -> default to binary/extreme tests, forced colors or opacity, bypasses, or disabled passes before more tuning. [ad-hoc note]
- For water debugging, do not ask him to choose individual debug modes during play; he said pressing F10 through every mode is the practical workflow but produces too many screenshots, so default to the targeted `WaterArtifact` capture set. [ad-hoc note]
- If the final `Off` image "still looks unchanged" or remains a "washed transparent sheet" while proof modes look better, stop value tuning and pivot to isolation or the layer-first rebuild branch. [ad-hoc note]
- When surface-wave polish is active, Bryan's feedback was "The waves are still too uniform" and he wants them to feel "more like the caustic effect" -> bias wave work toward cellular/caustic proof, not repeated bands. [ad-hoc note]
- If shoreline gaps versus overlap come up, treat a small under-terrain overlap as acceptable when terrain depth hides it and it removes visible seams. [ad-hoc note]
- When balancing feature work in ProceduralPlanets, preserve Bryan's "performance and optimization" priority without derailing active water work: finish water first, then do a primary performance pass before grass or other large feature areas. [ad-hoc note]
- Prefer the repo's `ILogger` abstraction over direct `UnityEngine.Debug.Log`, and do not mix namespace/folder cleanup into active fixes unless there is an explicit rule-backed reason. [ad-hoc note]

## General Tips

- The `ad_hoc` extension is active and authoritative; consolidate every note and tag any derived summary content with `[ad-hoc note]`. [ad-hoc note]
- Search [MEMORY.md](MEMORY.md) first for `code-refactor`, `docs/audit/2026-06-code-refactor`, `ProgressRangeHandle`, `Generation timings`, `mesh-visible-terrain`, `CloudWeather`, `WaterData`, `BottomDistortionOnly`, `WaterArtifact`, `SeaRay`, `WaterVolumeLip`, or `performance priority`. [ad-hoc note]
- In ProceduralPlanets review work, `CLAUDE.md` is a real contract rather than background reading: it carried the decisive rules for DTO/runtime boundaries, logging, and banned boot paths in the `code-refactor` audit.
- In ProceduralPlanets, prove stage ownership early: if an artifact appears in an upstream debug mode such as `CloudWeather` or `WaterData`, stop tuning downstream presentation branches first. [ad-hoc note]
- If the latest sidecars already validate an upstream gate, do not reopen that branch without regression evidence; move to the next failing gate instead, such as grass density instrumentation after `mesh-visible-terrain` marker validation. [ad-hoc note]
- If repeated F10 runs show no visible progress, stop knob-twiddling and design an extreme/binary isolation step before touching more constants. [ad-hoc note]
- `.csproj` builds are code-health checks only; Unity shader reimport and planet/water regeneration still decide the real visual verdict. [ad-hoc note]
- Build and script reload success are still not startup proof for the new perf work; a fresh play-mode run and a fresh `Editor.log` slice are required to confirm the `Generation timings` line and any real wall-clock gain.
- Parallel `ProceduralPlanets.Core.csproj` and `ProceduralPlanets.Planet.csproj` builds can collide on a shared intermediate DLL write; rerun them serially before calling it a real regression. [ad-hoc note]
- For underwater lip work, do not re-enable a global `ZTest Always` lip pass; only use the relaxed lip prepass when the camera is inside the water mesh. [ad-hoc note]

## What's in Memory

### C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets

#### 2026-06-14

- ProceduralPlanets `code-refactor` audit and startup/perf refactor: code-refactor, docs/audit/2026-06-code-refactor, CLAUDE.md, ProgressRangeHandle, Generation timings, ChunkedSurfaceProvider
  - desc: Search this first in `cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets` when the task is reviewing the June refactor branch, checking whether the current architecture matches the docs, or resuming the startup/progress/perf visibility work.
  - learnings: Treat the audit docs as the entrypoint and keep the findings-only boundary intact; the follow-through refactor added async phased generation plus timing/progress instrumentation, but runtime improvement is still unverified until a fresh play-mode run captures the new `Generation timings` log.

#### 2026-06-02

- ProceduralPlanets grass scale F10 validation and density-debug routing: mesh-visible-terrain, MarkerProjection, emitted instances, visible grass chunks, rejection counters, force-density
  - desc: Search this first in `cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets` when grass markers were recently fixed and the remaining question is whether sparse coverage comes from candidate rejection or weak visual representation. [ad-hoc note]
  - learnings: Marker placement is currently validated by `mesh-visible-terrain` plus `MarkerProjection: meshHits=5, fallbacks=0`; the next step is rejection counters first, then branch between gate fixes and tuft/cross-card representation work. [ad-hoc note]

#### 2026-05-31

- ProceduralPlanets cloud seam diagnosis and cube-face sampling fix: Cloud Diagnostics, CloudWeather, CubeFaceToUnitSphere, CubeFaceUv, WeatherSampling.hlsl, SphericalWeatherGrid.cs
  - desc: Search this first for sharp diagonal or cube-face-shaped cloud seams in `cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets`, especially when you need to prove whether the artifact starts in the weather field or later in cloud rendering. [ad-hoc note]
  - learnings: The winning route was `CloudWeather` first; once the seam showed there, the fix was not lighting but aligning cube-face UV orientation across weather generation, shader sampling, cloud shadows, and the CPU weather query path. [ad-hoc note]

### Older Memory Topics

#### C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets

- ProceduralPlanets ocean surface polish and WaterData edge diagnosis: WaterData, SurfaceFxContrib, SurfaceCellPattern, SurfaceVoronoi, dataEdge, dataContinuity, WaterMeshBuilder, Glint
  - desc: Use this when resuming ocean-surface polish in `cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets`, especially if waves still feel too uniform or a hard cutout appears in glint and nearby debug modes; it routes into the caustic-style surface branch and `WaterData` ownership checks. [ad-hoc note]
- ProceduralPlanets water-layer rebuild handoff: BottomDistortionOnly, WaterNoPost, SurfaceRawOpaque, SurfaceFxProof, washed transparent sheet, QualityController, CloudQuality
  - desc: Use this when the final `Off` water view still looks washed out in `cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets` even though proof modes show convincing raw surface/effect output; it captures the layer-by-layer rebuild order and the related cloud-quality routing. [ad-hoc note]
- ProceduralPlanets water-first delivery priorities and performance guardrails: performance priority, water first, grass later, FPS, frame time, async workers, compute shaders, ILogger
  - desc: Use this when planning follow-up work in `cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets`, especially for optimization sequencing, logging conventions, or whether cleanup should stay out of active water work. [ad-hoc note]
- ProceduralPlanets shoreline water-artifact debugging: hard isolation, WaterArtifact, VolumeOnly, TerrainSourcePink, SeaRay, SeaSourceMatte, forced opacity, disabled passes
  - desc: Use this as the main water-artifact runbook in `cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets` for shoreline-like lines, low-horizon far-shore contours, or any case where repeated F10 runs are not moving the result; it also points to [skills/proceduralplanets-water-artifact-debug/SKILL.md](skills/proceduralplanets-water-artifact-debug/SKILL.md). [ad-hoc note]
- ProceduralPlanets underwater shoreline-gap lip prepass and water ownership handoff: WaterVolumeLip, WaterVolumeLipPrepass, _WaterInterfaceTexture, WaterVolumeRenderFeature, VolumeLipMesh, through-planet regression
  - desc: Use this for the underwater shoreline-gap workflow in `cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets`, especially when a lip-prepass experiment helps underwater views but risks above-water regressions; it captures the ownership split and the guard against global always-depth lip rendering. [ad-hoc note]
