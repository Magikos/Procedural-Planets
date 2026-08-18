---
name: project_shade_preference_siting
description: ShadePreference sites scatter props relative to tree cover (mushrooms under canopy, meadow flowers in the open) — includes the MEASURED correlation table and two traps that made it silently do nothing
metadata:
  type: project
---

2026-08-18. `ScatterPrototype.ShadePreference` (-1..+1) sites a prop against the SHARED biome openness
field, so mushrooms and ferns gather under cover while meadow flowers fill the gaps between stands.
Both gather paths pass it (`ScatterField` and `ScatterGatherJob`); `ScatterGatherBurst` never calls
`ScatterClumping.Keep`, so there are only two parity sites, not three.

**MEASURED correlation against tree cover — author from this table, do not guess:**

| ShadePreference | corr | meaning |
| --- | --- | --- |
| +1.0 | +0.83 | deep wood |
| 0.0 | +0.80 | default, which ALREADY leans wooded |
| -0.5 | -0.06 | indifferent |
| -1.0 | -0.84 | open ground |

**The crossover is near -0.5, NOT 0**, because every prototype obeys the openness field by default. A
prop meant to ignore cover wants about -0.5; "mildly open" wants -0.6, not -0.3. Authored today:
mushrooms +0.8, ferns +0.6, woodland flowers -0.2, lake wildflowers -0.5, grass -0.6, meadow flowers -0.9.

**Two traps, each of which cost a full pass:**

1. **Do not lerp the openness INPUT toward its mirror.** A 50/50 mix of a field and its own inverse is a
   constant, so -0.5 measured as exactly zero signal and the scale was non-monotonic. Blend the
   smoothstep RESPONSE instead.
2. **The `wooded` term is saturated at 1 across ~85% of the surface**, while each prototype's per-species
   grove field swings the full 0..1 — so left alone the grove noise carries nearly all the variance and
   the shared signal is invisible. The grove term now fades out as the preference magnitude rises.

**Measure siting by correlating against the SHARED OPENNESS FIELD, not against one neutral prototype.**
The first test did the latter, read -0.002 for every value including the extremes, and made a working
feature look completely dead. The neutral prototype's own grove noise dominated its variance.

Not yet built, and the better-looking half of what Bryan's mushroom references show: fungi on stumps and
fallen logs. Those are saved exceptions in `ScatterHarvestStore`, not seed-derived, so they need a
renderer that decorates existing stump records rather than a scatter prototype.

Related: [[project_scatter_clumping_direction]], [[project_all_generated_props]].
