---
name: feedback-camera-teleport-wedges-editor
description: Never jump the play-mode camera a long distance via transform.position - it re-plans the whole scatter field and hangs the Editor until MCP times out
metadata:
  type: feedback
---

Setting `cam.transform.position` to a far-away point during play mode wedges the Unity Editor for many
minutes and can end the session.

Measured 2026-08-23. I teleported the camera roughly a quarter of the way around the planet to inspect an
artifact. `Editor.log` shows what followed: `ScatterTileCache` queued about **77,000 tiles** and ground
through them on the main thread at ~10,000 per 200 ms batch, finishing at **6,645 live tiles / 252,245
instances**. Throughout, `Get-Process` reported `Responding=False` with one core pegged at 100% and 9.8 GB
resident. Every `mcp__unity__execute_code` call returned `{"success":false}` with no message; only
`telemetry_ping` still queued. The work did complete (`0 queued 0 inflight`), but the MCP websocket had
already given up - `Keep-alive failed: The remote party closed the WebSocket connection` - and the Editor
logged a clean `Shut down.` The session was lost.

**Why:** `ScatterTileCache.Reeval` re-plans on every 40 m of travel (see
[[project_runtime_hitch_profile]]). A jump of thousands of kilometres is that cost multiplied by the whole
visible shell, taken in one frame with no streaming budget.

**How to apply:** to inspect somewhere far away, use the registered console teleport
(`CommandExecutor.ExecuteImmediate`, e.g. `scatter.goto`) which the streaming system expects, or step the
camera in hops and let tiles settle between them. Better still, do not travel at all - most questions are
answerable from a render at the existing camera, from a `Texture2D` readback, or from a C# query over mesh
and field data. That is how the lake blocks were found in [[project_water_shore_rendering]].

Two diagnostics worth keeping, both usable while the Editor is dead:

- `powershell Get-Process Unity | Select Id,CPU,Responding` separates "hung" from "busy". Sample `CPU`
  twice 20 s apart: a delta near 20 s means one core is grinding and it may still recover; a delta near
  zero means it is genuinely stuck.
- `dotnet build ProceduralPlanets.Planet.csproj -v q --nologo` compiles the C# with no Editor at all,
  3.5 s. Use it to check edits while Unity is unavailable. It cannot check shaders.
