<!-- Template: docs/audit/YYYY-MM-DD-scope-audit.md
     Modeled on docs/audit/2026-07-03-grass-cloud-line-audit.md and
     2026-07-03-general-code-audit.md. Delete comment blocks when instantiating.
     THE ONE RULE: findings only. No code changes, no "quick fixes while I'm here".
     Bryan marks each finding fix / defer / wontfix before anything is touched. -->

# <Scope> Audit — YYYY-MM-DD

<Scope paragraph: exact tree state — branch, base commit, dirty-working-tree notes
(e.g. "branch code-refactor, dirty on top of ec0b1cd"); which files/systems are covered;
what prior audits already covered — link them and DO NOT re-list their findings.>

**Findings only — no code changed.** Severity: `BUG` (wrong behavior), `RISK` (latent
failure), `PERF`, `DEAD` (dead code, per project rules removed when discovered), `RULE`
(project-rule violation), `ARCH` (structure), `META` (docs/tooling).
<!-- Line-level audits may add: `STYLE/BP` (best practice), `SUGG` (optional improvement).
     Keep the legend to the severities you actually use. -->

---

## What came back clean (mechanical sweeps)

<!-- Negative results are load-bearing: they stop the next auditor re-sweeping.
     List the sweep AND its result. -->

- **<Sweep name>**: <what was grepped/checked, and the clean result with numbers>.
- ...

---

## Part 1 — Bugs and correctness

<!-- Finding IDs are letter-series per part/domain (A1, A2... / B1... / G1...) and are
     cited by ID from later design docs — never renumber after publication. -->

### A1. BUG — <one-line title stating the wrong behavior>
`Relative/Path/File.cs:123-130`

```csharp
// exact code quote from the tree audited — quote, don't paraphrase
```

<Analysis: what actually happens, why it is wrong, what triggers it, blast radius.
If a sibling system already solved this correctly, quote that too — it is the fix sketch.>

**Fix:** <sketch only — the smallest correct change and its risk. NOT applied.>

### A2. RISK — <title>
...

## Part 2 — <Perf / rule compliance / structure ...>

...

---

## Priority table

| # | Finding | Effort | Risk |
|---|---------|--------|------|
| 1 | A1 <short name> | one line | low |

<One closing line: which findings are live defects vs drift control.>

<!-- After publication: the other agent (Codex/Claude) appends a review section below —
     "## Codex feedback" / "## Claude feedback" — agreeing, amending, or refuting per
     finding, dated against the tree it re-checked. Append; never rewrite findings. -->
