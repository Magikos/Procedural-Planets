# Current audit ledger

**Findings only — no code changed.**

The single live answer to "what is open right now". Dated audits in `docs/audit/` remain as history
and are not rewritten. Design: `docs/design/2026-08-18-periodic-code-audit.md`.

```
schemaVersion:    1.0.0
scopeId:          scope-1
auditedHead:      abb1123ea104afb4b983bc50698367f66d294765
treeFingerprint:  060F54F43F8F79B1E74D462955278E9F
auditedAt:        2026-08-18
tier:             1 (deterministic only — no LLM pass has run yet)
inventory:        432 files, 59,909 lines
```

Regenerate the header values with `pwsh tools/audit/audit.ps1 -Action fingerprint` and
`-Action inventory`. Check staleness with `-Action staleness`.

## Status of this ledger

Tier 1 (deterministic) has run. **The Tier 3 LLM pass has not**, so this ledger contains only
machine-verifiable findings plus a list of historical findings awaiting revalidation. It is not yet a
complete answer to "what is open".

---

## Open findings

### F-2026-08-18-todo-sdftext

- **category:** Operations
- **status:** OPEN
- **evidenceState:** CURRENT
- **grade:** CONFIRMED
- **severity:** low
- **anchor:** `Assets/Graphics/Shaders/SDFText.shader` — file header comment
- **evidence:** `SDFText.shader:13` — `// For world-space text on a surface, use SDFTextWorld.shader (TODO).`
- **validation:** `pwsh tools/audit/audit.ps1 -Action lint` reports exactly one `PP-R011`. Re-run to confirm.
- **impact:** CLAUDE.md names `ponytail:` and `planned:` as the only debt markers. A stray `TODO` is
  invisible to the marker harvest, so this intent is not tracked anywhere.
- **recommendation:** Convert to `// planned: world-space text lives in SDFTextWorld.shader, <doc or reason>`,
  or delete the line if the plan is dead.
- **effort:** S · **fixRisk:** LOW · **confidence:** HIGH
- **behaviorNote:** Preserving — comment only.
- **decisionHistory:** *(none yet)*

### F-2026-08-18-dup-noise-cpu-gpu

- **category:** Simplicity / Correctness
- **status:** OPEN
- **evidenceState:** CURRENT
- **grade:** UNPROVEN
- **severity:** med
- **anchor:** `Assets/Resources/GpuPlanetTerrain.compute` and `Assets/Scripts/Planet/Noise.cs`
- **evidence:** Clone detector reports ~11 overlapping duplicate windows shared between
  `GpuPlanetTerrain.compute:98` and `Noise.cs:80`.
- **validation:** `pwsh tools/audit/audit.ps1 -Action clones`. **Not yet confirmed** — nobody has read
  both implementations to establish whether they must agree bit-for-bit or merely resemble each other.
- **impact:** If these are the same noise formula in two languages, they are a dual implementation that
  must stay in lockstep. This project has already shipped two bugs of exactly that shape
  (`HashCode.Combine` variant seeds; the 64-bit `ReadyMask`), both silent and both invisible to play-testing.
- **recommendation:** Read both. If they must agree, add a parity test in the style of
  `Assets/Tests/EditMode/ScatterGatherParityTests.cs`. Do not attempt to unify the code across the
  CPU/GPU boundary.
- **effort:** M · **fixRisk:** LOW · **confidence:** MED
- **behaviorNote:** Preserving — a test, not a change.
- **decisionHistory:** *(none yet)*

### F-2026-08-18-dup-shader-lighting

- **category:** Simplicity
- **status:** OPEN
- **evidenceState:** CURRENT
- **grade:** UNPROVEN
- **severity:** low
- **anchor:** `Assets/Graphics/Shaders/FoliageLit.shader`, `Scatter.shader`, `PropLit.shader`
- **evidence:** Two largest duplicate regions in the repo — ~31 shared windows between
  `FoliageLit.shader:90` and `Scatter.shader:44`; ~25 between `PropLit.shader:17` and `Scatter.shader:15`.
- **validation:** `pwsh tools/audit/audit.ps1 -Action clones`. Not yet read by a human or agent.
- **impact:** Unknown until read. Shader families commonly share lighting preambles deliberately, so this
  may be correct as-is. Flagged because it is the largest duplication in the scope, not because it is wrong.
- **recommendation:** Read the shared regions. If they are the same lighting block, consider a shared
  `.hlsl` include — the project already uses that pattern. Smallest dedupe only; no new abstraction.
- **effort:** M · **fixRisk:** MED (shader changes are visual) · **confidence:** LOW
- **behaviorNote:** **May change visuals.** Any dedupe requires visual verification before commit.
- **decisionHistory:** *(none yet)*

### F-2026-08-18-oversize-chunkedsurfaceprovider

- **category:** Architecture
- **status:** OPEN
- **evidenceState:** CURRENT
- **grade:** OPINION
- **severity:** low
- **anchor:** `Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs`
- **evidence:** 1,555 lines — the largest file in scope, nearly 3× the next (`Planet.cs`, 689). 25 files
  exceed the 400-line guardrail.
- **validation:** `pwsh tools/audit/audit.ps1 -Action lint` (oversize section). Size is measurable; whether
  it should be split is not.
- **impact:** CLAUDE.md treats size as a symptom, not a defect: "when you're about to add a new
  responsibility to a class, split first". This is a standing note, not an action.
- **recommendation:** Do not split to hit a number. Split at the next responsibility added. The 2026-07-22
  audit already carries this as F11 (surface-provider mixed responsibilities) — see aliases below.
- **aliases:** `F11@2026-07-22-consolidated-code-audit`
- **effort:** L · **fixRisk:** MED · **confidence:** HIGH (on the measurement, not the recommendation)
- **behaviorNote:** Preserving if done as pure extraction.
- **decisionHistory:** *(none yet)*

---

## Awaiting revalidation — historical findings not yet carried forward

The Tier 3 pass has not run, so these are **not** yet part of the live answer. Each must be revalidated
against the current tree before it is seeded, per the design's build order step 2. Do not copy their old
conclusions.

| Source | Prior IDs | Note |
|---|---|---|
| `docs/audit/2026-07-22-consolidated-code-audit.md` | F01…F14 | Largest prior audit; F06 and F11 known to have been carried into later docs |
| `docs/audit/2026-07-25-scatter-audit.md` | F1, F2 | F2 marked RESOLVED by the 2026-08-11 audit |
| `docs/audit/2026-07-26-scatter-audit.md` | — | Marked OUT OF SCOPE by the 2026-08-11 audit |
| `docs/audit/2026-08-11-startup-planet-generation-audit.md` | P1…P6, R1 | P1 and P2 substantially addressed by commit `764a0fd`; R1 (water cancellation) believed still open |

## Deliberate, tracked, not findings

Harvested by `pwsh tools/audit/audit.ps1 -Action markers` — 20 markers (13 `ponytail:`, 7 `planned:`).
These are author-declared decisions, not defects. The audit reads them as evidence and does not
re-litigate them while their anchors are unchanged.

**Dated re-review:**

| Due | Site | Decision |
|---|---|---|
| 2026-09-17 | `Assets/Scripts/Core/Services/DebugRegistry.cs:273` — `IDebugDiagnosticProvider` | Keep as a diagnostics extension point. Nothing implements it and `RegisterDiagnostic` is never called. If still unimplemented at the review date, delete it and its consumer. |
