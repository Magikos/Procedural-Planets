# Init Graph design

**Date:** 2026-06-10
**Status:** Design draft — not yet implemented
**Branch:** code-refactor
**Replaces:** Priority-number ordering in `IEarlyInitialize` / `ILateInitialize`

## Goal

Replace the priority-number ordering in `LoadingManager` with a generic dependency-resolved graph. Services declare what they depend on; the graph topologically orders them; cycles fail loudly with a useful diagnostic. The same graph powers reverse-order teardown and a `WhenReady<T>()` Awaitable for external observers.

The graph is its own component — not part of `LoadingManager`. `LoadingManager` consumes it. Other systems can too.

## Why priority numbers are out

Today every initializer picks a number. New entries renumber neighbors. `RainParticleController` carries `[DefaultExecutionOrder(-50)]` partly to side-step this; other services use `EarlyPriority => -100`, `=> 1000`, etc. — magic constants with no documented meaning. There's no machine-checkable answer to "why does A run before B?" and adding a third service between them requires reading both files.

## Locked decisions

- **Type-list dependency declarations** on `IEarlyInitialize` / `ILateInitialize`. `EarlyPriority` / `LatePriority` go away.
- **One graph per phase** (early, late). Cross-phase deps are not supported; if you need them, you're using the wrong phase.
- **Interface dep targets, not concrete types.** Services depend on `typeof(IPlanet)`, not `typeof(Planet)`.
- **Kahn's algorithm** (true topological sort), not deferred-resolution retry loop. Single up-front pass; deterministic order; one-shot cycle detection. Deferred-resolution was the live-discussion sketch but Kahn's is easier to debug and report.
- **Cycle detection emits a diagnostic listing the cycle**, then refuses to start the phase. Hard fail, not warning.
- **Reverse teardown order** is computed once from the same graph.
- **`WhenReady<T>()` is for external observers only** (UI, gameplay code, debug tools outside the init graph). Services *in* the graph declare formal deps; using `WhenReady<T>()` as an imperative-ordering escape hatch is a rule violation, not a feature.

## Interface shape

```csharp
public interface IEarlyInitialize
{
    IReadOnlyList<Type> EarlyDependencies => Array.Empty<Type>();
    Awaitable EarlyInitialize(CancellationToken cancellationToken);
}

public interface ILateInitialize
{
    IReadOnlyList<Type> LateDependencies => Array.Empty<Type>();
    Awaitable LateInitialize(CancellationToken cancellationToken);
}
```

Default return is empty (no deps) so existing services without dep declarations keep working — they just sort first in the topological order. Migration is incremental.

## Graph component

```csharp
public sealed class InitGraph
{
    public InitGraph(IReadOnlyList<IEarlyInitialize> services);
    public IReadOnlyList<IEarlyInitialize> Order { get; }   // topologically sorted
    public IReadOnlyList<IEarlyInitialize> TeardownOrder { get; }  // reversed
}
```

Two instances per boot: one for the early phase, one for the late phase. Each takes the list of services that implement the relevant interface and produces the order.

Inside: standard Kahn. Build adjacency from `Dependencies`. For each dep type, find the registered service implementing that interface (typed `IsAssignableFrom`). Track unresolved-dep count per node; emit zero-count nodes; decrement neighbors; repeat. Remaining nodes after the loop are in a cycle.

## Cycle diagnostic

When the graph detects a cycle, report:

```
InitGraph: dependency cycle detected in Early phase.
  - GrassRuntimeController depends on IPlanet
  - Planet depends on IGrassRuntimeProvider
  - GrassRuntimeProvider depends on IGrassRuntimeController
  - GrassRuntimeController ← (cycle closes here)
```

Then throw. The phase does not start. Better to halt at boot with a clear message than to silently skip half the services.

## Missing dependency

If a service declares `typeof(IFoo)` and no registered service implements `IFoo`, that is also a hard fail with a diagnostic:

```
InitGraph: service GrassRuntimeController declares dependency on IFoo, no registered service implements it.
```

## WhenReady<T>()

```csharp
public static class DependencyManager
{
    public static Awaitable<T> WhenReady<T>(CancellationToken ct = default);
}
```

Returns an `Awaitable<T>` that completes when the service implementing `T` finishes its `LateInitialize` phase (or immediately, if it already has). Implementation: a typed completion source per service, signalled by `InitGraph` after each `LateInitialize` returns. If the service throws during init, all awaiting observers receive the same exception.

Optional phase overload — `WhenReady<T>(Phase.Early, ct)` — left out of v1. Add later only if a real consumer needs it.

## Integration with LoadingManager

`LoadingManager.RunInitialization` changes from:

```csharp
var earlyInitializers = allBehaviours
    .OfType<IEarlyInitialize>()
    .OrderByDescending(i => i.EarlyPriority)
    .ToList();
foreach (var initializer in earlyInitializers)
    await initializer.EarlyInitialize(ct);
```

to:

```csharp
var earlyGraph = new InitGraph(allBehaviours.OfType<IEarlyInitialize>().ToList());
foreach (var initializer in earlyGraph.Order)
    await initializer.EarlyInitialize(ct);
```

Identical at the call site; the graph just resolves the order. Same for late.

The exception-per-service `try/catch` in the current loop stays — one service failing doesn't tear down the whole boot. But it does mean dependent services run anyway, possibly against bad state. Open question below.

## Teardown

`LifecycleGraph.TeardownOrder` is the reverse of `Order`. `GameBootstrap.OnDestroy` (or the orchestrator MB that owns the service set) iterates teardown order and calls `Dispose` on each `IDisposable`. Services that aren't `IDisposable` are skipped silently.

Cross-cutting: services that today register cleanup in `OnDestroy` should migrate to `Dispose`. Where the lifetime is owned by a MonoBehaviour, the MonoBehaviour's `OnDestroy` calls into the graph.

## Migration plan

Land in three commits:

1. **Add `InitGraph` + `DependencyManager`, no behavior change.** New types compile; nothing uses them yet. Default interface members keep `EarlyPriority` / `LatePriority` for back-compat (graph ignores them when deps are declared, falls back to priority when not).
2. **Switch `LoadingManager` to use `InitGraph`.** Existing services with priority-only ordering still work — they have no deps, sort first, then by priority as tiebreaker. New services start declaring `EarlyDependencies` / `LateDependencies`.
3. **Migrate services off priority.** One service at a time. When the last priority is gone, remove `EarlyPriority` / `LatePriority` from the interfaces and any tiebreaker logic from the graph.

## Open questions

1. **Service-failure semantics.** Today a thrown exception in `EarlyInitialize` is logged and the next service runs anyway. With formal deps, this is more dangerous — downstream services may be running against missing state. Three options:
   - Keep current behavior (log and continue). Simple, matches today.
   - Halt the entire phase on any service failure. Safer, but one bad service blocks everything.
   - Halt only the subgraph downstream of the failed service. Best behavior, hardest to implement (need reverse adjacency).
   Recommended: option 3, but defer to v2 if it's complexity we don't need yet.
2. **Service registration timing.** The graph needs the service list before sorting. `LoadingManager` currently uses `FindObjectsByType` which catches every scene `MonoBehaviour`. Pure-class services (the post-Wave-2 future) aren't `MonoBehaviour`s — they need to be registered explicitly somewhere. Probably via the orchestrator MB. Worth designing alongside the orchestrator pattern.
3. **`WhenReady<T>()` interface vs concrete generic.** `WhenReady<IPlanet>()` resolves by interface — same matching rule as deps. Should `WhenReady<Planet>()` (concrete type) also work? Recommended: no. Encourages coupling to concrete types. Force interface usage.
4. **Tiebreaker for siblings.** Two services with the same dep set — what determines their order? Today priority does. With priority gone, options: declaration order in the source list, alphabetical by type name, or "undefined — don't depend on it." Recommended: undefined, with a debug warning if two siblings reference each other's state in a way that hints at an undeclared dep.

## Out of scope

- Parallel init of independent subgraphs. Could speed up boot, but our boot is fast enough that serializing is fine. Add later if measured.
- Conditional dependencies (A depends on B *if* X, else C). Unneeded complexity for now.
- Hot-swap / rebuild of the graph at runtime. Boot-time only.
- Cross-phase deps (an Early service depending on another's Late completion). Use the phases correctly instead.
