# ProceduralPlanets Shared Memory

This is the canonical committed memory index for Claude Code and Codex.
Detailed source memories remain linked below so useful history is preserved
without loading all of it into every session.

## Precedence

1. Bryan's current explicit instructions.
2. Current code, captures, sidecars, and project documentation.
3. This shared memory.
4. Agent-specific historical memory.

Memory can be stale or checkout-specific. Revalidate dates, branches, visual
results, and implementation status before acting.

## Current Direction

- The active branch is `code-refactor`.
- The current broad arc is audit-led architecture, maintainability, and
  performance work. Audit findings remain read-only until Bryan reviews and
  approves implementation.
- Earlier water, cloud, grass, biome, and terrain memories remain relevant when
  those topics resume, but they do not override the newer refactor focus.
- Build success is a code-health check. Unity reimport, regeneration, runtime
  diagnostics, and visual inspection determine rendering correctness.

## Working Preferences

- Diagnose rendering by stage ownership. Use binary or extreme proof modes
  before tuning values.
- Let the latest F10 capture evidence choose the next debugging branch.
- Keep audits findings-first. Do not begin fixes until Bryan reviews them.
- Prefer focused edits and preserve unrelated work in a dirty worktree.
- Use `ILogger` / `LoggerProvider`, not new direct `UnityEngine.Debug.Log*`.
- Use Unity `Awaitable`; do not introduce coroutines, `async void`, or
  `Task.Run`.
- Do not introduce a test framework until Bryan defines a testing strategy.

## Architecture Memory

- Settings ScriptableObjects are editor authoring surfaces. Runtime consumers
  use immutable snapshot DTOs through the world settings service.
- Internal subsystem decomposition uses interfaces and orchestrator-owned
  dependency injection. `ServiceLocator` and `EventBus` are for cross-subsystem
  boundaries, not internal pipelines.
- Initialization runs through the loading phase system. Avoid new
  `RuntimeInitializeOnLoadMethod` and `[DefaultExecutionOrder]` usage.
- Resolve services during initialization, not per frame.
- Ocean geometry waves belong on the existing spherical water mesh, not on a
  camera-following patch.

## Agent-Specific Indexes

- [Claude memory index](claude/MEMORY.md)
- [Claude code-refactor arc](claude/project_code_refactor_arc.md)
- [Claude current-focus history](claude/project_current_focus.md)
- [Claude audit workflow](claude/feedback_audit_workflow.md)
- [Claude settings DTO pattern](claude/feedback_settings_dto_pattern.md)
- [Claude subsystem decomposition](claude/feedback_subsystem_decomposition_wiring.md)
- [Codex memory summary](codex/memory_summary.md)
- [Codex memory registry](codex/MEMORY.md)
- [Codex water artifact runbook](codex/skills/proceduralplanets-water-artifact-debug/SKILL.md)

## Codex Rendering History

Search the Codex indexes before reopening these topics:

- Water: `WaterArtifact`, `TerrainSourcePink`, `SeaRay`,
  `BottomDistortionOnly`, `WaterVolumeLip`, `WaterData`,
  `SurfaceFxContrib`.
- Clouds: `CloudWeather`, `CubeFaceUv`, `WeatherSampling`.
- Grass: `mesh-visible-terrain`, `MarkerProjection`, rejection counters,
  density instrumentation.

## Updating Memory

- Stable cross-agent knowledge belongs in this index or a linked topic file.
- Claude-specific auto-memory details may stay under `claude/`.
- Codex-specific evidence and imported historical notes stay under `codex/`.
- When two memories conflict, record the conflict and the evidence that resolves
  it instead of silently deleting history.
- Do not store credentials, tokens, private keys, or sensitive captures.
