---
name: feedback-placeholder-art-while-building
description: Bryan (2026-08-26) — placeholder art/shapes are fine while mechanics are being built; never gate a mechanic on an art import.
metadata:
  type: feedback
---

Bryan, 2026-08-26: *"I am fine with all place holder art/shapes as we build out the mechanics."*

**Why:** art import and mechanics are independent risks, and bundling them makes both slower to judge. A
capsule that flees correctly proves the behaviour; a beautifully rigged deer that stands still proves
nothing. Said in the context of creature work, but stated generally — it applies to any system under
construction.

**How to apply:** when a feature could be blocked on assets, build it against a primitive and say so. Do NOT
offer "import the art pack first" as a step. Keep the swap cheap by construction — e.g.
`CreatureResidencyService` hands out a pose and knows nothing about what draws it, so replacing capsules with
rigged meshes touches `CreatureView` alone. If a mechanic genuinely cannot be judged without real art, say
which one and why, rather than importing pre-emptively.

Consistent with [[feedback-goal-first-scoping]] (prerequisites become tasks, never reasons to defer) — but
note the direction here is the opposite of gold-plating: it licenses LESS work on presentation, not more.
Does not weaken [[feedback-quality-over-cheap]]: placeholder ART is fine, placeholder MECHANICS are not.

Related: [[project-creature-residency]], [[project-all-generated-props]].

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Placeholder art while building](feedback_placeholder_art_while_building.md) — Bryan 2026-08-26: *"I am fine with all place holder art/shapes as we build out the mechanics."* **Never gate a mechanic on an art import; build against a primitive and keep the swap to one file.** Placeholder ART yes, placeholder MECHANICS no.
