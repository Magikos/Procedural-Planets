# PP-MCP-001: asynchronous composited screenshots

Status: Patch loaded and live capture checks passed on Unity 6000.7.0a5, 2026-09-05. See validation limits below.

## Why this exists

The installed MCP capture helper called `EditorApplication.Step()` from inside a tool command.
Unity reported:

> An abnormal situation has occurred: the PlayerLoop internal function has been called recursively. Please contact Customer Support with a sample project so that we can reproduce the problem and troubleshoot it.

The stack pointed to `ScreenshotUtility.CaptureCompositedAfterFrame`, line 196 in the original file.
Missing profiler end samples followed. This was a tool capture defect, not evidence of a wildlife simulation defect.

## Patch scope

The patch changes three package files:

- `Runtime/Helpers/ScreenshotUtility.cs`: await the existing end-of-frame capturer instead of stepping Unity.
- `Editor/Tools/ManageScene.cs`: return the pending capture through the existing asynchronous command infrastructure.
- `Editor/Tools/Cameras/ManageCamera.cs`: await the delegated scene command.

Running capture waits for a normally rendered frame. Paused capture reads the current completed frame without advancing simulation.
Neither path changes the user's pause state. Running capture fails explicitly if Play Mode changes or no frame arrives within ten seconds.
The watchdog and transient capturer are cleaned up. Late textures are destroyed rather than leaked.
The editor-only continuation destroys the transient helper immediately. Deferred destruction would retain it while Play Mode is paused.
Existing camera-specific, Edit Mode, batch, and scene-view capture paths remain available.

## Source and persistence

The project currently references `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`.
The lock file records commit `c14de1e6dc01ab42d2bb358730cff954bce0ce6b`.
The installed directory during diagnosis was `Library/PackageCache/com.coplaydev.unity-mcp@a4c2d0a84573`.
This patch targets the installed source context, including its synchronous end-of-frame helper; do not assume a stock upstream version matches it.

No maintained MCP checkout is configured on this machine. The durable project artifacts are this patch, its apply script, and this runbook.
The installed cache modification is disposable. Unity warns that immutable package edits can be lost during Package Manager operations.
The script does not modify package version selection or prevent future updates.
No upstream issue, commit, or pull request was submitted.

## Check and apply after a package update

First inspect the new capture implementation and run the reproduction checks below.
If upstream has fixed the issue, keep its implementation and retire this local patch.
Do not blindly reapply merely because the version changed.

From the repository root:

```powershell
./tools/patches/unity-mcp-playerloop/Apply-Patch.ps1 -CheckOnly
./tools/patches/unity-mcp-playerloop/Apply-Patch.ps1
```

Automatic discovery requires exactly one cached MCP package. Otherwise pass `-PackageRoot` with the active package directory.
The directory must be inside this project and identify itself as `com.coplaydev.unity-mcp`.
The script checks all patch context before writing, detects already-applied changes, and backs up all three files under `local-only/mcp-patch-backups/`.
Different upstream context stops application without changing files. Review and rebase the patch when necessary; do not force it.

Reversal also checks context and takes a backup:

```powershell
./tools/patches/unity-mcp-playerloop/Apply-Patch.ps1 -Reverse -CheckOnly
./tools/patches/unity-mcp-playerloop/Apply-Patch.ps1 -Reverse
```

Only refresh/recompile when Unity is available. Reload the package before testing; changing the source does not prove the loaded assemblies changed.
`ManageScene.HandleCommand` and `ManageCamera.HandleCommand` should both return `Task<object>` after this patch loads.

## Runtime validation

Save the original scene, Play Mode, and pause state. Preserve unrelated console diagnostics before clearing test noise.

1. In Edit Mode, request a camera screenshot and a non-capture `manage_scene` command. Require valid results.
2. In running Play Mode, capture with `manage_camera`, `include_image=true`, and no explicit camera.
3. Put a screen-space UI marker over the camera image. Require that marker in the composited result.
4. Repeat running capture three times. Require nonempty PNGs, no PlayerLoop recursion, and no missing profiler samples.
5. Pause Play Mode, record `Time.frameCount`, and capture again. Require unchanged frame count and pause state.
6. Start a running capture, then pause or leave Play Mode before completion. Require a bounded failure and no leaked capturer.
7. Exercise the timeout without a rendered Game view. Require a bounded failure, then a successful retry after restoring the view.
8. Verify an explicit-camera capture and multiview capture still work. Explicit-camera images can omit screen-space UI by design.
9. Check no `__MCP_ScreenshotCapturer__` objects remain, and restore the original editor state.

## Validation record

- Patch preflight passed for the installed source.
- First application backed up all three original files.
- Repeated application made no changes and reported the patch already applied.
- Reverse preflight passed without changing the installed files.
- An isolated package fixture passed application, repeat application, and byte-exact reversal.
  Changing an upstream method signature caused preflight rejection; hashes confirmed that no source files changed on rejection.
- Both patched MCP assemblies compiled successfully using Unity's compiler and its existing Bee response files.
  Output and reference assemblies were redirected to `docs/agent-conversation/mcp-playerloop-2026-09-05/`.
  This did not replace the editor's loaded assemblies. `runtime-build.txt` and `editor-build.txt` contain no compiler diagnostics.
- Unity entered Play Mode during the refresh without this task requesting it. Reflection still reported the old synchronous handlers.
  Bryan confirmed that another agent was testing. Live validation was paused. The loaded patch is not yet verified.

### Live validation, 2026-09-05

Bryan released Unity for this run. Reflection confirmed the asynchronous scene handler was loaded.

- Edit Mode capture and the non-capture scene query passed.
- Three running captures passed. A separate capture visibly included a magenta screen-space overlay above the planet.
- Paused capture retained the overlay and preserved frame count 2403 and the paused state.
- Explicit-camera capture and six-view capture returned valid results.
- Pausing immediately after starting capture returned `Play Mode changed during screenshot capture.`
  The first run found one helper awaiting deferred destruction. The patch now uses immediate destruction in its editor-only continuation.
  After recompilation, the same interruption returned the expected error with zero helpers while still paused.
- Disabling the transient capture object prevented its frame callback and exercised the watchdog.
  The final patch returned `No rendered frame arrived within ten seconds. Focus the Game view and retry.` after 10.01 seconds.
  No helpers remained. Three subsequent tool captures passed.
- Console error queries returned zero entries throughout the capture checks and after final restoration.
- The final scene remained `Assets/Scenes/Planet.unity`, clean, with nine roots. Play Mode and pause were off.
  No capture helpers or test overlays remained. No scene save was needed.
- Patch repeat detection and reverse preflight passed after the cleanup correction. Graphify update completed.

Evidence files are under `docs/agent-conversation/mcp-playerloop-2026-09-05/`.
The watchdog test used a disabled helper, rather than hiding the Game view. Exit-during-capture was not separately exercised.
Overlay, paused, explicit-camera, and multiview checks preceded the final cleanup-only correction; running, interruption, and timeout checks followed it.
