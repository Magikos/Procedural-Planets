# Architecture Memory

- Bryan confirmed on 2026-09-07 that multiplayer is planned. Build shared agent systems with authority-owned simulation and value-based presentation.
- Rewrites may replace implementations, but removing existing functionality requires Bryan's agreement. Reuse and extend shared mechanisms before creating parallel systems.
- The agent-system implementation sequence and preservation checks live in [the agent systems plan](../docs/design/2026-09-07-agent-systems.md).

- Settings ScriptableObjects are editor authoring surfaces. Runtime consumers
  use immutable snapshot DTOs through the world settings service.
- Internal subsystem decomposition uses interfaces and orchestrator-owned
  dependency injection. `ServiceLocator` and `EventBus` are for cross-subsystem
  boundaries, not internal pipelines.
- Initialization runs through the loading phase system. Avoid new
  `RuntimeInitializeOnLoadMethod` and `[DefaultExecutionOrder]` usage.
- Resolve services during initialization, not per frame.
- Ocean geometry waves belong on the existing spherical water mesh, not on a
  camera-following patch.
- Character locomotion is spherical-first, not sphere-bound. The reusable motor
  consumes injected gravity and grounding capabilities; radial gravity plus
  analytic planet-surface grounding are the first implementations. Other worlds
  can supply different providers without changing the movement core.
