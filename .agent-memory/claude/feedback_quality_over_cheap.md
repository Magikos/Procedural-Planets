---
name: feedback_quality_over_cheap
description: Bryan always takes the proper fix over the cheap one — stop offering the shortcut as an option
metadata:
  type: feedback
---

When there is a correct fix and a cheaper one, Bryan takes the correct one. Every time.

His words, 2026-08-19:

> "Do it properly - that's always going to be my pick. I don't like easy, lazy or cheap. Quality, the
> right way every time."

**Why:** this project is long-lived and he is building substrate for a game he intends to keep working on.
A shortcut that papers over a root cause becomes a defect he pays for repeatedly. He would rather spend
the time once.

**How to apply:**
- Do not present "cheap vs proper" as a question. Pick the proper one and do it.
- Surface a choice only when the proper path carries a real cost he would want to weigh — significant
  time, risk to working systems, or a dependency on something not yet built. Even then, recommend the
  proper path and say why.
- Attacking the root cause beats narrowing a symptom. Widening a blur to hide an edge, raising a
  threshold to dodge a gate, special-casing one call site — these are the shape of answer he rejects.
- Concrete example from the water arc: grass ran into raised lakes because the fade keys off the global
  sea radius. Cheap fix was narrowing the biome blur near water; proper fix was getting the per-body
  water level onto the GPU so grass, caustics and the underwater test all read the real surface. He
  picked the GPU path without hesitating.

**Boundary — this is not licence to gold-plate.** "Properly" means the correct solution to a problem that
actually exists, not speculative structure for one that does not. It sits alongside
[[feedback_goal_first_scoping]]: prerequisites become tasks rather than reasons to defer, and the fix
addresses the cause rather than the symptom. It does not mean inventing abstractions nobody asked for.

Note this **overrides the lazy/minimal-diff default** that operating modes may push. User instruction
wins. Related: [[project_water_architecture_build]].
