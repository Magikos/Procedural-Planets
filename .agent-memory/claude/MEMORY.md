# Memory Index

- [Scatter gather perf](project_scatter_gather_perf.md) — 2026-07-29: scatter IS deterministic (verify PASS, seed-based) but camera-centric + gather is SLOW (~9-14s, per-candidate biome eval); "missing scatter" = altitude (spacebar drops to surface) or gather can't keep up while flying (4564b76 capped fly speed + gather cost); real fix = faster/incremental gather

- [Planet look-dev](project_planet_look_dev.md) — 2026-07-28: Synty-look pass (post was OFF→enabled+graded PlanetLookProfile, ambient lifted, scatter densified, grass=compute-blanket per-biome params raised); commits 29f1b6a+d75144d; grass follow-ups (far-field coverage, flowers need mesh, bush brightness, day/night ambient)

- [Test harness](project_test_harness.md) — 2026-07-27: first tests (EditMode, 49 green: ScatterId/DTO-validation/ScatterHash-golden/PlacementMath); Bryan requested TDD → overrides CLAUDE.md "no test framework" rule (UTF 1.8.0 was already in manifest); reconcile CLAUDE.md + testing-stance when confirmed
- [Scatter LOD + impostor](project_scatter_lod_impostor.md) — 2026-07-27/28: shared ScatterLodBatcher (mesh LODs + far-field impostor tier, f6ef526); impostors now DYNAMICALLY LIT/day-night-correct (91daf1f: unlit-albedo bake + runtime URP main-light+SH); empty-bake guard (e566e2d); contact-sheet validator tool (de8abc5); ScatterLodStrip workbench; gotchas: instanced billboards need GetObjectToWorldMatrix, URP manual-render RT clears black + Mathf.SmoothStep≠HLSL smoothstep
- [Scatter biome buildout](project_scatter_biome_buildout.md) — 2026-07-26: all 14 land biomes have scatter (42 prototypes); FoliageLit rules (never atlas as _TrunkMap, _ForceLeaf, Cull Off, _LeafNormalUp); per-biome slot convention (Birch=slot4)
- [Grass layering arc](project_grass_layering_arc.md) — 2026-07-16/17: far-field blanket revived (linear-coverage fix) + blade clump identity + scale/width fixes shipped; textured cards & ground-darkening reverted; Synty clump scatter PARKED (determinism/pivot/material); verify grass on REAL planet not the grid test scene
- [Cloud/weather visual arc](project_cloud_weather_arc.md) — 2026-07-14: cloud-type profiles + climate-temp driver + coverage 0.30 baked + rain-shaft/aureole/sun-bleed fix shipped; clouds parked needing polish; weather.force flattens source map (use weather.regenerate)
- [Skill library](project_skill_library.md) — 2026-07-06: 16-skill library at .agent-skills/ (README router); load skills for project work; CLAUDE.md drift it surfaced
- [Human-readable console params](project_human_readable_console_params.md) — DONE 2026-07-17: swept cloud/atmo/precip/weather cmds; converted cloud.density, cloud.debug-threshold/saturation, atmosphere.sun-disc-blend to 0-1; convention promoted into pp-change-control §4
- [No coroutines, use Awaitable](feedback_async_no_coroutines.md) — all async work must use Awaitable, never Unity coroutines
- [Testing stance](project_testing_stance.md) — no tests designed yet; don't push a test framework as near-term work
- [Audit & review workflow](feedback_audit_workflow.md) — how Bryan wants audits done and reviewed before fixing
- [Settings DTO pattern](feedback_settings_dto_pattern.md) — Settings SOs are editor-only; runtime consumers read immutable snapshot DTOs (NEVER the SO directly). Prevents god-object cross-coupling.
- [Subsystem decomposition wiring](feedback_subsystem_decomposition_wiring.md) — split god-class internals into interfaces + orchestrator-injected refs; ServiceLocator/EventBus are for cross-subsystem boundaries only, not internal pipelines.
- [local-only reference material](reference_local_only.md) — external projects/papers that source the key features
- [Current work focus](project_current_focus.md) — 2026-06-15: code-refactor arc COMPLETE; all audit backlog items closed; biome arc remains paused
- [Code refactor arc](project_code_refactor_arc.md) — 2026-06-10–15: codebase-wide audit-first refactor on branch `code-refactor`; arc complete
- [Ocean wave approach](project_ocean_wave_approach.md) — displace the existing mesh (Lague), NOT a camera patch
- [Grass + chunks research](project_grass_chunks_research.md) — Phase 8 SOT; Phase A done, Phase B design doc drafted 2026-05-31
- [Chunk biome seam](project_chunk_biome_seam.md) — known polish issue: faint chunk-boundary seams in top-K biome blend (kernel can't see across chunk borders)
- [Normal mapping flat](project_normal_mapping_flat.md) — step 8 ships but terrain still looks flat; data pipeline confirmed working, lighting compression likely the cause
- [Agent conversation](reference_agent_conversation.md) — `docs/agent-conversation/` is the shared cross-agent scratchpad for parallel work on the same phase
- [Console arc](project_console_arc.md) — debug console shipped (~60 cmds, 13 prefixes); CONSOLE-6 audit/cleanup in progress, do NOT fix before Bryan reviews findings
- [Planet Architect reference](reference_planet_architect.md) — external biome/climate/vegetation reference at D:\Planet_Architect_v0.1.5_Windows; analysis paper in docs/research/
