---
name: project-testing-stance
description: SUPERSEDED 2026-07-27 — Bryan requested tests/TDD; first EditMode suite exists. See [[project-test-harness]]. This older stance is kept for history.
metadata:
  node_type: memory
  type: project
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
---

**SUPERSEDED 2026-07-27:** Bryan explicitly asked for tests/TDD; a real EditMode suite now exists
(49 green). See [[project-test-harness]]. The stance below reflects 2026-05-28 and is kept for history
only — do not apply "don't push a test framework" anymore.

No automated tests exist and none are planned yet. Bryan has not designed or considered tests because the system is still in active development and it's unclear what's worth testing for. The editor-only `Test.cs` / `TestPoissonDiscSphereDraw.cs` Gizmo fixtures are throwaway, not real tests — fine to delete in favor of a proper suite later, or ignore for now.

**Why:** Premature to lock in test targets while subsystems are still churning.

**How to apply:** Do not push a testing framework / test assembly (e.g. audit IMP-01) as near-term work. Revisit only when Bryan signals the systems have stabilized.
