# tools/audit

Deterministic audit pass (Tier 1). No LLM, no network, read-only. One CLI owns the logic; hooks and
skills are thin wrappers that shell out to it.

Design: `docs/design/2026-08-18-periodic-code-audit.md`
Ledger: `docs/audit/current.md`

## Usage

```
pwsh tools/audit/audit.ps1 -Action inventory      # generated counts (never type these into prose)
pwsh tools/audit/audit.ps1 -Action lint           # CLAUDE.md rules that are mechanically checkable
pwsh tools/audit/audit.ps1 -Action markers        # ponytail: / planned: harvest, incl. dated re-reviews
pwsh tools/audit/audit.ps1 -Action clones         # cross-file duplicate regions, ranked
pwsh tools/audit/audit.ps1 -Action capabilities   # public/internal static helpers ("do we already have this?")
pwsh tools/audit/audit.ps1 -Action fingerprint    # tree fingerprint over the scope
pwsh tools/audit/audit.ps1 -Action staleness      # compare tree against the ledger header
pwsh tools/audit/audit.ps1 -Action all -Format json > audit.json
```

Measured on this repo (432 files, 59,909 lines): inventory ~1.2 s, lint ~1.3 s, clones ~17 s.

## Scope

`scope.json` is the single definition of "the repo". Every function reads it, so lint, clone
detection, capability indexing and fingerprinting can never inspect different file sets.

**Changing `scope.json` must bump `scopeId`.** That invalidates cached fingerprints and stored
validation evidence, rather than silently comparing different repositories.

## Rules

Rule ids are stable (`PP-R001`…). Each declares id, severity, scope, and matcher. Adding a rule bumps
the schema minor version.

Matching is **case-sensitive** (`-cmatch`). C# is case-sensitive, and case-insensitive matching made
HLSL swizzles like `.xxx` trip the `XXX` debt-marker rule — six false positives out of seven hits on
the first run. Rules needing case-insensitivity carry an inline `(?i)`.

`PP-R007` (files over 400 lines) is reported as a trend, not a per-line defect, because CLAUDE.md
treats size as a symptom rather than a rule.

## Staleness

A commit hash cannot identify what was audited — this tree habitually carries 100+ dirty files. So
staleness compares three things: `auditedHead`, `scopeId`, and `treeFingerprint` (hash over sorted
path + content for every file in scope, including untracked and deleted).

States: `current`, `dirty` (tree differs, no commit needed), `stale` (commits since), `diverged`
(HEAD is not a descendant), `scope-changed`, `no-ledger`.

The SessionStart hook in `.claude/settings.json` runs `-Action staleness` and **reports only**. It
never starts an audit.

## Limits

- Clone detection is textual, over normalised line windows. It finds copy-paste and near-copy. It does
  not find the same algorithm expressed differently — that needs the capability index plus judgement.
- The capability index lists static helpers only. Instance-based helpers are not indexed.
- Tier 1 finds *candidates*. Nothing here is a finding until it passes validation and judgement; see
  the three-phase pipeline in the design doc.
