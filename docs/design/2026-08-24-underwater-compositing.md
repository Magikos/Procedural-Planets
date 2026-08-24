# Underwater compositing — restructure

Status: proposed, not started. Written 2026-08-24 as a handoff so this can be picked up cold.

## The problem

Underwater, the final image is assembled by a **sequence of overrides**, each added to fix the symptom the
previous one caused. The result is that every change in this area breaks something adjacent, and three of the
last five fixes needed a follow-up fix.

The whole of it lives in one branch in `Assets/Graphics/Shaders/Atmosphere.shader`, entered when the camera is
submerged and the pixel is classified as sky:

```
if (_WaterVolumeEnabled > 0.5 && CameraUnderwater01() > 0.5 && SkyDepthMask(i.uv) > 0.5)
{
    result = UnderwaterSkyColor(viewDir);            // 1. a flat ambient water colour
    if (window > 0.001) result = lerp(result, sky * throughWater, window);   // 2. Snell's window over it
    surface = WaterInterfaceFrontMask(uv) * (1 - window * 0.85);             // 3. ...unless water covered it
    return lerp(result, originalCol.rgb, surface);   // 4. ...in which case use what the water pass drew
}
```

Each step overrides the last. Nothing in it asks what the pixel actually *is*.

## Evidence that this is the real problem

Captured 12 m below the surface, looking up at ~55 degrees, same frame:

| view | result |
| --- | --- |
| `debug.mode SurfaceOnly` (atmosphere bypassed) | underside is dark navy with faint wave streaks — **correct** |
| beauty | bright teal-green flood, surface detail almost entirely washed out |

So the water surface renders correctly and the atmosphere pass floods over it. What Bryan described as "the
underside looks like it is glowing" is the pale surface streaks showing through that wash wherever the
coverage mask happens to let them.

Reproduce: `camera.teleport LastDebugCapture` from the 2026-08-24 14:57 F10, or the position in
`.agent-memory/claude/project_water_shore_rendering.md`. Captures kept as `glow_0.png` / `glow_25.png`.

## Proposed shape

Compose ONE underwater result from the three things that physically contribute, instead of overriding:

- **Through the surface** — Snell's window. The sky, refracted, attenuated by the water above the camera.
  Confined to the cone of half-angle `asin(1/1.333)`.
- **Reflected off the underside** — total internal reflection outside that cone. Currently missing entirely,
  and its absence is why the window reads as a soft glow rather than a defined disc.
- **The water column itself** — in-scattered light along the view ray. This is what `UnderwaterSkyColor`
  approximates, and it should be *added to* the other two, not painted over them.

Weight them by Fresnel at the surface rather than by coverage masks, and let the water pass's own surface
render supply the first two wherever it drew something, since it already has the ripples and the glint.

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

The last two are the ones the restructure will most likely subsume.

## Tooling that this needs

- Shader reimport takes about **75 s** to reach the running frame, and up to ~110 s is safer. Zero compile
  errors does NOT mean the new variant is live. Capture a reference pixel first and assert it CHANGED.
- The play-mode camera **drifts**. Re-assert position and rotation in the same call that renders, and print a
  witness value such as sea offset next to the result.
- Full regeneration after a C# change is ~12–15 minutes. Batch the checks you want per run.
- `debug.mode` names bind only after a domain reload, which play mode blocks. Stop and restart play once.
- Debug modes that matter here: `SurfaceOnly`, `AtmosphereBypass`, `ShapeIsPrepassDepth`,
  `ShapeIsCompositeDepth`, `ShapeIsWaterMask`.

## Method

`.agent-memory/claude/feedback_identify_before_fixing.md` is the rule this area needs most: colour the
artifact itself, agree the target, and escalate to that after ONE failed fix rather than five. This file
exists because that lesson was learned expensively on exactly this code.
