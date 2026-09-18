# Moon appearance and phases — 2026-09-07

## Active Tracker

Status: Implemented and validated in Unity on 2026-09-07. Visual review remains open.

The moon uses NASA color and elevation maps, a dedicated phase shader, and a shared phase/orbit model. It remains a visual sphere without a collider.

- [x] Archive the existing moon and resolve its material.
- [x] Implement phase, orbit, appearance settings, and console controls.
- [x] Run lunar and console tests, builds, and eight-phase captures.
- [ ] Complete the extended weather, horizon, regeneration, and water comparison matrix.
- [ ] Obtain Bryan's visual review.

## Implementation and controls

Edit `Assets/Resources/Settings/MoonSettings.asset` for saved defaults. Runtime commands change the active snapshot without changing the asset.

| Command | Purpose |
|---|---|
| `time.moon.phase 4` | Select full moon. Values 0–7 select eight phases. |
| `time.moon.progress 0.25` | Select continuous progress; values wrap. |
| `time.moon.hold true` | Hold phase while daily sky motion continues. |
| `time.freeze false` | Resume time after the frozen review setup. |
| `time.moon.cycle 8` | Set game days per lunar cycle. |
| `time.moon.distance 3` | Set logical orbit distance in planet radii. |
| `time.moon.diameter 9.03` | Set angular diameter at the planet center. |
| `time.moon.inclination 5` | Set inclination relative to the sun's daily frame. |
| `time.moon.node 0` | Rotate the inclination node. |
| `time.moon.brightness 1` | Set surface brightness. |
| `time.moon.detail 1` | Set crater normal strength. |
| `time.moon.earthshine 0.01` | Set faint dark-side illumination. |
| `time.moon.tint 1 1 1` | Set surface tint. |
| `time.moon.status` | Read the current state. |

The old `time.moon-phase` command remains an alias. Commands use the shared catalog and development-only release policy. Expected rejection returns `ConsoleCommandResult.Fail`.

The material uses the 2019 NASA 4096 × 2048 color map and 1440 × 720 elevation map. Unity converts elevation to normals with heightmap scale 0.01. Source URLs, hashes, and credit are in `Assets/Resources/Moon/SOURCE.md`. No generated texture was needed.

The original material resolves to URP's package `Lit.mat`; it was not missing. The custom shader prevents ambient lighting from filling the unlit side. Terrain depth replaces whole-disc center culling. A camera-relative visual projection keeps distant orbits within the main camera's far limit. The logical moon position remains unchanged.

`CelestialCommands` now owns the console adapter. Local-time commands reuse `TrySetLocalTimeOfDay`, removing the duplicate calculation. Moonlight consumers retain their existing global values and conventions.

## Completed validation

Evidence root: `local-only/moon-validation/2026-09-07/`.

| Check | Result and evidence |
|---|---|
| EditMode lunar and console regressions | 55 passed, 0 failed, 0 skipped; `editmode-tests-final.json`. |
| Core and Planet builds | Both passed with zero errors. Existing analyzer and unrelated source warnings remain; `core-build.txt`, `planet-build-final.txt`. |
| Shader compilation | No messages from `ShaderUtil.GetShaderMessages` for `Planet/Moon`. |
| Runtime input rejection | Twelve invalid commands rejected through `CommandExecutor`; `command-rejections.json`. |
| Eight phases | Synchronous main-camera captures `phases/phase-0-1.png` through `phase-7-1.png`; matching `phase-0.json` through `phase-7.json`. |
| Orbit distance 100 | Logical distance 529344.3; projected visual center 83373.32 from camera; far limit 100000. Moon remains visible; `far-projection.json` and `after/moon-distance-100.png`. |
| Baseline | Original capture and scene snapshot under `before/`. |
| Knowledge graph | `graphify update .` completed after code changes. |

The eight-phase series uses one camera pose and zero inclination. Time and phase advance together to keep the moon in the same view. Fullness is 0, 0.1464, 0.5, 0.8536, 1, 0.8536, 0.5, and 0.1464. Earlier unsuffixed screenshots used asynchronous capture; use the suffixed series for review.

The final review setup holds full moon and freezes time. Saved defaults retain a five-degree inclination. Resume with `time.freeze false`; release phase hold with `time.moon.hold false`.

Extended cloud, horizon, opposite-hemisphere, regeneration, and water comparisons remain unverified. The automated checks cover phase invariance, cycle advancement, wrapping, angular size, projection, settings validation, and command rejection. This implementation does not simulate eclipse lighting or a visitable moon.

The following sections preserve the original discovery and proposed validation matrix. Their future-tense wording records the initial plan.

---

## Goal and scope

Give the moon recognizable craters, dark surface regions, and a clear transition between its light and dark sides. Support continuous phases and predictable movement across the sky.

Treat the moon as a distant visual prop. Reuse the sphere mesh, but remove its collider and unnecessary shadow casting. Do not build terrain, landing support, gravity, or a separate world.

Assumption: the first version uses a natural rocky appearance, adjusted to the game's art style. A fictional surface remains a later art choice.

Discovery describes the dirty working tree on `harvest-vertical-slice`, based on `d1e0f62`. The project currently uses Unity `6000.7.0a5`. Other agents own active scene, wildlife, and weather edits.

## Current behavior

Source: `Assets/Scripts/Planet/CelestialManager.cs` and `Assets/Scenes/Planet.unity`.

| Finding | Evidence | Consequence |
|---|---|---|
| The scene contains a sphere under `Moon/Visual`. | Mesh file ID `10207`; scale `2500`; enabled sphere collider. | Existing geometry is sufficient for a distant textured moon. |
| Generation overwrites the orbit radius. | `Initialize()` assigns three times the planet radius. | Inspector distance changes do not survive regeneration. |
| Sun and moon use different base planes. | `UpdateSun()` uses XY; `UpdateMoon()` uses XZ. | `MoonInclination` does not describe a small tilt relative to the sun's plane. |
| The phase command sets an orbit bucket. | `MoonPhaseCmd()` sets `(i + 0.5) / 8`. | A requested phase index does not guarantee the corresponding illuminated shape. |
| Daily time changes actual fullness. | `MoonPhase` comes from the sun/moon dot product; the index uses orbit progress. | The phase label and visible lighting can disagree. |
| Ambient and water lighting already use the moon. | `UpdateAmbient()` and `UpdateMoonShaderGlobals()`. | The new model must preserve these connections. |
| Visibility tests the moon's center. | `UpdateMoonVisibility()` hides all moon renderers together. | A large disc can disappear abruptly at the horizon. |

The scene references material GUID `31321ba15b8f8eb4c954353edc038b1d`. A local `Assets/**/*.meta` search found no matching asset. Unity must confirm whether this reference is unresolved or resolves elsewhere.

Existing duplication: `SetLocalCmd()` repeats the camera-to-local-time calculation in `TrySetLocalTimeOfDay()`. This is outside the moon change. `UpdateAmbient()` also repeats the sun/moon alignment calculation from `UpdateMoon()`; the moon change should share that result.

## Asset decision

The existing catalog points to `D:/Unity/Explore Assets/Assets`. Discovery searched current filenames and visually inspected the following candidates.

| Candidate | Observed content | Decision |
|---|---|---|
| UniStorm `Textures/Celestial/Moon Phases/Type 1` and `Type 2` | Two sets of eight small disc images. Inspected full-phase images are 267 × 267 pixels. Lighting is baked into the images. | Useful references. Do not switch these images to implement continuous phases. |
| Synty `PolygonElvenRealm/Textures/Misc/Moon_01.png` | 2048 × 2048 stylized moon artwork with packed regions and visible baked shading. | Art reference; not a direct spherical base-color map. |
| Synty `PolygonElvenRealm/Prefabs/FX/FX_Compnents/Textures/Moon_Texture_01.png` | 2048 × 2048 dark disc with a bright baked rim. | Poor input for changing illumination. |
| Toon Enchanted Meadow `Particles/Textures/TEM_Fake_Moon_01A.png` | 512 × 512 blue disc with soft surface detail. | Possible fantasy reference; limited crater detail. |
| NASA CGI Moon Kit | Global color and elevation maps designed for spherical rendering. | Recommended source for the first implementation. |

NASA provides a newer 2025 color map and the earlier 2019 map. Start with a 2K color map; increase resolution only if captures show insufficient detail. Use elevation data for surface normals, not terrain generation. Record source URLs, conversion settings, checksums, and the requested credit: NASA's Scientific Visualization Studio. Source checked on 2026-09-07: [CGI Moon Kit](https://svs.gsfc.nasa.gov/4720/).

Do not generate a texture yet. The first problem is controllable lighting and movement. Generated art becomes useful if Bryan wants a distinct fictional crater pattern after reviewing the first version.

## Proposed phase and orbit model

Keep `CelestialManager` as the owner. Extend its existing service contract instead of adding another time manager.

Use continuous lunar progress as the canonical phase input:

| Progress | Named phase | Ideal illuminated fraction at zero inclination |
|---|---|---|
| 0.00 | New | 0 |
| 0.25 | First quarter | 0.5 |
| 0.50 | Full | 1 |
| 0.75 | Last quarter | 0.5 |

Derive moon direction from the sun's daily frame, lunar progress, and orbital inclination. The configured cycle duration means days from one new moon to the next. Daily rotation moves both bodies across the sky; lunar progress changes their relative angle.

Derive labels, fullness, events, and shader values from the same evaluated state. Preserve the existing `MoonPhase` convention: -1 means full; +1 means new. Preserve `time.moon-phase` as an eight-phase shortcut, but select exact named phases instead of bucket midpoints.

Inclined orbits need a documented reference plane and node direction. At nonzero inclination, named new/full phases can be near alignment rather than perfect alignment. Verify exact endpoint math with inclination set to zero.

Use the actual observer direction for material lighting. Keep gameplay phase global so players at different locations share the same lunar cycle. A close artistic orbit can cause visible parallax and small differences in observed fullness.

Provide these authoring controls and runtime commands through the existing settings and console patterns:

| Control | Meaning |
|---|---|
| Continuous phase | Set progress from 0 to 1. |
| Cycle duration | Set game days per complete phase cycle. |
| Phase hold | Hold lunar progress while daily sky movement continues. |
| Inclination and node angle | Set the orbit plane relative to the daily celestial frame. |
| Orbit distance | Set distance in planet radii; preserve it through regeneration. |
| Apparent diameter | Set visual diameter in degrees at the planet center; derive mesh scale from orbit distance. |
| Surface appearance | Set tint, brightness, detail strength, and faint dark-side visibility. |

Keep physical phase and position coupled by default. Setting full moon should move it opposite the sun. Arbitrary sky placement with an unrelated phase would require deliberately artificial lighting; defer that mode unless requested.

Reject nonfinite values and invalid durations or distances. Ensure the visual sphere remains outside the planet. Make freeze, local-time changes, and regeneration recompute a valid state without dividing by zero. New settings should use immutable runtime snapshots and existing world registration.

## Proposed rendering

First reuse the sphere with a dedicated moon material. Use the existing directional sun vector and a matte surface response. Add restrained normal detail so craters affect the light boundary. Keep the broad silhouette round.

Check the existing URP material path before writing a custom shader. A custom shader is justified if ambient lighting fills the dark side or existing shadow controls break the intended phases. Do not bake the phase into the color texture.

The material must preserve a dark disc that occludes stars. Clouds and atmospheric scattering must remain in front of the moon. The current atmosphere pass scatters the existing scene color, which makes the sphere a useful first integration point.

Disable unnecessary terrain-shadow reception and shadow casting for the visual prop. This does not implement lunar or solar eclipses. Eclipse simulation is outside this first version.

Replace whole-disc horizon popping only after the baseline identifies the active depth behavior. Prefer existing terrain depth occlusion over a second horizon system. Any analytic fallback must consider the disc radius.

Likely implementation locations are `CelestialManager.cs`, `ICelestialTimeController.cs`, adjacent celestial settings, a moon material, and a dedicated texture folder with `SOURCE.md`. Add a shader only if the existing material path fails the phase checks. Update the moon scene references after the other agent releases the scene.

## Queued validation

This is a written queue, not a scheduled Unity job. No tests, builds, imports, captures, or Editor commands ran during discovery.

| Order | Check | Required result |
|---|---|---|
| M0 | Capture the current moon before implementation. Record seed, camera, quality, local time, phase, material, and sun vector. | Archived PNG and sidecar under `local-only/moon-validation/2026-09-07/before/`. |
| M1 | Check phase math at 0, 0.25, 0.5, and 0.75 with zero inclination. | Fullness matches 0, 0.5, 1, and 0.5 within 0.0001. Quarter phases have opposite illuminated sides. |
| M2 | Hold phase and advance a full day. Then advance one configured lunar cycle. | Daily movement preserves held phase. One cycle returns to the starting lunar state. |
| M3 | Check negative/wrapped phase input, invalid numbers, freeze, time jumps, and regeneration. | No invalid vectors, divisions by zero, lost controls, or stale shader values. |
| M4 | Build Core, then Planet, serially. Import and start a fresh Unity run. | No new compile errors or runtime exceptions. |
| M5 | Capture all eight phases with fixed exposure and repeatable viewpoints. | Clear craters; smooth phase changes; no texture seam or glowing dark side. |
| M6 | Capture rising/setting moon, daytime moon, clouds, opposite hemispheres, and orbit view. | No abrupt whole-disc popping, foreground overlap, star leakage, or camera-dependent phase reversal. |
| M7 | Compare new/full moon water and atmosphere captures. | Existing moonlight consumers remain coherent; full moon is brighter than new moon. |
| M8 | Review paired captures with Bryan. | Bryan confirms appearance and chooses final size and brightness. |

Run M1–M3 through the existing EditMode test assembly when implementation exists. No new test framework is needed. Run `graphify update .` after code changes. Preserve every unrelated change in the shared tree.

