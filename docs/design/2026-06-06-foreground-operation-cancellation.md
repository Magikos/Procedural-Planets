# Foreground operation visibility and cancellation

**Status:** Deferred future infrastructure issue.

## Problem

The debug console can open while startup scene initialization and planet
generation are running, but it does not know that a foreground operation is in
progress. `console.cancel` only tracks asynchronous commands launched by the
console, so it cannot inspect or cancel work owned by `LoadingManager`.

The console should remain available during loading. Disabling it would hide
logs and recovery commands precisely when initialization stalls or fails.

## Decision

Keep the console accessible during loading. Later, add an application-wide
foreground-operation registry that is independent of console-command pending
state.

Loading and planet generation will be the first users, but the same contract
should support scene transitions, climate application, planet regeneration,
debug captures, and future world-generation jobs.

## Expected behavior

- Opening the console shows the active operation, phase, progress, elapsed
  time, and whether cancellation is supported.
- Add commands equivalent to:

```text
operation.status
operation.cancel
operation.retry
```

- `console.cancel` remains scoped to commands launched by the console.
- Cancellation requires confirmation.
- The loading overlay remains visible while workers unwind and partial state is
  disposed.
- A cancelled startup remains in a recoverable state with clear retry and quit
  options.
- Logs and non-destructive diagnostic commands remain available while an
  operation is active.

## Required corrections before exposing cancellation

1. `LoadingManager.InitializeAsync` must rethrow
   `OperationCanceledException`; it currently catches cancellation as a generic
   initializer failure and continues.
2. `ProgressTracker` must unsubscribe in a `finally` path on cancellation or
   failure, not only on successful completion.
3. Each initialization or transition needs a per-operation linked
   `CancellationTokenSource`. The manager lifetime token must remain reserved
   for teardown.
4. `Planet.GeneratePlanetAsync` needs an explicit cancellation cleanup path for
   partial terrain, chunk buffers, biome textures, water, and grass resources.
5. Retry must restart from a defined clean boundary and must not duplicate
   bootstrap services or event subscriptions.
6. Scene-transition cancellation must define ownership after scene loading or
   activation has begun; it cannot leave both scenes partially active.

## Non-goals for the first implementation

- Arbitrary cancellation of every background task.
- Multiple simultaneous foreground operations.
- Resuming a partially generated planet.
- Treating cancellation as application teardown.

The first version should support one well-defined foreground operation with
status, cooperative cancellation, cleanup, and clean retry.
