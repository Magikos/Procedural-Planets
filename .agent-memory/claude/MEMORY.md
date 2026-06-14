# Memory Index

- [No coroutines, use Awaitable](feedback_async_no_coroutines.md) — all async work must use Awaitable, never Unity coroutines
- [Testing stance](project_testing_stance.md) — no tests designed yet; don't push a test framework as near-term work
- [Audit & review workflow](feedback_audit_workflow.md) — how Bryan wants audits done and reviewed before fixing
- [Settings DTO pattern](feedback_settings_dto_pattern.md) — Settings SOs are editor-only; runtime consumers read immutable snapshot DTOs (NEVER the SO directly). Prevents god-object cross-coupling.
- [Subsystem decomposition wiring](feedback_subsystem_decomposition_wiring.md) — split god-class internals into interfaces + orchestrator-injected refs; ServiceLocator/EventBus are for cross-subsystem boundaries only, not internal pipelines.
- [local-only reference material](reference_local_only.md) — external projects/papers that source the key features
- [Current work focus](project_current_focus.md) — Phase A chunk skeleton shipped; Phase B biome textures design doc awaiting Bryan's review
- [Code refactor arc](project_code_refactor_arc.md) — 2026-06-10: codebase-wide audit-first refactor on branch `code-refactor`; biome arc paused
- [Ocean wave approach](project_ocean_wave_approach.md) — displace the existing mesh (Lague), NOT a camera patch
- [Grass + chunks research](project_grass_chunks_research.md) — Phase 8 SOT; Phase A done, Phase B design doc drafted 2026-05-31
- [Chunk biome seam](project_chunk_biome_seam.md) — known polish issue: faint chunk-boundary seams in top-K biome blend (kernel can't see across chunk borders)
- [Normal mapping flat](project_normal_mapping_flat.md) — step 8 ships but terrain still looks flat; data pipeline confirmed working, lighting compression likely the cause
- [Agent conversation](reference_agent_conversation.md) — `docs/agent-conversation/` is the shared cross-agent scratchpad for parallel work on the same phase
- [Console arc](project_console_arc.md) — debug console shipped (~60 cmds, 13 prefixes); CONSOLE-6 audit/cleanup in progress, do NOT fix before Bryan reviews findings
- [Planet Architect reference](reference_planet_architect.md) — external biome/climate/vegetation reference at D:\Planet_Architect_v0.1.5_Windows; analysis paper in docs/research/
