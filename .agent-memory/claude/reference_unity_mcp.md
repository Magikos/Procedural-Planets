---
name: reference_unity_mcp
description: Unity MCP is connected and usable — how to drive the editor, force compiles (auto-refresh is OFF), run console commands, and capture screenshots. Read before assuming you can't see the game.
metadata:
  type: reference
---

**HotReload can WEDGE compilation — fixable in code, no Unity restart (2026-08-17).** Symptom:
`EditorApplication.isCompiling` stays `true` forever, the assembly on disk stops being rewritten, and the
console fills with `[HotReload] File is not part of any project` plus `[HotReload] Scripts have compile
errors: ... CS0103: The name 'X' does not exist` for **newly added .cs files**. Those errors are
HotReload's own incremental compile, NOT Unity's — Unity never started. Trigger: adding new script files.
Did NOT work: `AssetDatabase.Refresh(ForceUpdate)`, `CompilationPipeline.RequestScriptCompilation()`,
`CodeEditor.CurrentCodeEditor.SyncAll()`, `EditorUtility.RequestScriptReload()`.
**What DID work — stop the patcher, then request a compile:**
```csharp
var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "SingularityGroup.HotReload.Editor");
var patcher = asm.GetTypes().First(t => t.Name == "EditorCodePatcher");
patcher.GetMethod("StopCodePatcher", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { true });
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
```
Compiled ~90 s later. Supersedes the older "needs a Unity restart" note. Check
`EditorSceneManager.GetSceneAt(i).isDirty` before anything drastic — Bryan's unsaved scene edits are real.

**Unity MCP IS CONNECTED and works** (instance `ProceduralPlanets@3ece516259d377a5`, Unity 6000.6.0a7).
Verified end-to-end 2026-08-15: edited code, compiled, entered play, generated a planet, drove console commands,
and captured screenshots of the running game — all without Bryan touching the editor. **Don't ask Bryan to run
things you can run yourself.** Caveat: the MCP registers at session start only; if it's connected mid-session it
won't appear until a fresh Claude Code session.

## The compile loop (this is where the time gets lost)

**`kAutoRefreshMode` is 0 — auto-refresh is DISABLED in this editor.** Saving a `.cs` does NOT recompile, and
`refresh_unity` can report "compiling" while the assembly on disk stays old. Worse, **play mode blocks the domain
reload**, so a compile can sit queued indefinitely.

Reliable sequence:
1. `manage_editor stop` (a reload cannot apply during play)
2. `AssetDatabase.ImportAsset("Assets/.../File.cs", ImportAssetOptions.ForceUpdate)` for each edited file
3. `CompilationPipeline.RequestScriptCompilation()`
4. wait until `EditorApplication.isCompiling` is false
5. **verify**: `File.GetLastWriteTime(typeof(SomeType).Assembly.Location)` vs the `.cs` write time

**Always verify the timestamp before trusting a check.** Three verification passes in a row reported a fix as
broken because they ran against an assembly built 5 minutes before the edit.

**HotReload is installed and patches METHOD BODIES only.** So a logic change can take effect with a stale
assembly on disk, while these do NOT: field initializers (`new TreeDef().RootFlare` read 0.00 instead of 0.32),
newly added APIs (`TreeDefLibrary.DeadSpecies` "does not exist"), constructor/class-parameter changes. Also
**static caches survive a patch** — a cached `Material` keeps its old property values until a real domain reload.

`dotnet build ProceduralPlanets.*.csproj` is a fast syntax check but says NOTHING about what Unity has loaded.

## Running the game

- **Console commands from `execute_code`: `CommandExecutor.ExecuteImmediate("<cmd>")`** → `.Success/.Output/.Error`.
  Async commands refuse ("async commands are not valid in immediate execution") — e.g. `planet.generate`.
- Enumerate commands by reflecting `ConsoleRegistry._commands` (private static dict, ~230 entries).
- Useful: `planet.status` (runtime line has `generating=True/False`), `scatter.goto <Biome> <height>`,
  `scatter.count` (per-prototype instance counts near camera — the way to prove placement), `scatter.tiles`,
  `scatter.density`, `camera.teleports` / `camera.teleport <name>`, `debug.profiling`, `time.freeze`.
- **Planet generation takes ~2 minutes from entering play.** Poll `planet.status`; scatter commands answer
  "not configured (generate a planet first)" until it finishes.
- **Teleports often land on the NIGHT side** — screenshots come out black. Run `light.local-noon` + `time.freeze`.

## Screenshots

There is no screenshot tool. Render a camera and read the file:
`RenderTexture` → `cam.Render()` → `ReadPixels` → `EncodeToPNG` → `File.WriteAllBytes` into the scratchpad →
`Read` the PNG. 1600x900 is a good size.

**Scatter materials dither out between `_FadeStart` 120 and `_FadeEnd` 150 m** — a wide shot from further away
renders *nothing but shadows and labels*. Copy the material (never mutate the shared asset) and push the fade to
~5000 before any distant shot.

## execute_code gotchas

- `Object` is ambiguous — write `UnityEngine.Object`.
- `GetInstanceID()` is obsolete and fails compilation; compare references instead.
- `GetComponent<MeshFilter>().sharedMesh` throws on objects lacking one — iterate `GetComponentsInChildren<MeshFilter>()`.
- Roslyn compiles the snippet against the CURRENTLY LOADED assemblies, so a snippet referencing a brand-new API
  fails until the domain actually reloaded. That failure is a useful staleness signal.

## Don't save scenes casually

The editor may hold **unsaved user edits**. On 2026-08-15 saving `BiomeShowcase.unity` (to persist one field)
also persisted Bryan's unsaved rework of that scene — a ~40k-line diff replacing the committed contents. Check
`scene.isDirty` and what's actually in the scene before saving, and prefer changing values at runtime (play-mode
changes revert) when the change is only for a screenshot.

Related: [[project_tree_generator]] (the work this was proven on), [[reference_agent_conversation]].

## Iteration cost: where the time actually goes (measured 2026-08-20)

**Planet generation is ~40 s, not minutes.** The phase log is already in `Editor.log` — grep it for
`Generation timings` (path `~/AppData/Local/Unity/Editor/Editor.log`). Recent runs:

```
initialize=4.3s  terrain=9.2s  lake=1.0s  colors=19.5s  climate=0.3s  water=5.0s  finalize=0.2s  total=39.3s
```

`colors` (the biome bake) is half the total and is the only phase worth attacking if generation ever needs
to be faster. Historical entries in the same log show `colors` used to be ~39 s for a ~57 s total, so the
earlier optimisation work stuck — see [[project_startup_generation_perf]]. **There is no regression.**

**The real per-iteration cost is Unity compile + domain reload, 2-5 minutes**, on every code change.
Generation is a rounding error beside it. Consequences:

- **Batch code edits.** One compile for several changes beats one compile each.
- **Poll at 20-30 s, never 300 s.** I once reported generation as "~15 minutes" purely because I slept in
  300 s blocks around a 40 s job and counted the overshoot as runtime. The claim was false and it nearly
  sent me optimising a phase that is not slow. Short polls caught completion immediately.
- **Read `Editor.log` before measuring anything.** The instrumentation usually already exists; the console
  hides `Debug` level, which is why the timings looked absent.

`planet.rebuild-water` re-solves bodies and rebuilds the water mesh against existing terrain in **13 s** —
use it for any water change that does not need the biome or scatter bake.

## The editor can be hammered into a GPU device hang (2026-08-21)

After a long unattended session — dozens of play/stop cycles, domain reloads and full generations over
many hours — the editor died with:

```
D3D12Fence::Wait error: Device removal.
d3d12: Device failed error (887a0006)     <- DXGI_ERROR_DEVICE_HUNG
d3d12: GfxDevice was NOT out of Local memory (1.5 GB of 24.7 GB)
```

**Not an out-of-memory.** A device hang during a `Texture2DArray` upload at boot
(`[LoadingManager] Late-initializing 2 components`), which is the biome atlas — 512x512 RGBA32, 36 slices
— not anything task-specific. `AppData/Local/Temp/Unity/Editor/Crashes` shows this recurs on this project
roughly every week or two, so it predates any one session.

**How to behave:** check `Get-Process -Name Unity` before concluding the MCP link is merely slow — a long
run of `no_unity_session` replies can mean the editor is gone, not busy. Read the tail of `Editor.log` for
the cause, and check the crash-folder timestamps to see whether the crash is yours or old. Prefer fewer,
batched compile cycles; each domain reload is a fresh round of large uploads. And after a device hang,
surface it rather than silently relaunching and hammering the same GPU.
