# Code Health Checklist — Procedural Planets

Run this checklist periodically (every few features, before branch merges).

## Architecture
- [ ] Single Responsibility: each class does one thing
- [ ] No god classes accumulating unrelated logic
- [ ] Interfaces used for cross-system communication (ITerrainProvider, IBiomeProvider, etc.)
- [ ] EventBus used for decoupled notifications (not direct references between unrelated systems)
- [ ] Factory pattern used where object creation varies by type
- [ ] ScriptableObjects used for data/configuration, not logic

## Code Quality
- [ ] DRY: no duplicated logic across files
- [ ] No orphaned code (unused classes, methods, fields)
- [ ] No dead imports/usings
- [ ] Naming follows conventions (see guidelines.md)
- [ ] No magic numbers — constants or descriptive variables
- [ ] Methods are short and focused (< ~30 lines ideally)

## Performance
- [ ] Async/Awaitable used for heavy generation work
- [ ] Parallel.For for independent per-face operations
- [ ] No allocations in hot paths (Update loops, per-vertex calculations)
- [ ] Object pooling where applicable
- [ ] No serialized mesh/heavy data in scene files
- [ ] Meshes created at runtime, not saved

## Unity-Specific
- [ ] No [ExecuteAlways] unless explicitly needed
- [ ] No OnValidate triggering heavy work
- [ ] No editor-only code in runtime scripts (or wrapped in #if UNITY_EDITOR)
- [ ] [SerializeField] only on data that should persist in scenes/prefabs
- [ ] Materials/shaders configured in code match expected state
- [ ] No Shader.Find in hot paths (cache references)

## Determinism
- [ ] All procedural systems use seed propagation
- [ ] Same seed → identical output every time
- [ ] No System.Random without seed
- [ ] No reliance on execution order for determinism

## Project Hygiene
- [ ] No compiler warnings
- [ ] No missing script references in scenes
- [ ] Assembly definitions are correct and minimal
- [ ] Memory bank files (guidelines, structure, product, tech) are up to date
- [ ] Git: clean commits, no generated files committed
