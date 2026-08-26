---
name: feedback-adversarial-review-verification
description: "Bryan values adversarially verifying an external reviewer's (Codex's) claims against the tree in parallel before accepting them — do not take review feedback on faith."
metadata:
  type: feedback
---

When Codex (or any external reviewer) leaves feedback on plans/code, Bryan liked me
**independently verifying every checkable claim against the current tree with parallel
agents** before agreeing. On 2026-08-08 I fanned out ~10 read-only agents (one per Codex
claim-cluster), each returning a structured verdict (CONFIRMED/REFUTED/PARTIAL + evidence
file:line + impact + reviewer-overreach). Result: 9/10 CONFIRMED, 1 PARTIAL, none refuted —
Codex had caught 4 real bugs I authored, and my verification also surfaced nuances Codex
missed. Bryan: "I really liked the use of multiple verification agents."

**Why:** review claims are leads, not facts — some are right, some overreach, some mis-cite.
Verifying independently both catches my own errors and finds where the reviewer overstated,
and it produces an evidence-backed record instead of a he-said/she-said.

**How to apply:** for review/second-opinion turns, use the Workflow tool with a barrier of
per-claim verification agents (schema: verdict + file:line evidence + impact + overreach),
then synthesize MY feedback that concurs where confirmed, pushes back where the reviewer
overreached, and adds what both missed. Then fold confirmed corrections into the plan body,
not just appended notes. Two-way review (me ↔ Codex) is the working loop. See
[[project-character-terrain-plans]].

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Adversarial review verification](feedback_adversarial_review_verification.md) — Bryan liked verifying Codex's review claims against the tree with PARALLEL agents before accepting (Workflow barrier, one agent/claim, structured verdict). Don't take review on faith; fold confirmed fixes into the body
