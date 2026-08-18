---
name: feedback_goal_first_scoping
description: Bryan rejects scoping that defers a goal because prerequisites are missing — prerequisites become tasks
metadata:
  type: feedback
---

When Bryan asks for a scope or architecture document, its job is to **discover what has to happen to
reach the goal** — not to trim the goals that do not fit the current design.

His words, 2026-08-17, rejecting a v2 plan that deferred five of six decisions:

> "That's like saying we don't have any lamps in the house right now so we don't need electrical outlets.
> The entire point is to build the electrical outlet so when we buy the lamp (that is already on the
> future todo list) we can just plug it in."

and:

> "This just tells me we need to build watersettings/waterdto — not that we don't build this system until
> we have those. If those are a prerequisite, then they are added to the todo list — not remove the task
> that required the prerequisites."

**Why:** the project is in very active development. The current system's shape is not a constraint on the
goal; it is the thing being changed. A missing prerequisite is evidence about *ordering*, never evidence
*against* the feature.

**How to apply:**
- Never write "defer X until Y exists." Write "Y, then X" and put both on the list.
- A capability matrix should say what each goal *requires*, not which goals are *out of scope*.
- "Co-dependent with W5 — fold, or ship a versioned contract W5 extends" is the right shape: two ways
  forward, not a reason to stop.
- Reviews (mine or Codex's) that recommend trimming scope get the same treatment. When commissioning a
  Codex review, state up front that goal-trimming is not an acceptable recommendation.
- Related but distinct: Bryan also said *"I feel like you could have fixed the cloud shadows regardless
  if I have looked at it or not."* A defect with a findable mechanism does not need visual review first;
  the review gate is for **aesthetic choices**, not for bugs. See [[feedback_audit_workflow]].

Seen on the water redesign, [[project_water_architecture_build]].
