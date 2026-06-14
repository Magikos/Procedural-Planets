cwd: C:\Users\Bryan\Source\Repos\Magikorp\ProceduralPlanets
thread_id: ad-hoc-20260524-performance-priority
updated_at: 2026-05-24T01:26:49-05:00

Bryan wants performance and optimization preserved as an explicit project priority, but not to derail the current water work. Finish the water systems first, then schedule a primary performance pass before grass or other large feature areas. Performance should be considered continuously while adding water features.

Optimization priorities to remember:
- Track FPS and frame settings in debug captures so feature additions can be compared against prior F10 runs.
- Add/keep lightweight performance diagnostics for FPS, frame time, async task counts, and eventually CPU/GPU timing where practical.
- Prefer async/background workers for CPU-heavy generation and data preparation when Unity API access is not required on worker threads.
- Consider compute shaders for high-volume parallel work that belongs on the GPU.
- Watch batching, draw calls, CPU-to-GPU data transfer, mesh/material churn, allocations, caching, and data structure choices.
- Avoid letting water, shoreline, weather, atmosphere, and future grass features accumulate unmeasured cost.

Related code-quality reminders from Bryan's notes:
- The project has an `ILogger` abstraction and should generally use it instead of direct `UnityEngine.Debug.Log`, even though `UnityLogger` currently wraps Unity logging.
- Folder organization and namespaces are worth revisiting later, but water stability comes first. Existing local guidance says project scripts currently use no namespaces, so any namespace migration should be deliberate and rule-backed rather than incidental.
