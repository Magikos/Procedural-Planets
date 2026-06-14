---
name: feedback-async-no-coroutines
description: Bryan does not want Unity coroutines; all async work must use Awaitable
metadata:
  node_type: memory
  type: feedback
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
---

No Unity coroutines anywhere. Use `async Awaitable` (Unity 6) for all asynchronous/deferred work — including delays (`Awaitable.WaitForSecondsAsync`, `Awaitable.NextFrameAsync`, `Awaitable.EndOfFrameAsync`), background work (`Awaitable.BackgroundThreadAsync` / `MainThreadAsync`), and cancellation via `CancellationToken`. If you ever find `StartCoroutine`/`IEnumerator`/`yield return`/`WaitForSeconds`, replace it with the Awaitable equivalent.

**Why:** Bryan's explicit stated preference. The codebase already follows this consistently (verified 2026-05-28: zero coroutines, zero `async void`).

**How to apply:** When adding deferred/async behavior, reach for Awaitable, never a coroutine. Prefer returning `Awaitable` over `async void` so callers can await and observe completion/exceptions. See [[feedback-audit-workflow]].
