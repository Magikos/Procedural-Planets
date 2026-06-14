---
name: feedback-subsystem-decomposition-wiring
description: "How to decompose a god-class subsystem's internals — interfaces + orchestrator-injected explicit references, NOT ServiceLocator/EventBus (those are for cross-subsystem boundaries)."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
---

When splitting the internals of one cohesive subsystem (e.g. the chunked surface provider into generator / biome-atlas / mesh-cache / visibility), use **true classes behind interfaces**, wired by the **orchestrator constructing them and injecting the interface references** in dependency order (and disposing in reverse). Use **direct calls** for the ordered internal pipeline.

Do **not** reach for `ServiceLocator` / `EventBus` to wire internal parts. Those are reserved for the **cross-subsystem boundary** (independent global services finding each other; broadcasts like `PlanetGeneratedEvent`).

**Why:** Bryan considered SL+EventBus for the internal split (swappability via interfaces, no hard refs). Agreed the interface instinct is right but SL/EventBus is the wrong layer here: internal parts share one per-planet lifecycle, one instance each, on the per-frame hot path. ServiceLocator hides the dependency graph, fights lifecycle (regen churn, multi-planet), and breaks the project's "resolve once, never Get per frame" rule. EventBus turns an ordered pipeline (bake→atlas→select→render) into implicit control flow with per-frame dispatch cost and lost call stacks. Explicit interface injection from one composition root gives the same swappability with control flow kept legible. Also: Bryan dislikes `partial` classes for hand-written decomposition (only for code generators).

**How to apply:** Boundary = ServiceLocator/EventBus (already so). Internals = orchestrator owns the shared model + the collaborators, injects interfaces in dependency direction, mediates any feedback loop itself (e.g. rebake→rebind) so leaves never depend back on consumers. Keeps the dependency graph acyclic and explicit. See [[project-code-refactor-arc]]; worked plan in docs/design/2026-06-12-chunked-surface-provider-restructure.md.
