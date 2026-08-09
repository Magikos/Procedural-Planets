# 002 — Terrain-relief diagnosis (why does terrain read flat?)

**Planned at commit `c54fc72`** · Priority P1 · Effort S · Risk Low · Kind: experiment (diagnose, not fix)

Terrain has looked flat for a long time despite a triplanar normal/AO/roughness pipeline that is
confirmed wired (Phase B step 8). The **deliverable is a diagnosis** — measuring the *relative contribution*
of source-normal amplitude, triplanar tiling frequency, and lighting response — backed by labelled A/B
captures. A **MIXED** result (several contributors) is a valid outcome; the plan does not force a single
winner. It is explicitly **not** a tuning pass: per the project's own rule, prove each cause with
binary/extreme control tests before changing any visual constant, and route the eventual fix through
`pp-change-control` (visual-constant discipline). One temporary shader edit is used as a control probe and
**reverted**; the only thing that may land is the diagnosis doc plus a `pp-change-control` follow-up
recommendation per contributor found.

Prior belief (from memory, treat as a lead not a fact): "data pipeline confirmed working; lighting
compression likely the cause." This plan is designed to confirm or refute exactly that.

---

## Drift check (run before starting; if any fails, STOP and re-derive)

```bash
# The terrain lighting block + debug probes
grep -n "DEBUG_TERRAIN_SURFACE_NORMAL\|geometricDiffuse\|terrainDiffuse\|reliefShadow\|float dayLight = lerp" Assets/Graphics/Shaders/PlanetVertexColor.shader
# expect (line numbers approximate, content must match):
#   DEBUG_TERRAIN_SURFACE_NORMAL probe returning (surfaceNormalWS - geomN)*20 *0.5+0.5   (≈1088-1097)
#   float geometricDiffuse = saturate(dot(normalize(input.normalWS), sunDir));           (≈1125)
#   float terrainDiffuse   = saturate(dot(surfaceNormalWS, sunDir));                      (≈1126)
#   float reliefShadow = 1.0 - normalTurnsAwayFromSun * saturate(_BiomeNormalReliefShadow) * daylight; (≈1141)
#   float dayLight = lerp(0.24, 1.12, litDiffuse) * lerp(0.36, 1.0, ao) * reliefShadow;   (≈1142)

# The three control knobs + their defaults — check BOTH sources of truth for tiling (T15)
grep -n "_BiomeNormalStrength\|_BiomeNormalReliefShadow\|_BiomeTriplanarTiling" Assets/Graphics/Shaders/PlanetVertexColor.shader
# expect: _BiomeNormalStrength 2.0, _BiomeNormalReliefShadow 0.55, _BiomeTriplanarTiling 0.055 (Properties block, ≈line 9)
grep -n "_BiomeTriplanarTiling" Assets/Graphics/Materials/Planet.mat
# expect: _BiomeTriplanarTiling: 0.055 (≈line 98). Planet.mat is the runtime clone source (Planet.cs:245 →
# PlanetTerrainMaterial.cs:49 new Material(source)). If the MATERIAL value differs from the shader default,
# the material wins at runtime — read it back and predeclare that baseline; do NOT force 0.055.

# The normal array is a global, set once at bake
grep -n "SetGlobalTexture\|_normalArray\|SurfaceNormal\|placeholderColor" Assets/Scripts/Planet/Biomes/BiomeSurfaceTextureArrays.cs
# expect: Shader.SetGlobalTexture(NormalArrayId, _normalArray); array built with flat placeholder (128,128,255,255)

# Debug-mode enum values (for the console command name)
grep -n "TerrainSurfaceNormal\|TerrainSurfaceAo\|TerrainSurfaceRoughness" Assets/Scripts/Core/Services/DebugModeConstants.cs
# expect: TerrainSurfaceNormal = 83, TerrainSurfaceAo = 84, TerrainSurfaceRoughness = 85
```

If the `dayLight` formula, the debug probe, or the normal-array global-set differs, STOP — the shading
path moved since `c54fc72`; re-anchor the hypotheses on the current code before running control tests.

---

## Why this matters

"Terrain looks flat" is a persistent, vague complaint that has survived multiple polish passes because
nobody has *convicted a single cause*. That's expensive: every future look pass risks tuning the wrong
layer (the grass arc already burned cycles A/B-ing the wrong surface). A cheap, decisive diagnosis measures
which of amplitude / tiling-frequency / lighting-curve actually contributes (they can coexist), so the next
look pass tunes the right lever(s) instead of guessing — or, if the controls don't move relief, names the
next probe rather than forcing a winner. Small effort, high leverage.

## Current state (exact excerpts from `PlanetVertexColor.shader`)

The terrain uses a **custom analytic sun** (URP PBR is bypassed at planet scale) with **no independent
ambient/SH term on the day side** — the day floor is baked into the curve. The relief signal reaches the
final color through exactly two paths: `terrainDiffuse` (diffuse via the perturbed normal) and the Blinn
`specular` (also via the perturbed normal). Everything hinges on `surfaceNormalWS` — the triplanar,
`_BiomeNormalArray`-perturbed normal.

Lighting block (≈1112-1156):
```hlsl
float geometricDiffuse = saturate(dot(normalize(input.normalWS), sunDir)); // pure sphere curvature
float terrainDiffuse   = saturate(dot(surfaceNormalWS, sunDir));           // perturbed by normal map
...
float litDiffuse = terrainDiffuse * sunShadow;
float ao = surfaceArm.r;
...
float normalTurnsAwayFromSun = saturate((geometricDiffuse - terrainDiffuse) * 3.0);
float reliefShadow = 1.0 - normalTurnsAwayFromSun * saturate(_BiomeNormalReliefShadow) * daylight;
float dayLight = lerp(0.24, 1.12, litDiffuse) * lerp(0.36, 1.0, ao) * reliefShadow;   // <-- floor 0.24
dayLight *= lerp(0.45, 1.0, sunShadow);
float3 dayColor = surfaceAlbedo * dayLight * lerp(1.0, cloudShadow, daylight);
```

**Why "flat" is plausible from the math:** `litDiffuse ∈ [0,1]` maps into `dayLight` via `lerp(0.24, 1.12, …)`.
A microfacet whose perturbed normal turns, say, 30° off the geometric normal barely moves `terrainDiffuse`,
and even a fully sun-averted facet only pulls `dayLight` down toward the **0.24 floor** (× the ao/relief
terms). So normal-map contrast is compressed into a narrow brightness band → low apparent relief. That is
**hypothesis H2** and it's the prior favorite — but it must be proven against the alternatives, not assumed.

Debug probes already in the shader (≈1088-1109), selected by `_OceanDebugMode`:
```hlsl
DEBUG_TERRAIN_SURFACE_NORMAL   -> return (surfaceNormalWS - geomN)*20 * 0.5 + 0.5; // mid-gray = flat, speckle = bumpy
DEBUG_TERRAIN_SURFACE_AO       -> return surfaceArm.r (white=lit, dark=occluded)
DEBUG_TERRAIN_SURFACE_ROUGHNESS-> return surfaceArm.g
```
These map to debug-mode enum values `TerrainSurfaceNormal=83`, `TerrainSurfaceAo=84`, `TerrainSurfaceRoughness=85`.

**Normal source** — `BiomeSurfaceTextureArrays.Build()` assembles `_normalArray` per biome (`def.SurfaceNormal`),
falling back to a **flat placeholder `(128,128,255,255)`** when a biome has no authored normal, and sets it
as a global: `Shader.SetGlobalTexture(NormalArrayId, _normalArray)`. If many biomes hit the placeholder,
`surfaceNormalWS ≈ geometric normal` everywhere → the debug probe reads mid-gray → that's **hypothesis H1/H3**.

## The hypotheses

**Already refuted — regression checks only, not live hypotheses:**
- **H1-placeholder / H3 (normals absent or not reaching the shader).** `Logs/Editor.log` reports
  `BiomeNormalArray: … 16/16 slices from source, 0 placeholder`; all 15 biome assets carry a non-null
  `SurfaceNormal`; failure-archaeology entry 10 (`.agent-skills/pp-failure-archaeology/SKILL.md:343-360`)
  records mode-83 showing vivid perturbation and modes 84/85 showing source content. Treat these as one-shot
  regression re-checks (Step 2): a *changed* log or an unexpectedly mid-gray mode-83 is the only evidence
  that would reopen them.

**The genuine open question — these two can coexist; measure relative effect size, don't force one to win:**
- **H1-weak — source relief is subtle at production scale.** Two mechanisms: (a) `_BiomeNormalStrength`
  (default 2.0) too low; (b) `_BiomeTriplanarTiling` (default `0.055`) puts the texture frequency
  **sub-pixel at viewing altitude**, so authored detail averages out (a *frequency/scale* problem, not
  amplitude — entry 10 flags this).
  Fix would be strength/tiling authoring.
- **H2 — lighting curve compresses relief.** `dayLight = lerp(0.24, 1.12, litDiffuse) * …` — the `0.24`
  floor lifts the black level (washes bump shadows) and the `0.88` slope compresses `terrainDiffuse`
  contrast; `reliefShadow` (`_BiomeNormalReliefShadow` 0.55) is a weak secondary. Fix would be the lighting
  constants (via `pp-change-control`).

Mode 83 (×20 amplified) proves a *non-zero* perturbation reaches the shader; it does NOT prove the relief is
*strong enough at production scale* — so it cannot by itself settle H1-weak vs H2. That needs the effect-size
matrix (Steps 3-4).

## Commands

**No `dotnet build` here (T19).** This is a shader-only experiment; `dotnet` does not compile HLSL and adds
no evidence. The Step-4 gate is: forced Unity shader import → clean Unity shader compile → known-hunk revert →
second forced import → empty targeted `git diff` on the shader.

Everything else is play-mode capture via the run/operate + debugging skills
(`.agent-skills/pp-run-and-operate/`, `.agent-skills/pp-debugging-playbook/`):
- Change render mode: console `debug.mode TerrainSurfaceNormal` (also `TerrainSurfaceAo`, `TerrainSurfaceRoughness`,
  and `Off`/default to restore). If the exact token differs, run `debug.mode` with no arg to list names.
- Capture the current game view: console `debug.screenshot` (one image of the *current* debug mode —
  NOT `debug.capture`, which iterates the whole capture set into the wrong modes).
- **Archive before the pipeline prunes (T14).** `DebugCapturePipeline` keeps only `MaxCaptureRuns = 6`
  (`DebugCapturePipeline.cs:31`) and prunes oldest-first by write time after *every* save
  (`:285-287`) under `local-only/debug-screenshots`. In single-mode capture (what this diagnosis uses) the
  retained floor is 6 pairs, so a >6-shot run **evicts Step 1's baseline before Step 5**. After each A/B
  group, copy every `.png` + `.txt` pair into a dated stable subdir
  `local-only/debug-screenshots/baselines/2026-08-09-terrain-relief/` and record the archived filenames in
  the result table — do this *before* starting the next group.
- Fixed pose + **oblique** sun (the primary diagnostic): `camera.teleport "<saved spot>"` FIRST (pin the
  camera — `time.set-local` is measured relative to camera position, `CelestialManager.cs:215-236`, so it is
  only repeatable once the camera is fixed), then `time.set-local 0.35` + `time.freeze true`. Noon is the
  WORST angle for relief (sun ≈ geometric normal → both diffuse terms saturate near 1 → perturbations barely
  register); use it only as a secondary production-view sanity check, never as the H2 control.
- Lit-response capture: debug mode **82 `TerrainSunLighting`** shows the actual lit terrain response
  (`terrainDiffuse` in the **blue** channel; daylight=R, cloudShadow=G — not a grayscale image). Pair it
  with mode 83 (the ×20 normal-delta probe). Reuse the "Step Test" saved pose if present.

**Shader gotchas (cost real cycles before — obey):**
- Shader **CODE** edits do NOT hot-reload in play mode. After editing `PlanetVertexColor.shader`, force a
  recompile: `UnityEditor.AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ForceSynchronousImport)`
  (via Unity MCP `execute_code`), or the OLD compiled shader keeps running and your edit looks like a no-op.
- Material **PROPERTY** changes (`Material.SetFloat("_BiomeNormalStrength", …)`) DO apply live — use those
  for the strength control test (no recompile needed).
- **There is exactly ONE runtime terrain material, not "per chunk."** `PlanetTerrainMaterial.EnsureRuntime`
  creates a single `Planet/VertexColor` clone (`PlanetTerrainMaterial.cs:41-50`); every pooled chunk
  renderer shares it via `renderer.sharedMaterial` (`ChunkMeshCache.cs:181`), with per-chunk data in a
  `MaterialPropertyBlock` — no per-chunk material instances. The control test = `Material.SetFloat` on that
  one clone (`_terrainMaterial.Material`). **`Shader.SetGlobalFloat` does NOT work** for `_BiomeNormalStrength`
  / `_BiomeNormalReliefShadow`: both live in `CBUFFER_START(UnityPerMaterial)` (`PlanetVertexColor.shader:100-108`),
  so the shader reads them from the per-material constant buffer, never the global store. Never use
  `renderer.material` (it clones per-renderer, breaking the shared model). Note: the clone is rebuilt on
  every regenerate, so a control value resets on the next world gen — set it after generation, each run.

**Reaching the runtime material (T17/T21 — there is NO console path; paste-ready helper below).**
`_terrainMaterial` is a private field on `Planet` (`Planet.cs:51`), `PlanetTerrainMaterial` is internal/sealed
with only a private `_runtime` clone, and **no console command exposes `_BiomeNormalStrength` /
`_BiomeTriplanarTiling`** (repo-wide grep: zero). So `_terrainMaterial.Material.SetFloat(...)` is not operable
from the console — use the Unity-MCP `execute_code` tool.

**Preflight (T21):** `execute_code` is `AutoRegister = false`, group `scripting_ext`
(`…/unity-mcp@…/Editor/Tools/ExecuteCode.cs:16`) — the **"Scripting Extensions"** toggle in the MCP-for-Unity
window. Confirm the tool is callable before entering play mode (list MCP tools / probe it). If it is
unavailable, **STOP** and agree a temporary probe console command with Bryan — do **not** add an unreviewed
permanent tuning API. The runtime clone is uniquely named `"… (runtime)"` (`PlanetTerrainMaterial.cs:49`).

Paste-ready get/set (assert every write; restore the recorded baseline on cleanup/STOP):
```csharp
// execute_code — get or set a terrain-material float. Returns "instanceId | before -> after".
// Call after generation. For a GET pass value = null.
string prop = "_BiomeTriplanarTiling";   // or "_BiomeNormalStrength"
float? value = 0.0055f;                   // null = read only
var mats = Resources.FindObjectsOfTypeAll<Material>()
    .Where(m => m.shader != null && m.shader.name == "Planet/VertexColor" && m.name.EndsWith(" (runtime)"))
    .Distinct().ToList();
if (mats.Count != 1) throw new System.Exception($"expected exactly 1 runtime terrain material, found {mats.Count}");
var mat = mats[0];
if (!mat.HasProperty(prop)) throw new System.Exception($"material lacks {prop}");
float before = mat.GetFloat(prop);
if (value.HasValue) { mat.SetFloat(prop, value.Value); float after = mat.GetFloat(prop);
    if (Mathf.Abs(after - value.Value) > 1e-6f) throw new System.Exception($"write not applied: {after}"); }
return $"{mat.GetInstanceID()} | {before} -> {mat.GetFloat(prop)}";
```
Label every capture via `debug.screenshot`'s label arg (`debug.screenshot "baseline-off"`,
`DebugCaptureController.cs:349` → filename) so the A/B labels appear in the filenames, not only the write-up.

## Scope

**In scope:**
- Read-only captures with existing debug modes (Steps 1-3).
- ONE temporary shader-CODE control probe (Step 4), **reverted** after capture — never committed.
- Live material-property control tests (Step 3) — runtime only, reverted by ending play mode.
- The diagnosis write-up (Step 5) — the actual deliverable. May land as a short doc under `docs/`.

**Out of scope — do NOT do:**
- Ship a tuning change. Even if H2 is obvious, the fix is a separate `pp-change-control` change, not this plan.
- Touch caustics / `Ocean.shader` (project DON'T-TOUCH).
- Author new normal-map textures (that's the H1 fix, a follow-up).
- Change `_BiomeNormalArray` assembly or the triplanar sampling code as a "fix."
- Commit any temporary probe edit.

## Steps (ordered; each ends with a capture)

### Step 1 — Fix the pose + oblique sun, prove the setup
`camera.teleport` to a terrain spot with visible large-scale relief (a hill/ridge, not flat plain). Pin the
camera FIRST, then `time.set-local 0.35` + `time.freeze true` (oblique sun — see Commands for why noon is
the wrong angle). **Freeze weather too (T16):** `time.freeze` freezes only the sun; weather evolution is
independent (`weather.freeze`, `WeatherManager.cs:69-74`) and its `_CloudWindAngle` keeps advecting the
cloud-shadow field, which multiplies into the `Off` day color (`PlanetVertexColor.shader:1144`, daylight-weighted)
— so drifting clouds can masquerade as a curve/strength delta across the long 2×2 + reimport sequence.
**Also freeze wind-driven caster motion (T22):** `weather.freeze` stops the cloud grid but NOT Unity `_Time`
— foliage sway is `_Time.y`-driven (`FoliageLit.ApplyWind`) and applied in its `ShadowCaster` pass, and the
terrain day path samples `MainLightRealtimeShadow` (`PlanetVertexColor.shader:1132`), so animated leaves keep
casting *moving* shadows onto the terrain (daytime only — gated by `daylight`). `weather.wind-speed 0` zeroes
`_WindStrength01`/`_WindSpeedMps` → `ApplyWind` early-outs → static foliage + static cast shadow.
**Query and record `time.freeze`, `weather.freeze`, and `weather.wind-speed` originals**, then set
`weather.freeze true` + `weather.wind-speed 0` (wait one frame for the dirty global upload), keep all three
fixed through every cell, and **restore all three** in the cleanup/STOP path. (Alternatively, use an open
patch with no dynamic caster shadow crossing the comparison region and verify two identical baseline `Off`
captures separated by the run duration — but zero-wind is the more reliable, already-exposed route.) Keep
seed + quality tier fixed so every later capture differs only by the one variable under test. Take a baseline
`debug.screenshot "baseline"` in normal render mode + mode 82 (`TerrainSunLighting`). This is the "flat"
reference frame.

**Precondition (T7):** `git diff -- Assets/Graphics/Shaders/PlanetVertexColor.shader` must be empty before
any control test. If it is not, STOP and preserve the existing patch — do not try to make the final diff
empty by hand.

**Capture:** `baseline-normal-render.png`, `baseline-sun-lighting.png` + the sidecar.

### Step 2 — Regression check: confirm normals still reach the shader
This is NOT a live hypothesis test (H1-placeholder/H3 are already refuted, see Hypotheses) — it revalidates
the two cheap facts against the current build. `debug.mode TerrainSurfaceNormal` → `debug.screenshot`.
- **Bright speckle tracking terrain detail** (expected) → normals present + perturbing; proceed to the
  effect-size matrix (Steps 3-4). Also grab `TerrainSurfaceAo` + `TerrainSurfaceRoughness` — expect source
  content, not flat.
- **Mid-gray almost everywhere** (regression!) → something changed since entry 10 / `Editor.log`'s
  `0 placeholder`. This reopens H3; STOP and report the regression before continuing.

**Capture:** `probe-surface-normal.png` (+ ao/roughness). Record: speckle (expected) vs gray (regression).

### Step 3 — H1-weak effect-size control (live, no recompile)
Measures how much source-normal amplitude contributes. `Material.SetFloat("_BiomeNormalStrength", …)` on the
**single shared runtime clone** `_terrainMaterial.Material` (NOT `Shader.SetGlobalFloat` — the prop is
per-material, see Commands; NOT `renderer.material`). Read back the starting value first, restore it (or end
play mode) after. Hold pose/sun/seed/tier fixed; change only strength.
- **A/B floor + geometry baseline (T13):** strength 0 must drop mode-83 speckle to mid-gray — proves the
  material write reaches the rendered material (guards a silent no-op). **Also capture the `Off` view at
  strength 0.** This is the strength-0 / baseline-curve cell of the Step-4 2×2 (macro geometric contrast with
  *no* normal-map relief) — the reference the widened-curve/strength-0 capture is compared against.
- **Amplitude:** strength 8-12. If the **normal render** (not just mode 83) reads bumpier → source amplitude
  is a real contributor. If mode 83 responds but the normal render does not, the limiter is
  downstream/scale-dependent (points at H2 or the tiling mechanism below).
- **Tiling (the frequency mechanism) — run it as its own control, not just a suspicion.** Restore strength
  to baseline, then `Material.SetFloat("_BiomeTriplanarTiling", …)` on the same runtime clone: **read back the
  actual baseline** `baselineTiling` (whatever the drift check recorded — do NOT hardcode `0.055`), and set
  the probe **derived from it (T18):** `probeTiling = max(0.001, baselineTiling / 10)` (stop if `Range`
  clipping prevents the predeclared 10× — record the actual factor). Hold pose/sun/seed/tier/strength/curve/
  relief fixed. Capture modes 83 + 82, **labelled with the actual probe value**; treat the `Off` view as
  context only (the shared tiling prop also shifts albedo + ARM frequency). A stronger lit response → tiling
  frequency is a real H1-weak contributor. Restore the exact read-back `baselineTiling` before the Step-4 curve
  probe. (If you deliberately skip this control, remove tiling from the diagnosed contributors and record it as
  the next probe — do not claim it was measured.)

**Capture** (label with actual values): `strength-0-mode83.png`, `strength-0-off.png`,
`strength-12-normal-render.png`, `strength-12-probe.png`, `tiling-<probe>-mode83.png`, `tiling-<probe>-mode82.png`.

### Step 4 — H2 effect-size control (ONE variable per capture)
Measures how much the lighting curve compresses relief. Restore strength to its baseline (2.0) first, then
change **only** the `dayLight` diffuse curve — hold normal strength, relief-shadow, AO, pose, seed, tier,
and local sun fixed. Preserve the exact original endpoints in the write-up so the edit has a known inverse.
Temporary shader-CODE probe in `PlanetVertexColor.shader`:
```hlsl
// TEMP PROBE — revert. Widen the diffuse curve to expose normal-map contrast. (Change ONLY this line.)
float dayLight = lerp(0.02, 1.35, litDiffuse) * lerp(0.36, 1.0, ao) * reliefShadow;
```
Force-import the shader (see gotcha), re-capture the normal render + mode 82 + `Off` at the same pose.

**Mode 82 is the H2 negative control (T13).** Mode 82 returns `(daylight, cloudShadow, terrainDiffuse, 1)` at
`:1073-1083` — *before* the edited `dayLight` at `:1142` — so the curve edit must NOT change it. Predeclare:
mode 82's blue channel stays equivalent within capture jitter. If it moves, pose/sun/material/debug state
drifted → the run is invalid; stop and re-pin.

**Isolate normal-map relief from geometry — complete the 2×2 (T13).** A widened curve also amplifies ordinary
sphere/mesh *curvature*, which reads as "relief" even if normal maps aren't the cause. Compare four `Off`
captures: {baseline curve, widened curve} × {strength 2, strength 0}. Baseline×{2,0} came from Step 3. Now,
with the widened curve active, capture widened×strength-2 (the normal render above), then set
`_BiomeNormalStrength = 0`, capture widened×strength-0 `Off`, and restore strength. Interpretation:
- The **extra** relief in widened×strength-2 *beyond* widened×strength-0 is the normal-map contribution the
  curve amplifies → supports **H2 compresses normal-map relief**.
- If widened×strength-0 gains as much as widened×strength-2, the widening is amplifying macro geometry, not
  normal-map relief → NOT proof of H2 as phrased; record that.
Each capture still changes exactly one variable from the immediately preceding state.

If `_BiomeNormalReliefShadow` is still worth testing, restore the curve first and test it as a **separate**
control capture — never combined with the curve change.

**Revert the shader edit and force-import again.** Confirm clean: `git diff -- Assets/Graphics/Shaders/PlanetVertexColor.shader`
shows nothing (T7).

**Capture:** `curve-widened-str2-normal.png`, `curve-widened-str2-mode82.png`, `curve-widened-str0-off.png`;
confirm the revert.

### Step 5 — Write the diagnosis (the deliverable)
On approval this plan promotes to `docs/design/2026-08-08-terrain-relief.md`; write the result **in that
promoted plan** — do NOT spin up a separate `docs/research`/`docs/agent-conversation` file (it would drift
from the plan) and do NOT create an ad-hoc `docs/diagnosis/` directory. Use the experiment template:
prediction-before-result, capture references, a refutation table, and a verdict from
`ADOPT | RETIRE | RETIRE-WITH-KEEPS | INCONCLUSIVE(<next probe>)`.

Report **effect sizes, not a single winner** — H1-weak and H2-compression can coexist. For each contributor
with an A/B capture, name the exact lever:
- H2 → the `dayLight` floor `0.24` / slope and `_BiomeNormalReliefShadow`; fix is a `pp-change-control`
  visual-constant change, with the probe values that looked right as a starting point.
- H1-weak → `_BiomeNormalStrength` and/or `_BiomeTriplanarTiling` (default `0.055`, sub-pixel at altitude);
  fix is strength/tiling authoring.
- If neither control moved the lit render, the verdict is `INCONCLUSIVE` and the next probe is a
  procedurally-obvious sine-bump normal injected into `_BiomeNormalArray` (entry 10's recommended probe) —
  do NOT start editing unrelated subsystems.

**Scope the verdict to the sampled biome (T20/T23).** The 15 authored normal maps need not respond equally to a
*global* strength/tiling multiplier. **Identify the biome with a command that actually names it (T23):** run
`scatter.count` at the pinned pose — it prints `DescribeBiomeAt` (`ScatterField.cs:414,398-409`): named
primary + secondary + blend weight. (`debug.mode BiomeMapPrimaryId` only paints an unlabelled categorical
color; the sidecar carries only aggregate atlas stats — neither names the local biome.) Before recommending a
**global** H1 strength/tiling change, reach a contrasting biome with `scatter.goto <BiomeType>`
(`ScatterField.cs:575`), **re-save the pose and rerun `time.set-local 0.35`** (local time is camera-relative,
so it must be re-applied after moving), then repeat **only the winning control pair** there. If it holds, the
recommendation is global; otherwise word the verdict as applying to the sampled spot and make cross-biome
validation the follow-up. (One confirmation pair suffices — do not repeat the full matrix. If `scatter.*` is
unavailable in the target scene, record the color-coded spot/pose rather than claiming a named biome.)

Update failure-archaeology entry 10 (`.agent-skills/pp-failure-archaeology/SKILL.md`) if its OPEN status
changes, in the same reviewed documentation change.

**Verify:** the doc records each contributor's verdict with the capture that proves it (MIXED / INCONCLUSIVE
allowed), and the shader tree is clean (no leftover probe edit).

## Test plan

No automated tests — this is a visual diagnosis. "Tests" = the labelled A/B captures at a fixed pose, each
tied to the hypothesis it confirms/refutes. Repeatability comes from the pinned teleport pose + frozen
**oblique** local sun (`time.set-local 0.35`; noon is the wrong angle — see Commands — an optional secondary
sanity view only); capture the same frame across control tests so differences are attributable to the one
changed variable (one variable at a time — the systematic-debugging discipline).

## Done criteria

- The diagnosis is written into the promoted `docs/design/` plan (not `docs/diagnosis/`, not a separate
  research file), using the experiment template, with a verdict from
  `ADOPT | RETIRE | RETIRE-WITH-KEEPS | INCONCLUSIVE(<next probe>)`. A MIXED
  result (both H1-weak and H2 contribute) is valid provided each named contributor has its own A/B capture;
  an INCONCLUSIVE result names the next binary probe.
- If H2 contributes: the doc names the specific constant(s) and defers the change to a `pp-change-control`
  follow-up (this plan does NOT change them).
- The working tree is clean of the temporary Step-4 probe (`git diff` on the shader is empty).
- No visual constant was changed and committed by this plan; no caustics/`Ocean.shader` touched; no new
  normal textures authored.

## STOP conditions (report back)

- The `TerrainSurfaceNormal` probe shows speckle in some biomes and mid-gray in others → mixed cause (some
  biomes authored, some placeholder). Report the split rather than forcing one hypothesis.
- Forcing the shader recompile doesn't change the output (edit looks ignored) → you hit the hot-reload
  gotcha; confirm the force-import ran before concluding anything about H2.
- Widening the lighting curve AND cranking strength both fail to produce relief → the flatness is somewhere
  this plan didn't model (e.g. mesh normals themselves, or vertex-color washing out albedo). Stop and report
  — don't start editing other subsystems.

## Maintenance notes

- The convicted fix is a **separate** change: H2 → a `pp-change-control` constant tune; H1 → normal-map
  authoring or a strength default; H3 → a pipeline bug. Link this diagnosis from whichever follow-up lands.
- The debug probes (`TerrainSurfaceNormal/Ao/Roughness`) are the permanent tool for this class of question —
  reach for them first in any future "terrain looks wrong" investigation before touching constants.
- Watch, when the fix lands: raising the `dayLight` floor or relief strength interacts with the night-side
  blend and the grass/foliage ambient floors (they were tuned to match terrain in the recent look arc) —
  re-verify grass/terrain brightness parity after any curve change, not just the terrain in isolation.

---

## Codex review feedback — 2026-08-08

**Verdict: revise, then run.** The experiment is worth doing, but its current branches reopen settled
upstream facts and cannot uniquely distinguish the hypotheses as written. Narrow it to measuring the
relative contribution of source-normal amplitude versus lighting-response compression.

### Blocking corrections

**T1 — Treat H1-placeholder and H3 as regression checks, not live peer hypotheses.** The current biome
assets all have non-null `SurfaceNormal` references, and the latest editor log reports
`BiomeNormalArray: 512x512 RGBA32, 16/16 slices from source, 0 placeholder`
(`Logs/Editor.log:1021`). Failure-archaeology entry 10 also records that mode 83 previously showed vivid
perturbation and modes 84/85 showed source content. Start by revalidating those two cheap facts against
the current run. If they still hold, mark “placeholder/absent pipeline” as already refuted and do not
spend Steps 2-3 re-auditing it. A changed log or unexpectedly gray mode 83 is regression evidence and
justifies reopening that branch.

**T2 — The current branch logic is not discriminating.** Mode 83 multiplies the normal delta by 20, so
visible speckle proves a non-zero sampled perturbation but does **not** refute the “authored normals are
too weak at production scale” part of H1. Conversely, an x12 strength test that stays gray cannot by
itself distinguish a flat placeholder from a failed property write or broken sampling. Replace the
branch prose with a predict-first matrix and keep the controls independent:

1. Baseline strength 2, baseline curve: capture `Off`, `TerrainSurfaceNormal`, and
   `TerrainSunLighting`.
2. If the baseline mode is speckled, strength 0 with the baseline curve must change it to mid-gray;
   that A/B transition proves the live material control is hitting the rendered material.
3. Strength 8 or 12, baseline curve: capture the same modes. A stronger final-view response establishes
   source-normal amplitude as a contributor. If mode 83 responds but `Off` does not, the limiter is
   downstream/scale-dependent; if mode 83 does not respond after a verified material write, the
   normal-scaling/sampling branch remains open.
4. Restore strength 2, change only the `dayLight` response curve, and recapture. A stronger response
   establishes curve compression as a contributor.

These causes can coexist. Compare their effect sizes instead of requiring one to eliminate the other.

**T3 — Target the actual shared runtime material.** The plan's “~90-116 material instances” statement is
incorrect for the current chunk path. `PlanetTerrainMaterial.EnsureRuntime` creates one clone
(`PlanetTerrainMaterial.cs:40-50`), and every pooled chunk renderer receives that clone via
`renderer.sharedMaterial` (`ChunkMeshCache.cs:177-184`). `_BiomeNormalStrength` and
`_BiomeNormalReliefShadow` are in `UnityPerMaterial` (`PlanetVertexColor.shader:100-108`), so
`Shader.SetGlobalFloat` is not a valid control for them. Locate the unique live
`Planet/VertexColor` **sharedMaterial**, assert/read back its starting value, call `Material.SetFloat`,
and restore it (or end play mode). Never access `renderer.material`, which would manufacture per-renderer
clones and make the plan's false instance count come true.

**T4 — Change one visual variable per capture.** Step 4 currently widens two curve endpoints and says
“and/or” boost `_BiomeNormalReliefShadow`. That cannot attribute the result. Hold normal strength,
relief-shadow strength, AO, pose, seed, tier, and local sun fixed while changing only the diffuse curve.
If relief shadow is still worth testing, restore the curve and capture it as a separate control. Preserve
the existing curve endpoints in the doc so each temporary edit has an exact inverse.

### Experiment and evidence amendments

**T5 — Use an oblique local sun for the primary diagnostic.** Local noon aligns the sun most closely
with the geometric normal, where small normal perturbations produce the least first-order diffuse
contrast. Pin the camera first, then use an oblique repeatable value such as `time.set-local 0.35` and
`time.freeze true`; record the actual sidecar pose and sun state. Noon may be a secondary production
view, not the only H2 control. Archive the baseline PNG **and sidecar** before touching the shader, and
keep seed and quality tier fixed.

**T6 — Allow `MIXED` and `INCONCLUSIVE` verdicts.** H1-weak and H2-compression are not mutually exclusive,
and the project research method explicitly permits `INCONCLUSIVE (<next discriminating probe>)`. Replace
the done criterion requiring “exactly ONE” primary hypothesis. A valid result can be mixed, provided each
claimed contributor has its own A/B capture; an inconclusive result must name the next binary probe. The
failure ledger's recommended next probe is a procedurally obvious normal, which is preferable to editing
unrelated subsystems if both strength and curve controls are weak.

**T7 — Protect a future dirty shader before the temporary edit.** Add a precondition that
`git diff -- Assets/Graphics/Shaders/PlanetVertexColor.shader` is empty. If it is not, STOP and preserve
the existing patch rather than trying to make the final diff empty. After the probe, reverse only the
known hunk, force-import again, and verify both the source diff and Unity shader compile are clean.
`dotnet build` adds no evidence for a shader-only probe and can be omitted; Unity import/compile is the
relevant gate.

**T8 — Put the result in an existing date-stamped doc category.** Do not create an ad-hoc
`docs/diagnosis/` directory. Use `docs/research/2026-08-08-terrain-relief.md` (or the execution date) with
the repository experiment template: prediction before result, capture references, refutation table, and
`ADOPT | RETIRE | INCONCLUSIVE` verdict. If the open issue changes state, update failure-archaeology entry
10 in the same reviewed documentation change. Any actual visual fix remains a separate
`pp-change-control` plan and requires Bryan's capture review.

---

## Claude verification of Codex feedback — 2026-08-08

I re-read each cited range in the current tree. **T1-T7 CONFIRMED; T8 CONFIRMED-on-substance, PARTIAL on
specifics.** Codex caught two hard factual bugs (T3, T5) that would have made the experiment either
un-runnable or blind. Details and additions:

| Item | Verdict | My addition / nuance |
|------|---------|----------------------|
| **T1** H1-placeholder/H3 settled | CONFIRMED | `Logs/Editor.log:1019` reads `BiomeNormalArray: … 16/16 slices from source, 0 placeholder`; all 15 biome assets carry non-null `SurfaceNormal`; failure-archaeology entry 10 records mode-83 showing vivid perturbation. So "placeholder normals" / "normals don't reach shader" are **refuted** — demote to one-shot regression checks. **My addition:** entry 10 (`.agent-skills/pp-failure-archaeology/SKILL.md:343-360`) already names a *second* H1-weak mechanism I omitted — the `0.065` triplanar tiling going **sub-pixel at viewing altitude** (spatial **frequency**, not amplitude). The experiment must vary tiling/scale, not only `_BiomeNormalStrength`. |
| **T2** probe not discriminating | CONFIRMED | Mode-83's ×20 amplification means speckle proves *non-zero* perturbation, not *production-strength*. Use a predict-first matrix with independent single-variable controls. |
| **T3** material count + `SetGlobalFloat` | CONFIRMED (no caveat) | **Both my claims were wrong.** There is **one** runtime `Planet/VertexColor` clone (`PlanetTerrainMaterial.EnsureRuntime`), shared across all chunks via `renderer.sharedMaterial` with per-chunk data in a `MaterialPropertyBlock` — not "~90-116 instances." `_BiomeNormalStrength`/`_BiomeNormalReliefShadow` live in `CBUFFER_START(UnityPerMaterial)` (`PlanetVertexColor.shader:100-108`), so `Shader.SetGlobalFloat` has **no effect**. Correct control = `Material.SetFloat` on `_terrainMaterial.Material` (the single clone); never `renderer.material` (clones per-renderer). **Add:** the clone is rebuilt each regenerate, so a control value resets on the next world gen. |
| **T4** one variable per capture | CONFIRMED | My Step 4 "and/or boost `_BiomeNormalReliefShadow`" cannot attribute a result. Hold everything fixed, change only the diffuse curve; test relief-shadow as a separate control. |
| **T5** noon is the worst angle | CONFIRMED | I independently confirmed the physics: at noon the sun ≈ geometric normal, both diffuse terms saturate near 1, and `d(terrainDiffuse)/d(normal) ≈ 0` — perturbations barely register. Tooling all exists: `time.set-local` + `time.freeze` + debug mode **82 TerrainSunLighting**. **Two hard preconditions Codex under-stated:** `time.set-local` is **camera-relative** (`CelestialManager.cs:215-236`) → pin the camera *first* or it's non-repeatable; and mode 82 packs `terrainDiffuse` in the **blue** channel (daylight=R, cloudShadow=G) — not a grayscale lit image. |
| **T6** allow MIXED/INCONCLUSIVE | CONFIRMED | H1-weak and H2-compression coexist (entry 10 lists both). Drop the "exactly ONE hypothesis" done-criterion; permit `MIXED` (each contributor with its own A/B) and `INCONCLUSIVE(<next probe>)`. |
| **T7** protect dirty shader | CONFIRMED | Precondition: `git diff -- …/PlanetVertexColor.shader` empty before the probe; if not, **STOP** and preserve the existing patch (don't force the final diff empty). `dotnet build` adds no evidence for a shader-only probe — Unity import/compile is the gate. |
| **T8** doc location | PARTIAL | Substance CONFIRMED: **do not** create ad-hoc `docs/diagnosis/`; use an existing date-stamped category with the experiment template. **But** Codex over-specified `docs/research/`: that dir is narrowly a *literature-survey* digest (`pp-docs-and-memory` SKILL); the experiment template's own homes (`pp-research-methodology/SKILL.md:148`) are a **design doc or `docs/agent-conversation/` entry**. Load-bearing rule = "existing category + experiment template," not "must be research." Also the verdict enum includes **RETIRE-WITH-KEEPS** (`:160`), and failure-archaeology entry 10 (a **skill** ledger, not a `docs/` file) must be updated on any status change. |

### Required body edits before execute — APPLIED 2026-08-08

All six corrections below are now folded into the plan body above (Commands, Hypotheses, Steps 1-5, Done
criteria). Retained as the change record for Codex's second-opinion pass.

1. **"Freeze at noon"** (Commands + Step 1) → pin camera first, then `time.set-local 0.35` + `time.freeze true`;
   capture mode **82** (TerrainSunLighting, blue = lit response) **and** mode **83** (TerrainSurfaceNormal).
   Keep noon only as a secondary production-view sanity check.
2. **Commands** → drop "~90-116 material instances" and `Shader.SetGlobalFloat`; the control is
   `Material.SetFloat` on the single `_terrainMaterial.Material` clone; note it resets on regenerate.
3. **Hypotheses** → demote H1-placeholder + H3 to regression checks; add the H1-weak **tiling-frequency**
   mechanism (0.065 triplanar sub-pixel at altitude); frame the real question as H1-weak-at-scale vs
   H2-curve-compression, which **can coexist** — measure effect size, don't force elimination.
4. **Step 4** → change one curve variable per capture (remove "and/or"); relief-shadow is a separate control.
5. **Step 5 (write-up)** → `docs/diagnosis/` → an existing category (`docs/research/` **or**
   `docs/agent-conversation/`) using the `pp-research-methodology` experiment template; verdict enum
   `ADOPT | RETIRE | RETIRE-WITH-KEEPS | INCONCLUSIVE(<next probe>)`; allow MIXED; update failure-archaeology
   entry 10 on status change.
6. **Add T7 precondition:** clean shader diff before the Step-4 probe.

The experiment's spine (debug-probe convicts, extreme control tests, diagnosis-not-tuning, `pp-change-control`
defers the fix) is sound. T3 and T5 are the must-fix bugs; the rest tightens rigor.

---

## Codex re-review feedback — 2026-08-08

**Verdict: revise three mechanical items, then run.** The revised experiment now isolates source-normal
strength from lighting response and protects the dirty shader correctly. The remaining issues are a stale
constant, an unexecuted hypothesis branch, and one leftover noon instruction.

**T9 — Use the stamped tiling value and run the tiling control the verification section requires.** At
commit `c54fc72`, `PlanetVertexColor.shader` declares `_BiomeTriplanarTiling = 0.055` (one tile per about
18 world units), not `0.065`. More importantly, Step 3 only says to *suspect* tiling after a weak strength
result even though the revised hypotheses and Claude verification say the experiment must vary scale.
That cannot measure tiling's effect size.

Add an independent live material-property A/B after restoring normal strength to baseline:

1. Read back `_BiomeTriplanarTiling` from the one runtime material and assert the baseline is `0.055`.
2. Change only that property to the predeclared extreme lower-frequency control `0.0055` (10x larger
   texel footprint), keeping pose, sun, seed, tier, strength, curve, and relief shadow fixed.
3. Capture modes 83 and 82. Mode 83 isolates the sampled normal delta; mode 82's blue channel isolates its
   diffuse response. Treat the normal `Off` view as supporting context because the shared tiling property
   also changes albedo and ARM frequency there.
4. Restore `0.055` and read it back before the H2 curve probe.

This is a runtime `Material.SetFloat` control on `_terrainMaterial.Material`; it needs no shader edit or
recompile. If the experiment deliberately declines this control, remove tiling from the hypotheses and
state it as the next probe rather than claiming H1-weak-at-scale was diagnosed.

**T10 — Replace the leftover noon condition.** The Test plan still says repeatability comes from a
"frozen noon sun" (lines 265-268). That contradicts Commands and Step 1. It must say the pinned camera plus
frozen oblique local sun (`time.set-local 0.35`), with noon only as the optional secondary sanity view.

**T11 — Keep the result with the promoted experiment plan.** On approval, promote this staging file to a
date-stamped `docs/design/` plan and fill the experiment template/result there. `docs/research/` is the
project's external-literature digest category, while `docs/agent-conversation/` is primarily a cross-agent
handoff record. The owning design doc is the unambiguous home for predictions, captures, refutation table,
verdict, and next action. This also avoids creating a second result document that can drift from the plan.

After T9-T11, the plan is ready for Bryan's approval and execution; no further shader or architecture
work is implied by this review.

---

## Claude verification of Codex re-review — 2026-08-08

Verified against the tree (parallel agents). **T9 + T10 CONFIRMED; T11 concur.** No overreach.

| Item | Verdict | Detail |
|------|---------|--------|
| **T9** tiling constant + control | CONFIRMED | Default is `_BiomeTriplanarTiling = 0.055` (git-show at `c54fc72`, `PlanetVertexColor.shader:9`), **not** my `0.065`. It's in `UnityPerMaterial` and directly scales the triplanar sample UVs (`:337-341`) — so it IS the frequency knob for the "sub-pixel at altitude" hypothesis, and a live `Material.SetFloat("_BiomeTriplanarTiling", 0.055 → 0.0055)` on the single runtime clone is a valid, executable control (0.0055 is within `Range(0.001,1.0)`). I named tiling but never made the experiment vary it — must add the control (or explicitly downgrade tiling to "next probe," not claim it was diagnosed). |
| **T10** leftover noon | CONFIRMED | Test-plan line 266 still says "frozen noon sun," contradicting corrected Commands + Step 1. Rewrite to the pinned pose + frozen **oblique** local sun (`time.set-local 0.35`), noon only an optional secondary sanity view. |
| **T11** doc home | CONCUR (refines T8) | Put the result **in the promoted `docs/design/` plan itself** (predictions → captures → refutation table → verdict → next action), not a separate `docs/research`/`agent-conversation` file — the owning design doc is the unambiguous home and avoids a second doc drifting from the plan. Adopt over my earlier T8 "research-or-agent-conversation" note. |

### Body edits — APPLIED 2026-08-08

All three folded into the body above; retained as the change record.

1. **T9 (applied):** `0.065` → `0.055` in Hypotheses + Step 5; Step 3 now runs a real tiling A/B
   (`Material.SetFloat("_BiomeTriplanarTiling", 0.055 → 0.0055)`, read-back + restore, modes 83/82).
2. **T10 (applied):** Test plan now says pinned pose + frozen oblique sun (`time.set-local 0.35`).
3. **T11 (applied):** Step 5 + Done criteria target the promoted `docs/design/2026-08-08-terrain-relief.md`
   plan (dropped the `docs/research`/`agent-conversation` alternatives); still updates failure-archaeology
   entry 10 on status change.

002 had no open design question (unlike 001). Ready to execute on approval.

---

## Codex third review feedback — 2026-08-08

**Verdict: nearly ready; tighten the evidence contract before execution.** The oblique-sun setup, live
tiling control, runtime-material target, and result-document home are now correct. Four residual items can
still make the run ambiguous or lose its baseline.

**T12 — Make the framing match the executable mixed-contributor design.** The introduction still says
“which of three hypotheses,” “convicted a single cause,” and “a single follow-up recommendation,” while the
hypothesis section correctly has two live contributors that may coexist plus two regression checks. Revise
that framing to “measure the relative contribution of amplitude, tiling frequency, and lighting response;
MIXED is valid.” Likewise remove the either/or promise in Why this matters so the executor does not force a
winner that the matrix does not establish.

**T13 — Predeclare mode 82 as the H2 negative control, then isolate normal relief from geometry.** In
`PlanetVertexColor.shader`, mode 82 returns `(daylight, cloudShadow, terrainDiffuse, 1)` at lines 1073-1083,
before the `dayLight` curve edited at line 1142. Therefore the curve probe **must not change mode 82**. State
that prediction explicitly: its blue channel should remain equivalent within capture jitter; a material
change means pose/sun/material/debug state drifted and invalidates the run.

The widened curve can also make ordinary mesh/sphere curvature read stronger even if normal-map relief is
not the cause. Complete a 2×2 `Off`-view control: baseline curve at strength 2 (already the baseline),
baseline curve at strength 0, widened curve at strength 2, and widened curve at strength 0. Step 3 therefore
needs an `Off` capture at strength 0 in addition to its mode-83 write check. While the widened curve is
active, set `_BiomeNormalStrength = 0`, capture `Off`, then restore it. Only the additional relief that
depends on non-zero normal strength supports “the curve compresses normal-map relief”; a similar change at
strength 0 is macro geometric contrast, not proof of H2 as currently phrased. Each capture still changes
only one variable from the immediately preceding state.

**T14 — Archive every evidence pair before the pipeline prunes it.** `DebugCapturePipeline` retains only
`MaxCaptureRuns = 6` worth of flat PNG+sidecar files and prunes after each save. This plan takes more than
six individually labelled screenshots, so Step 1's baseline can disappear before Step 5. After each A/B
group, copy both `.png` and `.txt` files into a dated stable subdirectory such as
`local-only/debug-screenshots/baselines/2026-08-08-terrain-relief/`, and record those archived filenames in
the result table. Do this before starting the next group.

**T15 — Extend the drift check to the new tiling control's two sources of truth.** The drift block still
greps only normal strength and relief shadow. Add `_BiomeTriplanarTiling` to the shader check and verify both
its shader default and `Assets/Graphics/Materials/Planet.mat` runtime source are `0.055`. If either differs,
read back and predeclare the current baseline instead of silently forcing the stale value. This prevents a
future default/material override from turning the 0.055 → 0.0055 test into a misleading or non-restoring
probe.

---

## Claude verification of Codex third review — 2026-08-09

Verified against the tree (parallel agents + my own reads). **T12-T15 all CONFIRMED.** No design fork here
(unlike 001) — all four are fold-ready.

| Item | Verdict | Detail |
|------|---------|--------|
| **T12** framing drift | CONFIRMED (internal) | Intro + "Why this matters" still say "which of THREE hypotheses / convicted a SINGLE cause / a single follow-up," contradicting the mixed-contributor body (2 live contributors that may coexist + 2 regression checks, MIXED valid). Reframe to "measure the relative contribution of amplitude, tiling frequency, and lighting response; MIXED is valid"; drop the either/or promise. |
| **T13** negative control + strength-0 isolation | CONFIRMED | Mode 82 returns `(daylight, cloudShadow, terrainDiffuse, 1)` at `:1073-1083`, *before* the `dayLight` edit at `:1142` — so the curve probe must NOT change mode 82: predeclare it as an unchanged negative control (blue channel stable within jitter; a change = pose/sun/material drift → invalid run). And the widened curve amplifies ordinary sphere *curvature* too, so add a 2×2 `Off`-view control {baseline, widened} × {strength 2, strength 0}: only the *extra* relief that depends on non-zero `_BiomeNormalStrength` proves "the curve compresses **normal-map** relief." Strong methodological fix. |
| **T14** archive before prune | CONFIRMED | `DebugCapturePipeline.cs:31 MaxCaptureRuns=6`; prunes oldest-first by write time after each save (`:285-287`, `DebugScreenshotFiles:44-78`) into `local-only/debug-screenshots`. Nuance: kept = `6 × modesPerRun` pairs; the 6-floor bites in single-mode capture (`CurrentModeOnly`), which this diagnosis uses — so a >6-shot run *does* evict Step 1's baseline. Copy each PNG+sidecar into a dated `local-only/debug-screenshots/baselines/2026-08-09-terrain-relief/` after each A/B group; record archived names in the result table. |
| **T15** tiling drift-check | CONFIRMED | `Planet.mat:98 _BiomeTriplanarTiling 0.055` = shader default (`:9`); Planet.mat *is* the runtime clone source (`Planet.cs:245` → `PlanetTerrainMaterial.cs:49 new Material(source)`; `Configure` never touches tiling). Two real sources of truth. Add `_BiomeTriplanarTiling` to the drift grep (shader `:9` + `Planet.mat:98`); the Step-3 control reads back the runtime value and restores *that*, not a hardcoded `0.055`. |

### Body edits — APPLIED 2026-08-09

All four folded into the body above.
1. **T12 (applied):** intro + "Why this matters" reframed to relative-contribution / MIXED-valid.
2. **T13 (applied):** Step 4 predeclares mode 82 as the unchanged negative control + adds the
   widened-curve/strength-0 `Off` capture (2×2 with Step 3's strength-0 baseline).
3. **T14 (applied):** Commands now require archiving each PNG+sidecar into a dated `baselines/2026-08-09-*/`
   subdir after each A/B group (`MaxCaptureRuns=6` prune risk).
4. **T15 (applied):** drift check greps `_BiomeTriplanarTiling` from BOTH the shader default and `Planet.mat`;
   Step 3 reads back + restores the drift-checked runtime baseline instead of a literal `0.055`.

---

## Codex fourth review feedback — 2026-08-09

**Verdict: revise four experiment mechanics, then approve/run.** T12-T15 are correctly folded. The effect-
size matrix is now discriminating, but the composed `Off` image still has one uncontrolled time-varying
input and the runtime material controls are described as C# calls without an operator-access path.

**T16 — Freeze weather as well as the sun, and restore both prior states.** `time.freeze true` freezes only
celestial time. `weather.freeze` explicitly controls weather evolution independently
(`WeatherManager.cs:69-74`), including the cloud-wind angle that moves the cloud-shadow density sampled by
`CloudShadowFactor`. The `Off` path multiplies `dayColor` by `cloudShadow`
(`PlanetVertexColor.shader:1127,1144`), so an evolving cloud field can masquerade as a curve/strength delta
across the long 2×2 and shader-reimport sequence.

At Step 1, query and record both `time.freeze` and `weather.freeze`, then set the oblique sun and
`weather.freeze true` before the baseline. Keep them fixed through all cells and restore their original
values in the cleanup/STOP path. Mode 82's blue channel remains the curve-negative control; freezing
weather is required for the `Off` images and the other mode-82 channels to be comparable.

**T17 — Provide an exact, assertive path to the one runtime material.** `_terrainMaterial` is a private
field on `Planet` (`Planet.cs:51`), and `PlanetTerrainMaterial` is an internal sealed owner; no current
console command exposes `_BiomeNormalStrength` or `_BiomeTriplanarTiling`. Telling the operator to call
`_terrainMaterial.Material.SetFloat` is therefore not executable from the documented console.

Add a paste-ready Unity-MCP `execute_code` helper that locates **exactly one** live runtime material by
`shader.name == "Planet/VertexColor"` plus the runtime-clone identity, fails unless the distinct-material
count is one, and supports get/set/read-back for the two properties. Use it after generation, assert every
write, and restore the recorded values. If MCP is unavailable, stop and agree on a temporary probe command;
do not add an unreviewed permanent tuning API. Also use the existing label parameter explicitly—e.g.
`debug.screenshot "baseline-off"`—so the promised A/B labels appear in filenames rather than only in the
write-up.

**T18 — Derive the tiling extreme from the drift-checked baseline.** Step 3 now says the runtime baseline may
differ from `0.055`, but still sets the probe to literal `0.0055` and calls it 10×. That is only true for the
current value. Define `probeTiling = max(0.001, baselineTiling / 10)` (or stop if range clipping prevents the
predeclared factor), record the actual factor, label captures with the actual value, and restore the exact
read-back baseline. This keeps T15's drift tolerance from contradicting T9's effect-size control.

**T19 — Remove the leftover dotnet build from this shader-only experiment.** Commands still says
`dotnet build ProceduralPlanets.slnx` when Step 4 edits shader code, contradicting T7 and the project evidence
ladder. Dotnet does not compile HLSL and adds no evidence here. The gate is the forced Unity import, a clean
Unity shader compile, the known-hunk revert, a second forced import, and an empty targeted shader diff.

**T20 — Scope one-pose normal-map findings to the sampled biome.** The fifteen authored normal maps need not
respond equally to a global strength or tiling multiplier. Record the dominant biome(s) at the pinned pose.
Before recommending a **global** H1 strength/tiling change, repeat only the winning control pair at one
contrasting biome/texture; otherwise word the verdict as applying to the sampled spot and make cross-biome
validation the follow-up. The full matrix does not need to be repeated—one confirmation pair is sufficient.

---

## Claude verification of Codex fourth review — 2026-08-09

Verified against the tree (parallel agents + my own reads). **T16-T20 all CONFIRMED; all folded.**

| Item | Verdict | Detail / fold |
|------|---------|---------------|
| **T16** freeze weather too | CONFIRMED | `weather.freeze` (`WeatherManager.cs:69`) is independent of `time.freeze`; `_CloudWindAngle` keeps advecting cloud shadow under sun-only freeze, and `cloudShadow` multiplies into `dayColor` (`:1144`, daylight-weighted) → drifting clouds masquerade as a curve/strength delta. **Folded** into Step 1: record + set + restore both freezes. |
| **T17** material access | CONFIRMED | `_terrainMaterial` private (`Planet.cs:51`), `PlanetTerrainMaterial` internal/sealed, **zero** console commands for the props → `SetFloat` isn't operable from the console. **Folded:** an MCP `execute_code` helper (find the one `Planet/VertexColor (runtime)` clone, assert count==1, get/set/read-back/restore) + `debug.screenshot "label"` (`DebugCaptureController.cs:349` → filename). |
| **T18** probe from baseline | CONFIRMED | Step 3 read back the baseline but hardcoded `0.0055`. **Folded:** `probeTiling = max(0.001, baselineTiling/10)`, record the actual factor, label with the actual value. |
| **T19** drop `dotnet build` | CONFIRMED | Shader-only; dotnet doesn't compile HLSL. **Folded:** removed from Commands; gate = Unity import + shader compile + revert + empty diff. |
| **T20** scope to sampled biome | CONFIRMED | 15 authored normals needn't respond equally to a global multiplier. **Folded** into Step 5: record dominant biome, one cross-biome confirmation before a *global* recommendation, else scope the verdict to the spot. |

### Body edits — APPLIED 2026-08-09
T16 (Step 1), T17 (Commands helper + screenshot labels), T18 (Step 3), T19 (Commands), T20 (Step 5). 002 has no
open design question; ready to execute on approval.

---

## Codex fifth review feedback — 2026-08-09

**Verdict: T16 and T18-T20 are conceptually folded, but the runbook is not yet fully executable or
deterministic.** Three narrow corrections remain.

**T21 — T17 is not actually folded: include the promised paste-ready material helper and its preflight.**
The fourth-review acceptance criterion asked for a paste-ready `execute_code` helper; the revised Commands
section still only describes what such a helper should do. The installed MCP for Unity package marks
`execute_code` as `AutoRegister = false` in tool group `scripting_ext`
(`Library/PackageCache/com.coplaydev.unity-mcp@a4c2d0a84573/Editor/Tools/ExecuteCode.cs:16`), so the command may
not even be exposed until **Scripting Extensions** is enabled.

Put the exact MCP call and C# body in the plan. It must use
`Resources.FindObjectsOfTypeAll<Material>()`, filter `shader.name == "Planet/VertexColor"` and
`name.EndsWith(" (runtime)")`, fail unless exactly one distinct instance remains, validate `HasProperty`,
support get/set, return the instance id plus before/after value, and read back after every write. Preflight
the tool group before entering play mode; if it is unavailable, take the plan's STOP path before collecting
baselines. A prose specification still leaves every executor to invent a different control surface.

**T22 — Freeze wind-driven caster motion, not only the weather grid.** `weather.freeze true` stops
`WeatherEvolutionScheduler`, including `_CloudWindAngle`, but it does not freeze Unity `_Time`. Foliage uses
`_Time.y` in `FoliageLit.ApplyWind` and applies the same deformation in its `ShadowCaster` pass
(`FoliageLit.shader:174-189,356-396`). The terrain `Off` path samples `MainLightRealtimeShadow`
(`PlanetVertexColor.shader:1132`), so moving leaves can still alter the 2×2 cells during shader reimports.

Before the baseline, query/record `weather.wind-speed`, set it to `0`, wait one frame for the dirty global
upload, and restore it in the same cleanup/STOP path as the two freeze states. Alternatively, require an
open terrain patch with no dynamic caster shadow crossing the comparison region and verify two unchanged
baseline `Off` captures separated by the expected run duration. The zero-wind control is the more reliable
and already-exposed route.

**T23 — Make the cross-biome requirement identify and reach a biome, not just display a color.** The sidecar
contains aggregate biome-atlas diagnostics but no local dominant-biome id/name; `BiomeMapPrimaryId` renders a
categorical color without a legend. Thus Step 5's “record the dominant biome(s) … or the sidecar” is not an
executable identification path. Use the existing `scatter.count` output (`ScatterField.DescribeBiomeAt`) to
record primary/secondary/blend at the pinned pose, and `scatter.goto <BiomeType>` (or an equally explicit
existing route) to reach the contrasting biome. After moving, save the second pose and rerun
`time.set-local 0.35`, because local time is camera-relative. Then repeat only the winning pair as already
scoped. If those commands are unavailable in the target scene, record color-coded spot/pose rather than
claiming a named biome.

---

## Claude verification of Codex fifth review — 2026-08-09

Verified against the tree (parallel agents). **T21-T23 all CONFIRMED; all folded.**

| Item | Verdict | Fold |
|------|---------|------|
| **T21** paste-ready material helper | CONFIRMED | `execute_code` is `AutoRegister=false`, group `scripting_ext` ("Scripting Extensions"; `ExecuteCode.cs:16`); runtime clone uniquely named `"… (runtime)"` (`PlanetTerrainMaterial.cs:49`); `HasProperty` + `Resources.FindObjectsOfTypeAll<Material>()` are the right APIs. **Folded:** the actual `execute_code` C# body + a preflight in Commands. (Nuance: the tool likely defaults *enabled* once prefs init since it's a built-in type — preflight anyway, since exposure depends on editor pref state.) |
| **T22** freeze foliage wind too | CONFIRMED | Foliage sway is `_Time.y`-driven (`FoliageLit.ApplyWind`) and applied in its `ShadowCaster`; the terrain day path samples `MainLightRealtimeShadow` (`:1132`), so moving leaf shadows alter the day-side `Off` image even with weather frozen. `weather.wind-speed 0` zeroes the wind → `ApplyWind` early-outs. **Folded** into Step 1 (record/set/restore all three). Day-only confound (fades at night). |
| **T23** identify + reach a biome | CONFIRMED | `scatter.count` prints `DescribeBiomeAt` (`ScatterField.cs:414,398-409`) = named primary/secondary/blend; `scatter.goto <BiomeType>` (`:575`) teleports. `BiomeMapPrimaryId` / the sidecar do NOT name the biome. **Folded** into Step 5 (my "BiomeMapPrimaryId or sidecar" path replaced). |

### Body edits — APPLIED 2026-08-09
T21 (Commands paste-ready helper + preflight), T22 (Step 1 wind-speed 0), T23 (Step 5 scatter.count/goto). 002
has no open design question; ready to execute on approval.
