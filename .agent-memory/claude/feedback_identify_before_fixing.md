---
name: feedback-identify-before-fixing
description: Bryan's rule - identify and target the artifact itself before fixing, and escalate to that the moment an attempted fix changes nothing
metadata:
  type: feedback
---

Bryan, 2026-08-24, after a water artifact took five wrong diagnoses:

> That's how we should debug all issues, identify and target - especially after an attempted fix and nothing
> changes.

**The trigger is the second failed fix, not the fifth.** One fix that changes nothing is the signal to stop
proposing causes and start building a view that makes the artifact identifiable. Every round spent
reasoning about which term "could" produce it after that point is wasted, because a frame like this has
correlated discontinuities everywhere and any of them will look guilty.

**What "identify and target" means in practice:**

1. Colour the ARTIFACT, not a suspect. Paint the pixels the user is pointing at and nothing else.
2. Hand the tool to Bryan so the target is agreed before any more code changes. He asked for "bright red so
   _I_ know you are working the right issue" - that is about trust in the target, not about the colour.
3. Then eliminate one input at a time, by capture, until only one candidate survives.
4. Only then write a fix.

**Corollary he did not have to say:** suspect your own recent commits first. Twice in this session the
artifact was introduced by my own work two commits earlier, and both times I searched code I had not written.

See [[project_water_shore_rendering]] for the worked example - the elimination table there is the shape this
should take.
