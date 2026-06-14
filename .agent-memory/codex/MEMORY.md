# Agent Memory Handbook

# Task Group: ProceduralPlanets code-refactor audit and startup/perf refactor

scope: Review the `code-refactor` branch against its audit/design docs, preserve the findings-first boundary, and resume the startup/perf refactor only with explicit awareness that runtime timing validation is still incomplete.
applies_to: cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets; reuse_rule=safe when working in this repo on the `code-refactor` architecture/performance thread or a similar review of recent refactor commits, but branch state and runtime conclusions must be revalidated against the active checkout and a fresh Unity run.

## Task 1: audit the `code-refactor` branch against `docs/audit/2026-06-code-refactor`, with findings-first review boundaries

### rollout_summary_files

- rollout_summaries/2026-06-13T16-56-33-3amJ-code_refactor_audit_and_startup_perf_refactor.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=C:\Users\Bryan\.codex\sessions\2026\06\13\rollout-2026-06-13T11-56-38-019ec1e9-ec3e-7482-af5e-f1a141383931.jsonl, updated_at=2026-06-14T04:48:04+00:00, thread_id=019ec1e9-ec3e-7482-af5e-f1a141383931, review started from audit docs and branch history before edits)

### keywords

- code-refactor, docs/audit/2026-06-code-refactor, 00-summary.md, CLAUDE.md, settings DTO, init graph, chunked surface provider, RuntimeInitializeOnLoadMethod, findings only, git branch -avv

## Task 2: add phased startup/progress instrumentation and async generation work while preserving behavior

### rollout_summary_files

- rollout_summaries/2026-06-13T16-56-33-3amJ-code_refactor_audit_and_startup_perf_refactor.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=C:\Users\Bryan\.codex\sessions\2026\06\13\rollout-2026-06-13T11-56-38-019ec1e9-ec3e-7482-af5e-f1a141383931.jsonl, updated_at=2026-06-14T04:48:04+00:00, thread_id=019ec1e9-ec3e-7482-af5e-f1a141383931, assistant-initiated follow-through after the audit)

### keywords

- ProgressRangeHandle, GeneratePlanetAsync, Generation timings, InitializeAsync, BuildClimateMapAsync, BuildFaceAtlasesAsync, GrassSurfaceAtlasBuilder, ChunkedSurfaceProvider, TextureAllocationBatchSize, direct face-atlas bake

## Task 3: verify builds and Unity reloads, but leave runtime startup/timing validation explicitly incomplete

### rollout_summary_files

- rollout_summaries/2026-06-13T16-56-33-3amJ-code_refactor_audit_and_startup_perf_refactor.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=C:\Users\Bryan\.codex\sessions\2026\06\13\rollout-2026-06-13T11-56-38-019ec1e9-ec3e-7482-af5e-f1a141383931.jsonl, updated_at=2026-06-14T04:48:04+00:00, thread_id=019ec1e9-ec3e-7482-af5e-f1a141383931, build and reload checks passed, fresh play-mode timing capture still missing)

### keywords

- dotnet build, ProceduralPlanets.Core.csproj, ProceduralPlanets.Planet.csproj, Editor.log, Get-Process Unity, Generation timings, runtime validation, shader warnings, git diff --check

## User preferences

- when Bryan asks to "review the project" and points to audit docs, treat the work as an audit/review first: start from `docs/audit/2026-06-code-refactor`, summarize findings by theme, and surface open questions instead of assuming conclusions [Task 1]
- when the audit docs say "Findings only. No code modified. Do not start fixing until Bryan reviews and marks decisions on each finding.", do not roll directly from review into edits without explicit approval [Task 1]

## Reusable knowledge

- `main` was at `a5e068b` and `code-refactor` was ahead with the refactor stack already landed, including the settings DTO migration, init-graph design, and chunked-surface-provider split. The audit treated those as current architecture, not proposals. [Task 1]
- `CLAUDE.md` was validated as the project contract for this branch: ScriptableObjects are authoring surfaces, DTOs are runtime state, `RuntimeInitializeOnLoadMethod` is effectively banned except `LoadingManager`, `ILogger` is preferred over raw `Debug.Log`, and dead experiments should be deleted or gated. [Task 1]
- The branch already follows a preferred split pattern of orchestrator plus small services, direct wiring, deterministic disposal, and explicit interfaces. That is the baseline to compare future large-class refactors against. [Task 1]
- `ProgressRangeHandle` now lives in `Assets/Scripts/Core/Services/ProgressHandle.cs` and implements `IProgressHandle`, so sub-phases can report into the main loading bar without duplicating progress plumbing. [Task 2]
- `Planet.GeneratePlanetAsync` now times and logs `initialize`, `terrain`, `colors`, `climate`, `water`, and `total`, while `ColorGenerator.InitializeAsync`, `ClimateMapGpuData.BuildAsync`, `BiomeAtlasService.BuildFaceAtlasesAsync`, and `GrassSurfaceAtlasBuilder.BuildAsync` follow the worker-thread compute plus staged main-thread upload pattern. [Task 2]
- `ChunkedSurfaceProvider.GenerateAsync` now batches chunk texture allocation with `TextureAllocationBatchSize = 64` and can skip allocating/uploading temporary per-chunk biome textures when direct face atlases are available. [Task 2]
- Code-health verification for this rollout was strong but bounded: `dotnet build ProceduralPlanets.Core.csproj --no-restore`, `dotnet build ProceduralPlanets.Planet.csproj --no-restore`, Unity script reloads, and `git diff --check` all passed. Runtime startup improvement is still unverified because no fresh play-mode run captured the new `Generation timings` line. [Task 3]
- If Unity is already active, prefer validating through the existing editor session rather than launching a competing editor instance. In this rollout `Get-Process Unity` showed an active `Unity.exe` session, so the checks stayed to build output and reload logs. [Task 3]

## Failures and how to do differently

- Symptom: a review request turns into implementation drift. Cause: the audit and the fix pass were not kept as separate approval boundaries. Fix: for similar refactor reviews, finish the findings pass first and wait for explicit approval before editing. [Task 1]
- Symptom: audit docs feel too large to digest efficiently. Cause: reopening every detailed doc too early burns time and context. Fix: summarize the audit by theme first, then reopen only the specific finding docs that matter for the current question. [Task 1]
- Symptom: a large patch fails mid-edit with comment, encoding, or context mismatch. Cause: the change spans too much unstable file context at once. Fix: break the work into smaller targeted patches. [Task 2]
- Symptom: async worker cancellation or completion handling races. Cause: cancellation is propagated before the worker task has actually settled. Fix: wait for the worker task to settle before rethrowing cancellation or other errors. [Task 2]
- Symptom: a helper type compiles in one assembly but is invisible where it is needed. Cause: it was placed in the wrong assembly scope. Fix: keep shared progress helpers in core services; `ProgressRangeHandle` had to move into `Assets/Scripts/Core/Services/ProgressHandle.cs`. [Task 2]
- Symptom: a startup/perf refactor looks complete because builds and script reloads passed. Cause: the runtime wall-clock evidence was never captured from a fresh play-mode start, and `Editor.log` can contain stale noise. Fix: validate from a fresh run, capture the new `Generation timings` line, and use a fresh log slice before claiming startup improvement. [Task 3]

# Task Group: ProceduralPlanets grass scale F10 validation and density-debug routing

scope: Resume ProceduralPlanets grass follow-up after visible-mesh marker projection was validated, with the next work aimed at explaining sparse coverage rather than reopening marker placement.
applies_to: cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets; reuse_rule=safe when the current grass question in this repo is marker-placement validation versus sparse visible coverage, but revalidate against the latest F10 sidecars before assuming the same gate still holds.

## Task 1: validate visible-terrain grass scale markers, then route the next pass into density rejection instrumentation instead of more placement fixes

### rollout_summary_files

- extensions/ad_hoc/notes/20260602-proceduralplanets-grass-scale-f10.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260602-proceduralplanets-grass-scale-f10.md, updated_at=2026-06-02T00:00:00-05:00, thread_id=ad-hoc-20260602-proceduralplanets-grass-scale-f10, authoritative extension note for post-fix grass scale validation, sparse-coverage observations, and the next density-debug branch)

### keywords

- grass scale, mesh-visible-terrain, MarkerProjection, hasDrop=True, meshHits=5, fallbacks=0, emitted instances, visible grass chunks, rejection counters, force-density, tuft clusters, cross-card clusters

## Reusable knowledge

- Marker placement is no longer the active problem when sidecars still report `Markers: hasDrop=True, lastSuccess=True, status=mesh-visible-terrain, count=6` and `MarkerProjection: meshHits=5, fallbacks=0`. Those values mean the scale markers landed on the visible terrain surface after the visible-mesh raycast and offset projection fixes. [Task 1] [ad-hoc note]
- The current visual result is "grass is visible but very sparse." The close human-reference capture made blade scale roughly plausible, but coverage still read as isolated thin strokes. Treat this as a density/representation problem, not a scale-marker problem, unless the sidecars regress. [Task 1] [ad-hoc note]
- The last reviewed F10 captures were `local-only/debug-screenshots/F10-water.00-Off-20260601-230026-092`, `...230048-077`, and `...230116-837`. Counts were about 79-104 visible/tracked grass chunks, 79-104 draw calls, and about 6k emitted instances, with FPS near 59 in two views and 30.1 in the close blade view. [Task 1] [ad-hoc note]
- The next instrumentation step is explicit: add grass F10 rejection counters for candidate cells/lanes, density-zero rejects, biome/state-mask rejects, water rejects, slope fade/rejects, distance/cull rejects, random density-roll rejects, emitted instances, and overflow/cap rejects. Use those counters before touching density constants. [Task 1] [ad-hoc note]
- After counters exist, add a debug density multiplier or force-density mode. If counters show most candidates die in one gate, fix that gate first. If many instances are already emitted but the scene still reads sparse, improve blade representation with tuft or cross-card clusters rather than only raising raw instance count. [Task 1] [ad-hoc note]
- Debug marker shadows are not important for this validation pass. Save real shadow-casting renderer work for production placed assets such as trees, rocks, and other gameplay objects later. [Task 1] [ad-hoc note]

## Failures and how to do differently

- Symptom: the instinct is to keep editing marker placement after the latest projection fix. Cause: older grass work made placement suspect, but the newest sidecars already validate visible-terrain placement. Fix: do not reopen marker placement unless future sidecars lose `mesh-visible-terrain` or `MarkerProjection: meshHits=5, fallbacks=0`; move the next pass into density instrumentation instead. [Task 1] [ad-hoc note]
- Symptom: grass still looks sparse and the next move is to raise density blindly. Cause: without rejection counters, it is unclear whether sparse output comes from aggressive gating or from weak visual representation per emitted instance. Fix: add the F10 rejection counters first, then branch based on whether the dominant issue is candidate rejection or poor blade representation. [Task 1] [ad-hoc note]
- Symptom: many instances are emitted yet the scene still reads as thin isolated strokes. Cause: instance count alone does not guarantee perceived coverage. Fix: pivot from pure count tuning to tuft or cross-card cluster representation once the counters confirm emission is not the bottleneck. [Task 1] [ad-hoc note]

# Task Group: ProceduralPlanets cloud seam diagnosis and cube-face sampling fix

scope: Diagnose sharp cloud-layer seams in ProceduralPlanets by proving whether the artifact already exists in the weather field, then route fixes into cube-face sampling consistency before touching downstream cloud lighting/composite code.
applies_to: cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets; reuse_rule=safe when a future cloud/weather seam appears in this repo, but revalidate against the current F10 `Cloud Diagnostics` capture bundle before editing because the owning stage can change.

## Task 1: prove a sharp cloud seam is upstream in `CloudWeather`, then fix mismatched cube-face UV mapping across shader and CPU sampling paths

### rollout_summary_files

- extensions/ad_hoc/notes/20260531-094908-cloud-cubeface-seam.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260531-094908-cloud-cubeface-seam.md, updated_at=2026-05-31T09:49:08-05:00, thread_id=ad-hoc-20260531-094908, authoritative extension note for cloud seam diagnosis, the partial border-snap attempt, and the final cube-face UV alignment fix)

### keywords

- Cloud Diagnostics, CloudWeather, CloudDensity, CloudOpticalDepth, cube-face seam, CubeFaceToUnitSphere, CubeFaceUv, WeatherSampling.hlsl, WeatherEvolution.compute, CloudShadows.hlsl, SphericalWeatherGrid.cs

## Reusable knowledge

- The highest-value routing check is stage ownership: if the seam is already visible in `CloudWeather`, treat it as a weather texture / cube-face sampling problem and not a cloud lighting or composite problem. In this case the same wedge then propagated into `CloudDensity`, `CloudOpticalDepth`, and normal `Off`. [Task 1] [ad-hoc note]
- The first fix attempt edge-snapped border texels inside `WeatherEvolution.compute` to match initial weather generation. That change was valid but only partial because a follow-up `Cloud Diagnostics` F10 still showed the wedge in `CloudWeather`. [Task 1] [ad-hoc note]
- The root cause was inverse mismatch: weather generation used `CubeFaceToUnitSphere(face, uv)`, but shader-side `CubeFaceUv(direction)` was not its inverse, with several faces flipped or rotated during sampling. That produced large face-shaped discontinuities. [Task 1] [ad-hoc note]
- The validated fix was to align cube-face UV mapping across `Assets/Graphics/Shaders/Includes/WeatherSampling.hlsl`, `Assets/Graphics/Shaders/WeatherEvolution.compute`, `Assets/Graphics/Shaders/Includes/CloudShadows.hlsl`, and `Assets/Scripts/Planet/Clouds/SphericalWeatherGrid.cs`. [Task 1] [ad-hoc note]
- Verification for this fix had two parts: `dotnet build ProceduralPlanets.Planet.csproj --no-restore` passed, and Bryan visually circled the planet several times without finding another cloud seam. Treat the seam as visually fixed for now, but keep Unity-side revalidation in mind if related shader sampling changes land later. [Task 1] [ad-hoc note]

## Failures and how to do differently

- Symptom: a sharp diagonal or cube-face-shaped cloud line shows up and the instinct is to tune downstream density, optical depth, or lighting. Cause: the seam can already exist in the weather field. Fix: run `Cloud Diagnostics` F10 and inspect `CloudWeather` first; only move downstream if `CloudWeather` is clean. [Task 1] [ad-hoc note]
- Symptom: border-texel snapping during weather evolution helps but does not remove the wedge. Cause: evolution continuity is not the only issue when cube-face orientation is inconsistent between generation and sampling. Fix: compare `CubeFaceToUnitSphere(face, uv)` against every `CubeFaceUv(direction)` and CPU query path before doing more evolution-only tuning. [Task 1] [ad-hoc note]
- Symptom: a seam returns after this fix. Cause: either cube-face sampling drift reappeared or the artifact moved downstream. Fix: if `CloudWeather` shows the seam, revisit weather cube-face sampling, evolution, or true cross-face filtering / ghost border texels; if `CloudWeather` is clean but `CloudDensity` or `Off` shows it, pivot downstream to cloud density, raymarch, or lighting. [Task 1] [ad-hoc note]

# Task Group: ProceduralPlanets ocean surface polish and WaterData edge diagnosis

scope: Resume the active ocean-surface iteration after the wave pattern shifted toward a caustic/cellular look and the latest F10 evidence pointed the visible cutout toward water-mesh metadata continuity rather than glint itself.
applies_to: cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets; reuse_rule=safe when resuming the same repo's current surface-water/glint/foam iteration, but treat the dirty file set, exact F10 captures, and current checkout state as specific to the active worktree until revalidated.

## Task 1: hand off the active surface-water iteration with caustic-style wave changes and `WaterData` edge diagnostics

### rollout_summary_files

- extensions/ad_hoc/notes/20260526-094524-water-surface-resume-state.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260526-094524-water-surface-resume-state.md, updated_at=2026-05-26T09:45:24-05:00, thread_id=ad-hoc-20260526-094524, authoritative extension note for the active ocean-surface polish state, dirty files, and `WaterData`/glint cutout diagnosis)

### keywords

- Ocean.shader, WaterDebugModule.cs, WaterData, SurfaceFxContrib, SurfaceFxProof, MotionMask, Glint, WaveSlope, SurfaceCellPattern, Hash22, SurfaceVoronoi, dataEdge, dataContinuity, WaterMeshBuilder, caustic-style surface pattern

## User preferences

- when tuning the active ocean surface, Bryan's latest feedback was that "The waves are still too uniform" and he wants the pattern to "feel more like the caustic effect" -> prefer cellular/caustic-style wave proof over long uniform bands when iterating the visible surface pattern [Task 1] [ad-hoc note]
- when a visible cutout still appears in `WaterData` or in `SurfaceFxContrib` green, Bryan's current workflow rule is to "prove root source first, then fix it" -> stop glint tuning and move into `WaterMeshBuilder` / mesh-water-data ownership checks before more polish work [Task 1] [ad-hoc note]

## Reusable knowledge

- The active dirty files at this handoff are `Assets/Graphics/Shaders/Ocean.shader` and `Assets/Scripts/Core/Services/WaterDebugModule.cs`. Do not revert them blindly; they are part of the current surface-water iteration. [Task 1] [ad-hoc note]
- The latest code-health check passed with `dotnet build ProceduralPlanets.Planet.csproj`, but visual validation is still pending because Unity must reimport the shader changes and Bryan still needs to rerun F10 for the next verdict. [Task 1] [ad-hoc note]
- The current phase is explicitly layer-by-layer water rebuild, but the active layer has moved from volume/depth/caustics into ocean surface polish: wave pattern, glint, foam, then later wake integration. The current user-visible concerns are overly uniform waves, a desire for a more caustic-like surface feel, and a hard-edged cutout that also affects glint. [Task 1] [ad-hoc note]
- The latest F10 bundle around `20260526-085345` through `20260526-085354` changed the main theory. The hard-edged cutout appears in `WaterData`, not just `Glint`, which points upstream to mesh-provided water metadata in vertex colors (`R=depth01`, `G=shore01`, `B=body01`) rather than glint as the root source. [Task 1] [ad-hoc note]
- `Ocean.shader` now contains a caustic-style surface branch with `Hash22`, `SurfaceVoronoi`, `SurfaceCellPatternUv`, and `SurfaceCellPattern`; `ComputeSurfaceWaves` blends that cellular pattern into wave slope/ripple proof instead of relying mainly on long repeated bands. The expected next proof is that `SurfaceFxProof` should look more cellular/caustic and less like repeated S-shaped stripes. [Task 1] [ad-hoc note]
- The current diagnostic defense against the cutout is `float3 waterData = float3(depth01, shore01, body01);`, `dataEdge = saturate(length(fwidth(waterData)) * 16.0);`, `dataContinuity = lerp(1.0, 0.22, smoothstep(0.16, 0.86, dataEdge));`, with glint multiplied by `dataContinuity`. `SurfaceFxContrib` was repacked to `R=wave slope`, `G=detected water-data edge`, `B=glint` so the next F10 can prove whether the cutout aligns with water-data discontinuity. [Task 1] [ad-hoc note]
- The immediate next routing checks are fixed: inspect `SurfaceFxProof` for a more caustic/cellular animated pattern, inspect `SurfaceFxContrib` green for `dataEdge`, inspect `WaterData` for the same cutout, and only keep glint/fresnel continuity on the table if the cutout does not line up with `WaterData` or `SurfaceFxContrib` green. [Task 1] [ad-hoc note]
- If the cutout is present in `WaterData`, the most likely root source is `WaterMeshBuilder` metadata generation or interpolation, especially `AddVertex` color packing, `CreateIntersection`, `bodyFactor` classification, shore/depth edge values, and clipped-triangle interpolation across ocean/shore boundaries. [Task 1] [ad-hoc note]
- A prior patch just before this handoff restored nearly invisible foam by relaxing camera/distance visibility suppression, and it decoupled local storm/weather sampling from wave geometry because `MotionMask` patches were shaping stretched/uniform wave regions too strongly. [Task 1] [ad-hoc note]
- Related skill: skills/proceduralplanets-water-artifact-debug/SKILL.md [Task 1] [ad-hoc note]

## Failures and how to do differently

- Symptom: glint shows a hard-edged cutout and the next instinct is to keep tuning glint. Cause: the discontinuity can originate earlier in mesh-provided `WaterData`, with glint only amplifying it. Fix: check `WaterData` and `SurfaceFxContrib` green first; if they show the same shape, pivot into `WaterMeshBuilder` metadata generation instead of more glint constants. [Task 1] [ad-hoc note]
- Symptom: wave proof still looks like long uniform stripes after a surface polish pass. Cause: the older crossed-band proof is still dominating over the newer cellular branch. Fix: inspect `SurfaceCellPattern` scale/time and the balance inside `ComputeSurfaceWaves` before doing more broad visual tuning. [Task 1] [ad-hoc note]
- Symptom: foam nearly disappears after wave/surface iteration. Cause: post-generation camera/distance visibility suppression can over-attenuate it. Fix: keep the relaxed visibility path and verify `Foam` / `FoamParts` are no longer black before assuming foam generation itself broke. [Task 1] [ad-hoc note]

# Task Group: ProceduralPlanets water-layer rebuild handoff

scope: Resume the current water reset direction after the latest F10 bundle proved the shader can generate effects but the production composite still presents the final `Off` view incorrectly.
applies_to: cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets; reuse_rule=safe when the active symptom is a washed/transparent final water result despite debug modes proving raw surface/effect generation, but revalidate the exact debug evidence against the current F10 bundle before resuming implementation.

## Task 1: stop the current water-polish tuning loop and restart from visible, testable render layers beginning with bottom distortion

### rollout_summary_files

- extensions/ad_hoc/notes/20260524-224518-water-layer-reset-plan.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260524-224518-water-layer-reset-plan.md, updated_at=2026-05-24T22:45:18-05:00, thread_id=ad-hoc-20260524-224518, authoritative extension note for the active water reset plan, layer-by-layer rebuild order, and `BottomDistortionOnly` direction)

### keywords

- water layer reset, start over, BottomDistortionOnly, WaterNoPost, SurfaceOnly, SurfaceRawOpaque, SurfaceFxProof, QualityController, CloudQuality, washed transparent sheet, WaterVolume.shader, Ocean.shader

## User preferences

- when the current water pass has drifted into polish without visible final improvement, Bryan explicitly chose to "start over" from the ground up using visible, testable render layers -> do not resume the abandoned tuning loop; restart from the first independently provable layer and only add the next layer after the current one is obvious in normal view [Task 1] [ad-hoc note]
- when the final `Off` view is still a "washed transparent sheet with little/no convincing surface effect" but debug/proof modes show the ingredients exist, Bryan's chosen direction is to stop more alpha/foam/glint/wave tweaking -> route the next work into layer isolation and composite ownership instead of more production-value tuning [Task 1] [ad-hoc note]

## Reusable knowledge

- The latest confirmed state separates clouds from water. Clouds were fixed after F10 proved `QualityLevel: 0 (PC)` was being misclassified as low; `QualityController` now classifies quality by name, and recent sidecars report `CloudQuality: tier=High, low=False, stepMultiplier=1.00`. [Task 1] [ad-hoc note]
- The current water failure is specifically in the final presentation/composite, not in raw effect generation. Around the latest F10 set near `20260524-223630`, `Off` looked like a washed transparent sheet, while `WaterNoPost` / `SurfaceOnly` showed darker raw surface behavior, `SurfaceRawOpaque` showed the ocean shader can generate visible color/detail, and `SurfaceFxProof` clearly showed the generated wave/effect patterns. [Task 1] [ad-hoc note]
- The active rebuild order is explicit and should be resumed in sequence: `BottomDistortionOnly`-style shallow-water bottom distortion/refraction/caustic movement first, then base water tint/depth transparency, then top surface normals/ripples, then foam/shore wash/wakes one by one with proof modes, and glint/sun sparkle last. Each layer must stay visible after the next one is added. [Task 1] [ad-hoc note]
- The first layer likely belongs in `WaterVolume.shader` / the refraction path rather than the top `Ocean.shader` surface branch. The immediate implementation direction is to add a `BottomDistortionOnly` debug mode/capture path and make the bottom distortion unmistakable in normal `Off`/production view before restoring other layers. [Task 1] [ad-hoc note]
- Keep the same F10 sidecar discipline while rebuilding: capture quality, FPS, mode, focus, weather, wave, and surface-effect metadata so each layer can be compared against prior runs. [Task 1] [ad-hoc note]
- Related skill: skills/proceduralplanets-water-artifact-debug/SKILL.md [Task 1] [ad-hoc note]

## Failures and how to do differently

- Symptom: repeated water tuning changes debug/proof modes, but the final `Off` view still looks washed out or unchanged. Cause: the production water stack/composite is not presenting the generated effects correctly, so more polish is compounding an unproved stack. Fix: stop the tuning loop and rebuild the stack from isolated visible layers, proving each one in normal view before adding the next. [Task 1] [ad-hoc note]
- Symptom: raw surface/effect modes (`WaterNoPost`, `SurfaceOnly`, `SurfaceRawOpaque`, `SurfaceFxProof`) show promising behavior, but the production image still fails. Cause: the active bug is downstream composite/presentation, not absence of effect generation. Fix: debug composite ownership and isolate the lowest layer first instead of tuning glint, foam, alpha, or waves on the final stack. [Task 1] [ad-hoc note]
- Symptom: a proposed water layer is only visible in a debug/proof mode and disappears in normal view. Cause: the layer has not been integrated or isolated strongly enough to be a valid production step. Fix: stop the sequence there and debug that one layer only; do not add the next layer until the current one is independently obvious in the normal view. [Task 1] [ad-hoc note]

# Task Group: ProceduralPlanets water-first delivery priorities and performance guardrails

scope: Preserve Bryan's current project sequencing, optimization priorities, and code-quality guardrails while water systems are still the active feature area in ProceduralPlanets.
applies_to: cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets; reuse_rule=safe for this repo's current planning/implementation order and local engineering conventions, but treat the exact timing of the performance pass as project-state specific if the feature roadmap changes.

## Task 1: consolidate the current "finish water first, then do a primary performance pass" project priority note

### rollout_summary_files

- extensions/ad_hoc/notes/20260524-012649-performance-water-priority.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260524-012649-performance-water-priority.md, updated_at=2026-05-24T01:26:49-05:00, thread_id=ad-hoc-20260524-performance-priority, authoritative extension note for water-first sequencing, performance guardrails, and code-quality reminders)

### keywords

- performance priority, water first, grass later, F10 runs, FPS, frame time, async workers, compute shaders, ILogger, UnityLogger, namespaces

## User preferences

- when balancing roadmap priorities in ProceduralPlanets, Bryan wants "performance and optimization preserved as an explicit project priority, but not to derail the current water work" -> finish the water systems first, then plan a primary performance pass before grass or other large feature areas, while still considering perf continuously during water work [Task 1] [ad-hoc note]
- when adding logging in this repo, Bryan's note says the project "should generally use it instead of direct `UnityEngine.Debug.Log`" -> prefer the existing `ILogger` abstraction unless there is a repo-backed exception [Task 1] [ad-hoc note]
- when touching organization concerns during the water push, Bryan's note says folder organization and namespaces are worth revisiting later, but "water stability comes first" -> do not mix incidental namespace migration or broad folder cleanup into active water fixes without an explicit rule-backed reason [Task 1] [ad-hoc note]

## Reusable knowledge

- Performance should be measured in the same debug workflow used for water iteration: track FPS and frame settings in debug captures so feature additions can be compared against prior F10 runs. [Task 1] [ad-hoc note]
- Keep lightweight diagnostics available for FPS, frame time, async task counts, and eventually CPU/GPU timing where practical so regressions are visible before water, shoreline, weather, atmosphere, and future grass work accumulate unmeasured cost. [Task 1] [ad-hoc note]
- Prefer async/background workers for CPU-heavy generation and data preparation when Unity API access is not required on worker threads. Consider compute shaders for high-volume parallel work that belongs on the GPU. [Task 1] [ad-hoc note]
- Watch batching, draw calls, CPU-to-GPU data transfer, mesh/material churn, allocations, caching, and data structure choices during feature work instead of waiting for a late cleanup pass. [Task 1] [ad-hoc note]
- `UnityLogger` currently wraps Unity logging, but the durable convention note is still to route new logging through the `ILogger` abstraction. Existing local guidance says project scripts currently use no namespaces, so any namespace migration should be deliberate and rule-backed rather than incidental. [Task 1] [ad-hoc note]

## Failures and how to do differently

- Symptom: performance work keeps getting postponed until after more large systems land. Cause: optimization is treated as a vague future concern instead of an explicit milestone. Fix: preserve the current sequence: finish water first, then run a primary performance pass before grass or other large feature areas. [Task 1] [ad-hoc note]
- Symptom: new water or atmosphere features add cost that is hard to compare against earlier builds. Cause: F10/debug captures are missing FPS or frame-setting context, and lightweight perf counters are absent. Fix: include FPS/frame settings in debug captures and keep basic diagnostics for frame time and async-task activity. [Task 1] [ad-hoc note]
- Symptom: active water fixes turn into broad cleanup churn. Cause: logging/style/namespace concerns get folded into the feature branch without a narrow goal. Fix: keep using `ILogger`, defer namespace/folder reorganization until later, and keep water stability as the first priority. [Task 1] [ad-hoc note]

# Task Group: ProceduralPlanets shoreline water-artifact debugging

scope: Diagnose shoreline-like lines, near-surface silhouettes, low-horizon far-shore contours, terrain-contact transparency, and terrain source-color bleed in the ProceduralPlanets water path using the repo's F10 debug workflow and the `Ocean.shader` / `WaterVolume.shader` split.
applies_to: cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets; reuse_rule=safe for this repo's named F10 modes, shader files, and verification workflow, but treat constants, thresholds, and visual conclusions as checkout- and scene-specific until revalidated in the current Unity view.

## Task 1: confirm the remaining shoreline line is terrain source color bleeding through the water-volume composite, then matte it in `WaterVolume.shader`

### rollout_summary_files

- extensions/ad_hoc/notes/20260521-132053-source-matte.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-132053-source-matte.md, updated_at=2026-05-21T18:21:35Z, thread_id=ad-hoc-20260521-132053, authoritative extension note for the confirmed terrain-source diagnosis and source-matte pass)
- extensions/ad_hoc/notes/20260521-125937-terrain-source-pink.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-125937-terrain-source-pink.md, updated_at=2026-05-21T17:59:52Z, thread_id=ad-hoc-20260521-125937, authoritative extension note for `TerrainSourcePink`, `FoamPink`, and `VolumeSphere`)

### keywords

- TerrainSourcePink, FoamPink, VolumeSphere, sourceMatte, brightSourceBleed, sourcePathOcclusion, PlanetVertexColor.shader, hot pink, terrain source color

## Task 2: isolate the remaining contour with binary water modes, reject refraction, and pivot into source-occlusion in the volume composite

### rollout_summary_files

- extensions/ad_hoc/notes/20260521-1223-volume-source-occlusion.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-1223-volume-source-occlusion.md, updated_at=2026-05-21T17:24:08Z, thread_id=ad-hoc-20260521-1223, authoritative extension note for the source-occlusion pivot)
- extensions/ad_hoc/notes/20260521-1150-volume-refraction-isolation.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-1150-volume-refraction-isolation.md, updated_at=2026-05-21T16:51:15Z, thread_id=ad-hoc-20260521-1150, authoritative extension note for `VolumeNoRefraction`)
- extensions/ad_hoc/notes/20260521-1124-volume-edge-dilation.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-1124-volume-edge-dilation.md, updated_at=2026-05-21T16:24:48Z, thread_id=ad-hoc-20260521-1124, authoritative extension note for `WaterExpandedData` and `VolumeDilation`)
- extensions/ad_hoc/notes/20260521-0955-volume-only-confirmed.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-0955-volume-only-confirmed.md, updated_at=2026-05-21T14:55:38Z, thread_id=ad-hoc-20260521-0955, authoritative extension note for confirming the volume-only path)
- extensions/ad_hoc/notes/20260521-0941-water-binary-isolation.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-0941-water-binary-isolation.md, updated_at=2026-05-21T14:42:11Z, thread_id=ad-hoc-20260521-0941, authoritative extension note for the binary `Off`/`VolumeOnly`/`SurfaceOnly`/`WaterOff` split)

### keywords

- VolumeOnly, SurfaceOnly, WaterOff, VolumeContact, VolumeDilation, VolumeNoRefraction, VolumeOcclusion, WaterExpandedData, contactVisibilityFloor, sourceOcclusion

## Task 3: shift from shore-foam theory to near-surface silhouette and water-volume edge diagnostics

### rollout_summary_files

- extensions/ad_hoc/notes/20260521-0734-near-surface-silhouette.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-0734-near-surface-silhouette.md, updated_at=2026-05-21T12:35:05Z, thread_id=ad-hoc-20260521-0734, authoritative extension note for the grazing-silhouette diagnosis)
- extensions/ad_hoc/notes/20260521-0721-volume-edge-fresnel.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-0721-volume-edge-fresnel.md, updated_at=2026-05-21T12:21:31Z, thread_id=ad-hoc-20260521-0721, authoritative extension note for `WaterScreenEdgeFade` and `SurfaceBlend`)

### keywords

- SurfaceBlend, WaterScreenEdgeFade, horizonOcclusion, grazing alpha fade, VolumeOptical, VolumeMask, screen-space edge fade, `_WaterVolumeEnabled`

## Task 4: fade shoreline surface and volume contribution at terrain-contact pixels, with dedicated contact diagnostics

### rollout_summary_files

- extensions/ad_hoc/notes/20260521-0047-surface-contact-debug.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-0047-surface-contact-debug.md, updated_at=2026-05-21T05:47:48Z, thread_id=ad-hoc-20260521-0047, authoritative extension note for `SurfaceContact` mode 22 and the widened terrain-contact fade)
- extensions/ad_hoc/notes/20260521-0044-water-depth-contact.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-0044-water-depth-contact.md, updated_at=2026-05-21T05:36:14Z, thread_id=ad-hoc-20260521-0044, authoritative extension note for the transparent/depth-contact diagnosis)
- extensions/ad_hoc/notes/20260521-0034-shore-foam-edge.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-0034-shore-foam-edge.md, updated_at=2026-05-21T05:28:43Z, thread_id=ad-hoc-20260521-0034, authoritative extension note for the close-up shoreline foam diagnosis)

### keywords

- FoamParts, SurfaceAlpha, SurfaceContact, ShoreContactVisibility, WaterSceneGapMeters, WaterSceneContactClearance01, terrainClearance, WaterShoreFoamDepth

## Task 5: tighten the F10 capture workflow, split surface-vs-volume behavior early, and back off the over-strict interior mask

### rollout_summary_files

- extensions/ad_hoc/notes/20260521-0023-water-shelf-regression-overlap.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-0023-water-shelf-regression-overlap.md, updated_at=2026-05-21T05:19:01Z, thread_id=ad-hoc-20260521-0023, authoritative extension note for the shelf regression, softened volume gate, and shoreline overlap decision)
- extensions/ad_hoc/notes/20260521-0013-water-volume-interior-mask.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-0013-water-volume-interior-mask.md, updated_at=2026-05-21T05:09:32Z, thread_id=ad-hoc-20260521-0013, authoritative extension note for the strict interior-mask attempt)
- extensions/ad_hoc/notes/20260521-0004-water-artifact-diagnosis.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-0004-water-artifact-diagnosis.md, updated_at=2026-05-21T05:01:47Z, thread_id=ad-hoc-20260521-0004, authoritative extension note for the first split surface-vs-volume diagnosis)
- extensions/ad_hoc/notes/20260520-2352-f10-targeted-water-debug.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260520-2352-f10-targeted-water-debug.md, updated_at=2026-05-21T04:52:10Z, thread_id=ad-hoc-20260520-2352, authoritative extension note for the targeted capture set)
- extensions/ad_hoc/notes/20260520-2340-water-shoreline-retention.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260520-2340-water-shoreline-retention.md, updated_at=2026-05-21T04:50:54Z, thread_id=ad-hoc-20260520-2340, authoritative extension note for screenshot retention and initial shoreline suppression)

### keywords

- WaterArtifact, F10CaptureSet, CurrentModeOnly, FullLoop, DebugScreenshotMaxRuns, local-only/debug-screenshots, VolumeBoundary, volumeInteriorMask, volumeEdgeMask, volumeBodyMask, WaterMeshBuilder, sheet/shelf

## Task 6: test shoreline overlap, cube-face boundaries, and global water-body continuity after the source-matte path stalls

### rollout_summary_files

- extensions/ad_hoc/notes/20260521-143404-global-water-graph.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-143404-global-water-graph.md, updated_at=2026-05-21T19:34:04Z, thread_id=ad-hoc-20260521-143404, authoritative extension note for the global direction-space water graph pass)
- extensions/ad_hoc/notes/20260521-142207-square-face-boundary.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-142207-square-face-boundary.md, updated_at=2026-05-21T19:22:07Z, thread_id=ad-hoc-20260521-142207, authoritative extension note for `TerrainFaceId` and cube-face boundary diagnosis)
- extensions/ad_hoc/notes/20260521-140752-shoreline-coverage-seam.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-140752-shoreline-coverage-seam.md, updated_at=2026-05-21T19:07:52Z, thread_id=ad-hoc-20260521-140752, authoritative extension note for clipped shoreline overlap, boundary-depth encoding, and volume-mask correlation)

### keywords

- TerrainFaceId, WaterMeshBuilder, ClassifyWaterBodies, ComputeShoreDistance, global direction-space water graph, shoreline overlap, VolumeBoundary, VolumeMask, cube-face boundary, Mesh: verts=217960

## Task 7: diagnose the low-camera far-shore contour with analytic sea-path debug modes, then stop matte tuning and pivot back to coverage/geometry

### rollout_summary_files

- extensions/ad_hoc/notes/20260521-184117-stop-matte-tuning-pivot.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-184117-stop-matte-tuning-pivot.md, updated_at=2026-05-21T23:41:17Z, thread_id=ad-hoc-20260521-184117, authoritative extension note for the stop-tuning pivot)
- extensions/ad_hoc/notes/20260521-183348-horizon-contact-matte.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-183348-horizon-contact-matte.md, updated_at=2026-05-21T23:33:48Z, thread_id=ad-hoc-20260521-183348, authoritative extension note for `SeaSourceMatte` and horizon-contact matte)
- extensions/ad_hoc/notes/20260521-182338-long-sea-source-matte.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-182338-long-sea-source-matte.md, updated_at=2026-05-21T23:23:38Z, thread_id=ad-hoc-20260521-182338, authoritative extension note for long sea-source matte)
- extensions/ad_hoc/notes/20260521-181300-sea-matte-diagnostic.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-181300-sea-matte-diagnostic.md, updated_at=2026-05-21T23:13:00Z, thread_id=ad-hoc-20260521-181300, authoritative extension note for `SeaMatte`)
- extensions/ad_hoc/notes/20260521-180157-analytic-sea-occlusion-gate.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-180157-analytic-sea-occlusion-gate.md, updated_at=2026-05-21T23:01:57Z, thread_id=ad-hoc-20260521-180157, authoritative extension note for `curvedSeaOcclusion` and optical-gate changes)
- extensions/ad_hoc/notes/20260521-175403-shore-sea-path-override.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-175403-shore-sea-path-override.md, updated_at=2026-05-21T22:54:03Z, thread_id=ad-hoc-20260521-175403, authoritative extension note for `shoreSeaPathCoverage`)
- extensions/ad_hoc/notes/20260521-174545-curved-sea-ray-diagnostic.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260521-174545-curved-sea-ray-diagnostic.md, updated_at=2026-05-21T22:45:45Z, thread_id=ad-hoc-20260521-174545, authoritative extension note for the low-camera curved sea-ray diagnostic and F10 modes 35-37)

### keywords

- SeaRay, SeaVsMesh, SeaPath, SeaMatte, SeaSourceMatte, curvedSeaRay, curvedSeaCoverage, curvedSeaOcclusion, horizonContactMatte, low camera, far shoreline through water

## Task 8: enforce hard isolation before tuning when repeated F10 passes show no visible progress

### rollout_summary_files

- extensions/ad_hoc/notes/2026-05-24T17-05-33-hard-isolation-before-tuning.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/2026-05-24T17-05-33-hard-isolation-before-tuning.md, updated_at=2026-05-24T17:05:33-05:00, thread_id=ad-hoc-20260524-hard-isolation-before-tuning, authoritative extension note for the isolate-first diagnostic rule and stop-tuning threshold)

### keywords

- hard isolation, hard diagnostic lines, binary tests, extreme tests, forced colors, forced opacity, bypass blending, disabled passes, side-by-side F10 evidence, stop tuning

## User preferences

- when debugging water artifacts in play mode, Bryan clarified that he "cannot choose individual debug tests during play; pressing F10 through every water debug mode is the practical workflow but creates too many screenshots" -> default to the targeted `WaterArtifact` capture set and retention/pruning workflow instead of asking for one-off mode selection [Task 5] [ad-hoc note]
- when the artifact persists after a first theory, Bryan keeps rerunning above-water, below-water, and closer-to-the-artifact F10 captures and reporting which modes still light up -> keep the diagnosis evidence-led by the actual debug modes, not by generic render-order speculation [Task 1][Task 2][Task 3][Task 4][Task 7] [ad-hoc note]
- when Bryan reported the water-surface-to-shore artifact "still looks unchanged," the later notes treated that as credible and stopped more value tuning -> if the final `Off` image is unchanged after a pass, switch to binary isolation before proposing another constants tweak [Task 2][Task 7] [ad-hoc note]
- when debugging ProceduralPlanets water rendering, Bryan explicitly wants "hard diagnostic lines: isolate the cause first, then fix that cause" -> do binary/extreme tests and prove which subsystem owns the fault before proposing more tuning values [Task 8] [ad-hoc note]
- when shoreline gaps versus overlap are under discussion, Bryan asked whether the water mesh should bleed into terrain slightly -> treat slight under-terrain overlap as acceptable if terrain depth occludes it and it removes visible shoreline gaps [Task 5][Task 6] [ad-hoc note]
- when Bryan pointed out an "odd square-like shore geometry" and later clarified the line is only visible from a low camera near the water surface while looking along the curved planet -> route the next diagnosis from the latest observed shape/viewpoint instead of reusing the previous theory's parameter tweaks [Task 6][Task 7] [ad-hoc note]

## Reusable knowledge

- The practical F10 workflow is `FreeCameraController.F10CaptureSet = WaterArtifact`, which captures the named water-debug bundle and restores ocean debug mode `Off`. `CurrentModeOnly` preserves the one-mode workflow, `FullLoop` preserves the old full sweep, `DebugScreenshotMaxRuns = 6` keeps the targeted run count manageable, and screenshots live under `local-only/debug-screenshots`. [Task 5] [ad-hoc note]
- Early routing still matters: thin far above-water contours that show up in `VolumeBoundary` / `VolumeOptical` start as a volume coverage/contact problem, while close-up shoreline edges that track `FoamParts` / `SurfaceAlpha` need `Ocean.shader` terrain-contact work. The strict `volumeInteriorMask` attempt was too aggressive and caused the above-water `sheet/shelf` regression, so the safer fallback is `volumeWaterMask = waterMask * volumeEdgeMask * volumeBodyMask` with the softer `volumeEdgeMask` and `volumeBodyMask` gates. [Task 4][Task 5] [ad-hoc note]
- Slight shoreline overlap is acceptable in this repo. The first overlap fix pushed clipped shoreline vertices toward the dry endpoint by about `shoreRange * 0.08`; the later seam pass increased the under-terrain push to about `shoreRange * 0.22`, clamped by planet scale, and encoded small non-zero depth/shore values at boundary vertices so surviving edge pixels do not render a hard water-data line. Planet/water regeneration is required before judging that change. [Task 5][Task 6] [ad-hoc note]
- `Ocean.shader` terrain-contact handling moved from foam-only attenuation into raw contact measurement: `ShoreContactVisibility(terrainClearance01, sceneValid, shore01)`, `WaterSceneGapMeters`, and `WaterSceneContactClearance01` use the raw water-surface-to-opaque-scene gap so foam, base alpha, fresnel alpha, and focus-mode shimmer all fade when water is effectively sitting on the terrain ray. `SurfaceContact` mode 22 exposes red = low-shore contact pressure, green = terrain clearance, blue = raw water-to-scene gap, and bypasses the volume composite for that view. [Task 4] [ad-hoc note]
- The near-surface diagnosis moved away from shore foam. `WaterVolume.shader` added `WaterScreenEdgeFade` so `VolumeMask` can show raw water coverage, effective volume coverage, and screen-space edge fade; `Ocean.shader` added `SurfaceBlend` mode 23 so the surface path can isolate final alpha, base alpha, and boosted fresnel alpha while the volume pass is bypassed. `horizonOcclusion` and the stronger grazing alpha fade are the right levers when `FoamParts` is mostly clean but `SurfaceBlend` and `VolumeOptical` still show the contour. [Task 3] [ad-hoc note]
- When tuning stopped changing the visible result, the workflow added binary isolation modes: `VolumeOnly` 24 keeps only `WaterVolume.shader`, `SurfaceOnly` 25 keeps only the ocean surface, and `WaterOff` 26 disables both water paths. This created the first confirmed split: the remaining line stayed in `Off` and `VolumeOnly`, not `SurfaceOnly` or `WaterOff`, so the active fix belongs in the full-screen water volume composite/prepass path. [Task 2] [ad-hoc note]
- `VolumeContact` 27 exposes contact risk, terrain clearance, and resulting water visibility; `grazingSceneContact` extends the older low-shore contact logic to above-water near-surface grazing views. When that still left a bright sliver, `WaterExpandedData` and `dilationMask` were added, and `VolumeDilation` 28 made it possible to check whether the contour was simply a one-pixel boundary-coverage miss. [Task 2] [ad-hoc note]
- Refraction was a plausible theory but did not hold. `VolumeNoRefraction` 29 forces `debugRefractionEnabled = 0`; when `VolumeOnly` and `VolumeNoRefraction` looked effectively the same, the line was not coming from refraction and the more useful pivot was source-color occlusion, not more refraction tuning. [Task 2] [ad-hoc note]
- The decisive confirmation tools for source-color bleed are `TerrainSourcePink` 31, `FoamPink` 32, and `VolumeSphere` 33. If the contour turns hot pink in `TerrainSourcePink` but not `FoamPink`, keep the fix in `WaterVolume.shader` and the source-color path rather than returning to foam. The validated production-side levers are `sourceOcclusion`, `sourcePathOcclusion`, `sourceMatte`, and `brightSourceBleed`; `VolumeOcclusion` should return black for no-water pixels so it does not hide missing coverage by falling back to `_Source`. [Task 1][Task 2] [ad-hoc note]
- The later square-edge and seam work gave two routing checks before another topology rewrite. The shoreline-like line correlating with `Absorption`, `VolumeMask`, `VolumeBoundary`, `VolumeOptical`, `VolumeContact`, and `VolumeDilation` means the clipped/prepass edge is still exposing terrain source. `TerrainFaceId` 34 colors terrain by dominant cube-sphere face; if the square-ish boundary lines up there, the next fix belongs in cross-face water classification or shore-distance computation rather than matte tuning. [Task 6] [ad-hoc note]
- `WaterMeshBuilder` was later changed to build a global direction-space water graph across all six terrain faces so wet-body classification, shore-distance BFS, original water vertices, and clipped shoreline edge vertices can share keys across cube-face borders. A regenerated mesh count change from `219813` to `217960` is evidence that the continuity patch is active; if the low-horizon line still remains after that, cube-face continuity was not the root cause. [Task 6][Task 7] [ad-hoc note]
- Bryan's low-camera clarification changed the main theory: when the line is only visible from a low camera near the water surface while looking along the curved planet, the likely remaining issue is the water-volume path/depth model at grazing angles. `SeaRay` 35 outputs scene-behind-sea, analytic sea path, and sea grazing; `SeaVsMesh` 36 compares raster volume mask, curved sea ray, and curved sea coverage; `SeaPath` 37 compares old above-scene path, curved sea path, and final path. Use those three before changing foam or overlap again. [Task 7] [ad-hoc note]
- The curved-sea branch in `WaterVolume.shader` evolved in a specific order: `shoreSeaPathCoverage` allows shoreline/contact pixels to inherit curved sea path even when open-water gates are weak; `curvedSeaOcclusion` then feeds both `abovePath` and the optical/source gates directly for low-camera rays whose source is behind the sea sphere. When the contour still survived, `SeaMatte` 38 proved the far grazing artifact could be suppressed by a hard sea/source matte, and `SeaSourceMatte` 39 separated `longSeaSourceMatte` from `horizonContactMatte` to show which candidate region production shading was trying to cover. [Task 7] [ad-hoc note]
- The important late conclusion is a stop rule, not another tuning recipe: if `SeaSourceMatte` lights a broad magenta/green region over the contour but normal `Off` and `VolumeOnly` still keep the visible line, do not keep stacking opacity, luma, transmittance, or matte-threshold tweaks in `WaterVolume.shader`. Pivot back to water-volume coverage/edge geometry such as a screen-space horizon occluder with explicit feather, analytic sea-sphere coverage independent of the raster water edge, or mesh/prepass shoreline overlap. [Task 7] [ad-hoc note]
- The newest debugging rule is broader than any single mode: use binary/extreme tests to prove or eliminate a hypothesis before tuning. If alpha is suspected, force it to an extreme or bypass the blend/composite instead of moving small constants; if an extreme test does not change the artifact, treat that branch as likely disproven and move to another subsystem. Prefer hard debug modes, forced colors, forced opacity, disabled passes, and side-by-side F10 evidence over incremental knob changes. [Task 8] [ad-hoc note]
- Validation in this repo stays split between code health and visual confirmation: targeted `dotnet build ProceduralPlanets.Core.csproj` and `dotnet build ProceduralPlanets.Planet.csproj` pass, but parallel builds can hit the known shared intermediate DLL write collision and should be rerun serially; `dotnet build Assembly-CSharp.csproj` still fails because generated Shapes project files reference missing `Assets/Plugins/Shapes/...` sources; Unity must still reimport edited shaders and regenerate the relevant planet/water data before a visual fix is considered validated. [Task 1][Task 2][Task 3][Task 4][Task 5][Task 6][Task 7] [ad-hoc note]
- Related skill: skills/proceduralplanets-water-artifact-debug/SKILL.md [Task 1][Task 2][Task 3][Task 4][Task 5][Task 6][Task 7] [ad-hoc note]

## Failures and how to do differently

- Symptom: asking Bryan to select one debug mode during play wastes time and produces noisy back-and-forth. Cause: the practical workflow is F10 capture cycling, not manual mode selection. Fix: start from the targeted `WaterArtifact` capture set and inspect its retained PNG/TXT bundle before proposing another pass. [Task 5] [ad-hoc note]
- Symptom: underwater edge bleed improves but above-water water turns into a `sheet/shelf` where only the top surface is colored. Cause: the volume gate is too strict near shore. Fix: back off the hard `volumeInteriorMask`, use the softer `volumeEdgeMask * volumeBodyMask` approach, and re-check `VolumeMask` plus real above-water views. [Task 5] [ad-hoc note]
- Symptom: repeated tuning changes numbers but Bryan says the final `Off` image still looks unchanged. Cause: the source has not been isolated yet, or the current theory is touching the wrong branch. Fix: stop tuning and compare `Off`, `VolumeOnly`, `SurfaceOnly`, and `WaterOff` first; only continue once the active render path is confirmed. [Task 2][Task 7] [ad-hoc note]
- Symptom: multiple F10 runs show no visible progress and the work is drifting into tiny constant changes. Cause: tuning started before the responsible subsystem was proved. Fix: redesign the diagnostic around a hard isolation test such as forced colors, forced opacity, bypassed blending/composite, disabled passes, or another binary/extreme branch check; if that test still does not move the artifact, leave that branch and inspect another subsystem. [Task 8] [ad-hoc note]
- Symptom: `VolumeContact` shows risk near the contour but fading contact still leaves a bright line. Cause: the remaining problem is not just contact fade; it can be a narrow untreated source-color sliver or deeper source-color bleed. Fix: inspect `VolumeDilation`, then escalate to `VolumeNoRefraction`, `VolumeOcclusion`, `TerrainSourcePink`, and `FoamPink` instead of repeating contact tuning. [Task 1][Task 2] [ad-hoc note]
- Symptom: refraction looks suspicious at the volume boundary. Cause: it is easy to overfit to a plausible shader theory. Fix: compare `VolumeOnly` and `VolumeNoRefraction`; if they match, drop refraction as the primary cause and move on to source-color suppression. [Task 2] [ad-hoc note]
- Symptom: overlap, seam, or topology changes regenerate the mesh but the visible line remains identical. Cause: the latest change may have activated correctly without touching the real low-horizon failure mode. Fix: confirm with `TerrainFaceId`, regenerated mesh counts, and then pivot to the analytic sea-path or coverage branch instead of repeating more mesh constants. [Task 6][Task 7] [ad-hoc note]
- Symptom: the artifact survives and looks like "draw order" or a shoreline foam problem. Cause: the visible contour can actually be terrain source color already rendered behind the full-screen volume composite. Fix: confirm with `TerrainSourcePink` versus `FoamPink`, then keep the fix in `WaterVolume.shader` using `sourceOcclusion`, `sourcePathOcclusion`, `sourceMatte`, and `brightSourceBleed`. [Task 1][Task 2] [ad-hoc note]
- Symptom: the line is only visible from a low near-surface camera looking along the curved planet. Cause: the remaining issue is likely curved sea-path coverage, optical gating, or horizon contact coverage rather than foam or cube-face continuity. Fix: inspect `SeaRay`, `SeaVsMesh`, and `SeaPath` before changing shoreline foam, overlap, or more source-matte thresholds. [Task 7] [ad-hoc note]
- Symptom: `SeaSourceMatte` clearly lights the contour but `Off` and `VolumeOnly` still keep the line. Cause: production shading can classify the region, but more `WaterVolume.shader` matte tuning is not solving the visible result. Fix: stop stacking production matte tweaks and pivot to explicit coverage/geometry changes. [Task 7] [ad-hoc note]
- Symptom: code builds pass but visuals are unchanged. Cause: Unity has not reimported the edited shaders or regenerated the relevant planet/water data, or the verdict is being made from code-only verification. Fix: treat `.csproj` builds and `git diff --check` as code health only, then reimport and regenerate before judging the result. [Task 1][Task 2][Task 3][Task 4][Task 5][Task 6][Task 7] [ad-hoc note]

# Task Group: ProceduralPlanets underwater shoreline-gap lip prepass and water ownership handoff

scope: Resume the later underwater shoreline-gap workflow, including water-volume lip mesh/prepass experiments, above-water regression guards, and the current division of ownership between atmosphere, precipitation, and water features.
applies_to: cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets; reuse_rule=safe when resuming the same repo and water-debug workflow, but treat the lip pass, active modified files, and current regression state as checkout-specific until confirmed against the current worktree and F10 bundle.

## Task 1: hand off the active underwater gap workflow after the global lip prepass caused an above-water through-planet regression

### rollout_summary_files

- extensions/ad_hoc/notes/20260522-190957-proceduralplanets-water-handoff.md (cwd=C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets, rollout_path=extensions/ad_hoc/notes/20260522-190957-proceduralplanets-water-handoff.md, updated_at=2026-05-23T00:09:57Z, thread_id=ad-hoc-20260522-190957, authoritative extension note for the current water handoff, lip prepass gating, and ownership split)

### keywords

- WaterVolumeLip, WaterVolumeLipPrepass, _WaterInterfaceTexture, WaterVolumeRenderFeature, IPrecipitationDebugControl, DistanceToCenter, SeaLevelRadius, VolumeLipMesh, ZTest Always, through-planet regression

## Reusable knowledge

- The active workflow has moved beyond only the low-horizon contour. The handoff note says the evidence-led F10 debugging loop now covers underwater shoreline gaps, low-sun/atmosphere-water ownership, and water completion work across `Ocean.shader`, `WaterVolume.shader`, `WaterVolumePrepass.shader`, `WaterMeshBuilder`, `WaterVolumeRenderFeature`, `Planet`, and `FreeCameraController`. [Task 1] [ad-hoc note]
- The current uncommitted repo state is intentionally dirty in many water, atmosphere, precipitation, and debug files, including `.amazonq/rules/memory-bank/water.md`, `FreeCameraController.cs`, `Planet.cs`, `WaterMeshBuilder.cs`, `WaterVolumeRenderFeature.cs`, atmosphere and precipitation render features, and a new `IPrecipitationDebugControl` interface. Do not revert unrelated edits when resuming from this handoff. [Task 1] [ad-hoc note]
- The earlier underwater glow artifacts were precipitation/debug ownership problems, not water surface problems. The validated direction was to suppress precipitation/debug contribution underwater instead of depending on manual `P` / `Y` toggles. Light shafts are treated architecturally as an atmosphere camera effect that should stop or fade at water; water owns glints, shimmer, underwater shafts, caustics, refraction, distortion, wakes, foam, and waves. [Task 1] [ad-hoc note]
- Underwater shoreline bleed/gaps are tracked as a water-volume coverage problem, not foam, atmosphere, precipitation, or sky. Bryan's Scene view screenshot of the selected water mesh aligned with the gap shapes, which supports the water mesh/prepass boundary diagnosis. [Task 1] [ad-hoc note]
- The current lip design is explicit: `WaterMeshBuilder` generates a separate `WaterVolumeLip` mesh along wet/dry shoreline edges, `Planet` creates a `WaterVolumeLip` child under `Water` with only a `MeshFilter`, `WaterVolumeRenderFeature` draws the normal water mesh into `WaterVolumePrepass` and can draw the lip mesh into `_WaterInterfaceTexture`, and F10 sidecars print `VolumeLipMesh: active=..., verts=..., tris=...`. [Task 1] [ad-hoc note]
- The key regression sequence is already established. F10 around `20260522-175229` showed `VolumeLipMesh` active and nonzero (`33282` verts/tris), but underwater gaps still showed in `Off`, `VolumeOnly`, and `VolumeOcclusion`. A second `WaterVolumeLipPrepass` pass using `ZTest Always` was then added; later F10 sets around `20260522-181748`, `20260522-181812`, and `20260522-181843` proved that unconditional always-depth lip drawing causes a new above-water through-planet regression when the camera is above sea level. [Task 1] [ad-hoc note]
- The current fix is to keep `WaterVolumeLipPrepass` available but not draw it globally. `WaterVolumeRenderFeature` now estimates sea radius from the visible water mesh bounds and draws the relaxed lip pass only when the camera is inside that water mesh. The code-health validation after this change passed: `dotnet build ProceduralPlanets.Core.csproj`, `dotnet build ProceduralPlanets.Planet.csproj`, and `git diff --check`, with only existing CRLF warnings. [Task 1] [ad-hoc note]
- The next validation bundle is fixed: rerun the same three F10 viewpoints for the through-planet artifact view, above-shore view, and underwater-looking-at-shore view. The expected result is that above-water through-planet artifacts are gone while the underwater case still tests the original shoreline-gap issue. [Task 1] [ad-hoc note]
- Related skill: skills/proceduralplanets-water-artifact-debug/SKILL.md [Task 1] [ad-hoc note]

## Failures and how to do differently

- Symptom: the underwater lip experiment improves some shoreline coverage but creates artifacts through the entire planet or above the shore. Cause: a relaxed `WaterVolumeLipPrepass` with `ZTest Always` is being drawn globally, including when the camera is above sea level. Fix: gate the relaxed lip pass so it only runs when the camera is inside the water mesh; do not re-enable a global always-depth lip. [Task 1] [ad-hoc note]
- Symptom: F10 shows `VolumeLipMesh` active and nonzero, but underwater shoreline gaps remain in `Off`, `VolumeOnly`, and `VolumeOcclusion`. Cause: the lip path is active, but lip depth rejection, lip width, or lip data is still insufficient. Fix: keep the next change small and investigate tighter manual depth rejection, lip coverage width/data, or focused `WaterVolumeDeepDive` / `VolumeMask` diagnostics instead of broad water-completion changes. [Task 1] [ad-hoc note]
- Symptom: future work drifts back into atmosphere or precipitation theory for the underwater gap. Cause: older artifact families were different. Fix: preserve the ownership split from the handoff and keep shoreline-gap debugging in water-volume coverage unless new F10 evidence says otherwise. [Task 1] [ad-hoc note]
