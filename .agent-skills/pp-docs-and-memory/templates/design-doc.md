<!-- Template: docs/design/YYYY-MM-DD-topic.md
     Modeled on docs/design/2026-07-04-cloud-visual-migration-plan.md (the current house
     pattern). Delete these comment blocks when instantiating. -->

# <Topic> Plan — YYYY-MM-DD

## Active Tracker

Status: <one sentence: what has landed, what is pending. Rewrite every session that changes it.>

Current next action: <the single concrete next step — include the exact console command /
capture set when one exists, e.g. run `debug.capture-set "Cloud Diagnostics"` and F10>.

- [x] Phase 0: <landed item — carry the evidence pointer inline, e.g. capture set `20260704-1325xx`>
- [x] Phase 1 capture comparison: <capture IDs>, <Bryan's verdict>
- [ ] Phase 2: <next item>
- [ ] Phase N: <...>

<!-- Tracker rules: check a box only when its evidence exists (capture taken, Bryan
     reviewed) — never on "code compiled". Update tracker + Status + Current next action
     in the same session a phase lands. Don't restructure the body below when doing so. -->

---

Goal: <one paragraph, from-state → to-state, in observable/visual terms — what a person
sees or measures, not internals>.

Hard requirement carried through every phase: **<the invariant no phase may break, e.g.
"the weather grid stays the single source of truth">.**

Source docs: [<audit name>](../audit/YYYY-MM-DD-....md) (finding IDs A2, B1, ...),
[<research digest>](../research/YYYY-MM-DD-....md) (item IDs R1, R2). This plan sequences
them; the detailed code sketches live there — cross-reference, don't re-paste.

Files touched across all phases: <exhaustive list>. Nothing else. <Explicit exclusions,
e.g. "Caustics untouched.">

Verification workflow for every phase: <capture set + F10 before/after, relevant
`<prefix>.<command>` sweeps, compile check `dotnet build ProceduralPlanets.Planet.csproj`>.

---

## Phase 0 — <name> (<what it blocks>; <time estimate>)

<One line: why this phase exists / what is wasted without it.>

1. **<Item>** (source finding/item ID): <what to change, where — file and function — and
   the recommended shape of the change>.
2. **<Item>** ...

**Exit check:** <observable acceptance evidence: which capture, which cells/scenes to
compare, what must match. Archive the capture set.>

## Phase N — <name> (<estimate>)

...

**Exit check:** ...

---

## Sequencing, risk, decision gates

- Phases are strictly ordered <0→N>; each is independently shippable and capture-verified.
- **Gate after Phase <X> (Bryan review):** <why a human gate sits here>.
- Biggest technical risk: <the thing most likely to bite, and the mitigation/pre-work>.
- <Open DECISIONS that are Bryan's, flagged as decisions, not tasks.>
