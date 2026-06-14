---
name: project-testing-stance
description: As of 2026-05-28 Bryan has not designed any test strategy; the system is in active development and test targets are undecided
metadata:
  node_type: memory
  type: project
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
---

No automated tests exist and none are planned yet. Bryan has not designed or considered tests because the system is still in active development and it's unclear what's worth testing for. The editor-only `Test.cs` / `TestPoissonDiscSphereDraw.cs` Gizmo fixtures are throwaway, not real tests — fine to delete in favor of a proper suite later, or ignore for now.

**Why:** Premature to lock in test targets while subsystems are still churning.

**How to apply:** Do not push a testing framework / test assembly (e.g. audit IMP-01) as near-term work. Revisit only when Bryan signals the systems have stabilized.
