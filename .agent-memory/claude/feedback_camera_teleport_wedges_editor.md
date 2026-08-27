---
name: feedback-camera-teleport-wedges-editor
description: A long HORIZONTAL play-mode camera jump re-plans the whole scatter field and hangs the Editor; a vertical descent over the same spot is cheap and safe
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

## The play-mode camera also DRIFTS, and it silently invalidates captures

Measured 2026-08-23. A camera set to a submerged position was found later at radius 5039.8 - **+39.8 m
ABOVE water** - without anything obviously moving it. Several shader iterations were captured from that
drifted position and read as "my change did nothing", when in truth the code path under test was gated on
being underwater and never ran.

**Always re-assert `cam.transform.position` and `.rotation` in the SAME call that renders**, and print a
witness value (altitude, seaOffset) alongside the result. A capture whose viewpoint is not proven in the same
call proves nothing.

## Shader reimport in play mode: it works, but takes ~75 s

Same session. `AssetDatabase.ImportAsset(..., ForceUpdate)` on a shader DOES take effect during play mode -
proved by returning `float4(1,0,1,1)` unconditionally from the fragment and watching the frame go magenta.
It took **~75 s** to appear; a probe read at 45 s still showed the OLD variant.

`ShaderUtil.GetShaderMessages` reporting zero errors does NOT mean the new variant is live. Waiting on that
is what made a whole run of single-cycle conclusions untrustworthy.

**The reliable loop:** capture a reference pixel first, import, wait 110 s+, then re-capture and assert the
pixel actually CHANGED before believing anything the frame shows. If it did not change, the variant is
either stale or the code path is not running - and those two are indistinguishable without the unconditional
magenta test, which is the tie-breaker.


## It is HORIZONTAL travel that costs, not altitude (measured 2026-08-26)

The rule above is about a jump a quarter of the way around the planet. A DESCENT is a different move and is
cheap: `ScatterTileCache` re-plans on surface travel, so dropping from orbit to the ground above roughly the
same spot changes almost no tile addresses.

Measured overnight 2026-08-26, twice: camera at 13,234 m from the planet centre down to the surface, with the
target only **149 m away horizontally**, taken in two hops with a wait between. Frames held **13-16 ms**
throughout, `execute_code` never missed a beat, and the whole verification session ran unattended for over an
hour without a stall.

**So the test before teleporting is the HORIZONTAL arc, not the distance.** Compute it first — that is one
`execute_code` call — and if it is a few hundred metres, go. If it is kilometres, use `scatter.goto` or do not
travel.

The same session also contradicts the throttle claim in [[reference_unity_mcp]]: **unfocused, with
`Application.runInBackground` true, a full planet generation took 62-85 s, not the better part of an hour.**
Unattended play-mode verification is viable, and it is worth the wait — one session of it found five defects
in code that had already been committed and documented as working, including a whole feature that Unity was
silently refusing to run.

## Index digest (verbatim, moved from MEMORY.md 2026-08-26)

- [Camera teleport wedges the Editor](feedback_camera_teleport_wedges_editor.md) - a long play-mode position jump re-plans ~77k scatter tiles on the main thread; Unity goes unresponsive and MCP times out. Prefer captures and C# queries over travelling.
