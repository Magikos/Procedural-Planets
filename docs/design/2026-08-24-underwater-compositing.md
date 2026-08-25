# Underwater compositing — restructure

Status: **landed, uncommitted**. Written 2026-08-24 as a proposal, executed the same day, extended
2026-08-25 with W19 and the interface terms.
Files: `Assets/Graphics/Shaders/Atmosphere.shader`, `Assets/Graphics/Shaders/WaterVolume.shader`,
`Assets/Graphics/Shaders/Includes/WaterVolumeData.hlsl`, `.../WaterLevelField.hlsl`,
`.../WaterDisplacement.hlsl`, `.../Atmosphere.hlsl`.

This closes **W19** from [the water architecture plan](2026-08-17-water-architecture-plan.md) as a
side effect. W6–W18 and W20 are untouched.

## The problem

Underwater, the final image was assembled by a **sequence of overrides**, each added to fix the symptom the
previous one caused. Every change in this area broke something adjacent, and three of the last five fixes
needed a follow-up fix.

## Correction to the original diagnosis

The proposal named one owner — the underwater branch in `Atmosphere.shader` — on the strength of this
comparison, captured 12 m down looking up at ~55 degrees:

| view | result |
| --- | --- |
| `debug.mode SurfaceOnly` (atmosphere bypassed) | underside dark navy with faint wave streaks — correct |
| beauty | bright teal-green flood, surface detail almost entirely washed out |

**That comparison does not isolate the atmosphere.** `SurfaceOnly` returns `source` at
`WaterVolume.shader:690` as well, so it disables the volume composite too. The flood was in the pass the
comparison had also switched off:

```
// WaterVolume.shader, BeforeRenderingTransparents
if (SceneDepthValid(rawDepth) <= 0.0)
{
    if (CameraUnderwater01() > 0.01 && (IsProductionEquivalentDebugMode(...) || volumeBodyDebug))
        return float4(UnderwaterNoDepthColor(rayDir), 1.0);   // <- the teal flood
```

The water surface writes no depth, so every pixel showing it classified as "no geometry". This pass runs
**before** transparents, so it painted the flat column colour over the sky, and `Ocean.shader` then blended
the underside onto that at roughly a third of an alpha. The atmosphere's own override chain then ran on the
result. There were **three** column colours in the frame, not one.

Two further things had to be true for the flood to survive five rounds of debugging:

- It was gated on `IsProductionEquivalentDebugMode`, so **no debug mode could show it**. Every isolation
  view switched the very thing off that was being looked for.
- Its submersion threshold was 0.01, while the atmosphere's branch starts at 0.5. Between waves the wash
  appeared with nothing composited over it at all.

## What landed

One composite of the three terms that physically reach the eye, in `Atmosphere.shader`, which runs last:

```
transmit  = exp(-WATER_ABSORPTION * path / 40)          // Beer-Lambert to the surface
fresnel   = unpolarised water -> air, exactly 1 past the critical angle
window    = (1 - fresnel) * submersion ease
interface = lerp(mirrorColor, skyColor, window)         // TIR outside the cone, sky inside it
result    = interface * transmit + columnColor * (1 - transmit)
```

- **Snell's window** is now the Fresnel term, not a hand-placed `smoothstep` around `cos(48.75°)`. The rim
  and the hard outer edge fall out of the physics, which is why it reads as a defined disc rather than a
  soft glow.
- **Total internal reflection** is new. Outside the cone the underside mirrors the water below the camera.
- **The water column** is added under the other two rather than painted over them, weighted by its own
  opacity, so a ray that never leaves the water collapses to it with no special case.
- The `saturate(path / 40)` cap is gone. It floored blue transmission at 0.56 at any depth, so the surface
  read as flooded from 100 m down as readily as from 3 m.
- `WATER_ABSORPTION`, `WATER_ABSORPTION_UNIT_METRES` and `WATER_IOR` moved to `WaterVolumeData.hlsl`, the
  file that already exists to stop these three shaders drifting apart.
- `WaterVolume.shader`'s flood is now debug-only. Production underwater sky pixels belong to the atmosphere.

Then, the same pass extended (2026-08-25):

- **The interface refracts and reflects about the SURFACE's normal, not the planet's.** The atmosphere pass
  now includes `WaterDisplacement.hlsl` and evaluates `ComputeOceanSwell` at the point where the ray leaves
  the water — the same include the water mesh and the volume prepass displace with, so the rim tracks the
  waves the player can see rather than a second wave field. Snell's window heaves with the swell instead of
  sitting as a fixed circle. Ice locks it flat, exactly as `ComputeWaterVertexDisplacement` does.
- **TIR reflects about that normal too.** Reflecting about the planet normal mapped every direction outside
  the window to roughly the same near-horizontal ray, which is why it came back one flat colour.
- **The water column now scatters forward toward the sun**, and it is a marched integral rather than a
  hand-set tint. `UnderwaterSunShafts` steps ten times along the view ray, and at each step asks where that
  step's *sun* ray crossed the surface, so the light is attenuated by the real path in and the real path
  out. Against the sun's **refracted** direction — entering the water bends it toward vertical, so from
  below the sun sits higher than it does from the beach.

  The first version of this was an ambient tint added inside `UnderwaterSkyColor`. That was a second copy
  of "brighter toward the sun" living next to the march — the duplicated-override shape this file exists to
  remove — so it is gone and the march owns the whole directional term. `SHAFT_SCATTER` was then set by
  measuring what the tint had produced, not by taste: `(0.0098, 0.038, 0.036)` at 8 m down looking 70° off
  vertical toward the sun.

  Measured after, at 43° sun elevation, 8 m down, 70° off vertical: toward the sun `(0.314, 0.604, 0.565)`
  against `(0.271, 0.553, 0.514)` away. At 40 m the same view reads `(0.231, 0.588, 0.682)` — much bluer,
  which is red going first, and the ambient copy could not do that at all.

  It is added over geometry as well as over sky, so the shafts cross the seabed rather than stopping at
  the waterline.
- **W19 — the drifted `CameraUnderwater01` pair is now one function**, `CameraSubmerged01` in
  `WaterLevelField.hlsl`. Each copy had one half right: `WaterVolume` measured against the level field
  (correct for a lake perched above sea level) but faded across a fixed 1.5/2.0 m band; `Atmosphere` faded
  across the swell (correct) but measured against the global sea sphere, so it read +95 m while the camera
  floated in a raised lake and never treated that view as underwater at all.
- `_PlanetCenter` is declared behind `PLANET_CENTER_DECLARED` in both `Atmosphere.hlsl` and
  `WaterDisplacement.hlsl`, so a shader may now include both.
- **The fragment wave field is hoisted into `WaterDisplacement.hlsl`** as `ComputeWaterRipple`, beside the
  vertex swell — the domain warp, four long waves and three short ones, returning a `WaterRippleField`.
  `Ocean.shader` calls it and keeps its own breakup, cell pattern and resolve, which depend on Ocean-local
  noise. 53 lines out of `Ocean.shader`, **bit-identical output at all seven check viewpoints**, four of
  which show water surface prominently.

  `EvaluateRippleParameters` went with it — the size and energy derivation that turns the water-data
  channels and the wind into that field's inputs. A consumer deriving those on its own would band its waves
  off a different size and never look wrong enough to notice.

- **`_WaveAmplitude` and `_WaveScale` promoted to globals.** They were material properties on the Ocean
  material, which is what stopped any other pass evaluating the same waves. `WaterDto` already carried both
  values; `PlanetWaterSurface` now publishes them with `Shader.SetGlobalFloat`, and they are **deleted from
  `Ocean.shader`'s Properties block and its local decls** — a same-named material property shadows the
  global wherever that material is bound, which is exactly how the wave parameters went wrong the last time
  a promotion was done half way.

- **Snell's window has fine chop, and the shafts band off the short waves.** Both now evaluate
  `ComputeWaterRipple` — the window at the exit point, the shafts at each step's surface entry point.
  Geometric slope only: `Ocean.shader` multiplies the same gradient by `_WaveNormalStrength` to exaggerate
  its shading, and carrying that here would refract light through a surface steeper than the one the mesh
  and the depth buffer agree on.

- **Underwater at night was too bright** (Bryan, 2026-08-25). `UnderwaterSkyColor` lerped toward a "lit"
  colour whose night end was `(0.012, 0.105, 0.165)` — brighter than the authored deep colour in both green
  and blue — so midnight underwater came out a mid-blue however dark the world above it was. Colour and
  light level are now separate, and the night floor is `_NightAmbientIntensity`, the same one
  `Ocean.shader` uses for the surface, so both sides of the waterline move together under `light.*`.
  Daylight is unchanged by construction: at `daylight = 1` the new expression reduces to the old one.

## Three defects found while landing it, all worth remembering

**The prepass depth channel is not usable underwater.** The first version measured the path to the surface
from `_WaterVolumeData.r`. Underwater that pass clips nothing by design, draws `Cull Off`, and writes no
depth, so in patches it records the distance to the ocean **past the horizon** — the same defect `1fbcedf`
fixed for the view from above. The upward view came out as two flat colours split along a triangle edge. The
path is now measured against `WaterLevelField.hlsl`, which is continuous and knows about raised lakes.

**`CalculateScattering` returns the background for any ray that starts inside `_SeaLevelRadius`**
(`Atmosphere.hlsl:100-104` — `hitPlanet.x` is 0 from inside, so `maxDst` goes non-positive). The exit point
sits exactly on the water surface, which for the ocean *is* that radius. The window returned black
everywhere except near the frame edges, where longer rays happened to clear the sphere. The exit point is
now lifted clear of the sphere before integrating.

**`Ocean.shader`'s underside colour is not interface radiance.** Making the surface opaque underwater so the
atmosphere could use its ripples and glint looked obviously right and was wrong: that colour is the water
BODY seen from *above* — body tint, sky reflection, above-water lighting — and compositing it here painted
an above-water sheet over the window, split along the water mesh's own triangle edges. Reverted;
`Ocean.shader` is untouched by this work.

## Verified

Play mode **paused** so `_Time` is frozen — two renders of the same shader are then bit-identical, which is
what makes an A/B meaningful. Without pausing, waves and clouds move between calls and a null change reads
as ~7/255 mean difference.

| check | result |
| --- | --- |
| Snell's window, 3 m down, straight up, fov 140 | defined disc, sun inside it, water outside — `v4_wide_d3.png` |
| depth response, 30 m down, same view | disc faded to a soft glow, correct |
| no flood, 12 m down, tilts 0/45/57/75 | angular structure throughout; the flat teal is gone |
| swell deforms the rim, 3 m down, fov 140 | disc reads as an offset oval, not a circle — `v5_wide_d3.png` |
| low sun (32°), 8 m down, fov 140 | disc offset toward the sun, sun compressed against the rim, water brighter on the sun side — `v6_lowsun_wide.png` |
| sun shafts, 8 m down, 70° off vertical | toward sun `(0.314, 0.604, 0.565)` vs away `(0.271, 0.553, 0.514)` |
| shaft depth response, 40 m down, same view | `(0.231, 0.588, 0.682)` — red gone first, correct |
| shafts over geometry, seabed at 10 m | shafts and caustics both present — `v9_seabed.png` |
| ripple hoist is behaviour-preserving | **0 of 518400 pixels differ** across all seven, four of them showing water surface |
| `SeeThroughWater`, `ShoreStudy`, `OceanShoreStudy`, `Lake1`, `ChopTest`, `ShoreWorkReturn`, `OddGrass` | **0 of 518400 pixels differ** from HEAD, all seven |
| between waves, `LastDebugCapture` at −1.44 m | sky, horizon, no underwater treatment — `121018d` holds |

Captures in `%TEMP%\uw\`.

**Beware a false positive in the A/B.** `Camera.Render()` advances `_Time.y` by about 0.01 s even with play
mode paused, and across two tool calls that is enough to move a glint highlight and report tens of differing
pixels for a null change. Both sides must be captured at the *same* `_Time.y` — print it and check.

## Not done

- **TIR reflects the column, not the seabed.** A real underside also mirrors the bottom where it is close
  enough to see. That needs the underwater scene sampled about the reflected ray — and that ray points down
  and behind the camera, so it is mostly off screen and screen-space reflection will not supply it.
- **The night floor is flat, not moonlight.** `_MoonParams` and `_MoonIntensity` exist and the volume pass
  already reads them; a full moon overhead should light the column far more than a new moon, and the floor
  cannot tell them apart. Marked `ponytail:` at the site.
- **Cost is unmeasured.** `ComputeWaterRipple` is about eleven trig calls, and the shaft march now runs one
  per step — roughly 110 per underwater pixel on top of the interface's own. Underwater only, and never
  profiled. If it bites, the step count is the first dial.
- `SHAFT_SCATTER` is derived from a measurement of what it replaced, which makes it self-consistent but not
  necessarily *right*. Bryan has not had his eye on it.
- `CausticPattern` cannot be reused per march step for the banding — it is 81 animated Voronoi cells per
  call, so ten steps would be ~970 per pixel. The swell/ripple gradient is what bands the shafts instead.

Observed, not this work's:

- Distant scatter impostors over the far seabed read as dark angled specks (`v9_seabed.png`, upper half).
  Same family as the black-dot impostor issue in
  `.agent-memory/claude/project_scatter_dusk_lighting.md`. Not investigated.

## Do not regress

All verified, with the viewpoint each was checked at:

| commit | what | check at |
| --- | --- | --- |
| `1fbcedf` | prepass must not record the far ocean's distance | `LastDebugCapture` 10:30, `ShapeIsPrepassDepth` |
| `2704811` | far side of the ocean showing through the near side | `SeeThroughWater` |
| `c1966ef` | carried level meshing water; foam overhang saturating | `ShoreStudy` |
| `2634101` | cover-set mesh so the per-pixel shoreline has geometry | Bryan's bay F10 |
| `bc198ac` | Snell's window renders | 5 m down, straight up, fov 90 |
| `121018d` | not "submerged" while between waves | `LastDebugCapture` 14:57 |
| `847e867` | no air scattering on submerged pixels | — |
| `6d2e3d0` | atmosphere not erasing the surface underside | — |

The last two are subsumed: the branch no longer composites `originalCol` at all, and air scattering still
cannot reach a submerged pixel because the branch returns before it.

## Tooling

- **Pause play mode before any A/B.** `EditorApplication.isPaused = true` freezes `_Time`; without it the
  waves move between tool calls and swamp the comparison.
- **`time.freeze true` freezes the sun only, and `time.set-local` does not apply until the next frame** — a
  capture taken in the same synchronous call still uses the old sun. Read `_SunParams` back and check it.
- `AssetDatabase.ImportAsset(..., ForceSynchronousImport)` followed by `Camera.Render()` gets a new shader
  variant on screen in seconds, not the ~75-110 s a play-loop restart costs. `ShaderUtil.GetShaderMessages`
  reports compile errors without leaving C#.
- Full regeneration after a C# change is ~12–15 minutes. Batch the checks you want per run.
- `debug.mode` names bind only after a domain reload, which play mode blocks. Stop and restart play once.
- Useful here: `SurfaceOnly`, `AtmosphereBypass`, `SurfaceAlpha`, `ShapeIsPrepassDepth`,
  `ShapeIsCompositeDepth`, `ShapeIsWaterMask`. **None of them could see the production flood** — a temporary
  probe gated on `_SceneDepthDebugRange` (a float settable from C# with no enum change) was what found it.

## Method

`.agent-memory/claude/feedback_identify_before_fixing.md` is the rule this area needs most. The corollary it
adds today: **a debug mode that switches off more than one pass cannot attribute an artifact to one of
them.** Check what each isolation view actually disables before drawing a conclusion from it.
