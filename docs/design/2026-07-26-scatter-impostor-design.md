# Scatter far-field impostors — design (for approval)

Branch `scatter-placement`. Expands F4 slice-2 from the
[07-25 audit](../audit/2026-07-25-scatter-audit.md#f4--empty-far-field-150-m-gather-cap-no-impostors).
This is a **design for Bryan to approve before implementation** — it touches core files
(`ScatterField` gather radius, `ScatterRenderer`, the DTOs, prototype authoring) and has real
aesthetic calls, so it was not built blind.

## Problem

Today each prototype draws full-detail mesh LODs out to its cull distance (trees ~400 m) then hard-
culls. Beyond that the world is bare. Trees are also full-detail the whole way, which won't scale
once instance counts and view distance grow. A far tier also unlocks the "chop a forest, see the gap
from a distant hill" gameplay goal.

## Options

| Option | Quality | Cost to build | Runtime cost | Notes |
|---|---|---|---|---|
| **A. Mesh decimation** (synthesize LOD1/2) | Med | High | Med | Decimation of alpha-cutout card canopies is ugly; the source FBXs are single-mesh so we'd generate LODs ourselves. Keeps true 3D. |
| **B. Single cylindrical billboard** | Med (great at distance from ground) | Low | Very low | One baked side-view card per tree, billboarded around the up axis in the vertex shader. Flat when viewed from directly above/orbit — fine for a ground/near-ground camera. **Recommended first tier.** |
| **C. Octahedral impostor** | High | High | Low | Multi-angle atlas, blends nearest captured views; the original target. Heavy bake + shader, visually finicky (halos, blend seams). |

**Recommendation:** ship **B** as the far tier now (cheap, big visual win, low risk), keep **C** on
the roadmap for when orbit/aerial views matter. B and C share the same integration seams below, so B
is not throwaway.

## Integration sketch (Option B)

1. **Bake (editor, offline).** For each tree prototype, render LOD0 (all parts, correct materials)
   to an RGBA card via an orthographic side capture with alpha; trim + store as a texture asset under
   `Assets/Art/Impostors/`. Deterministic, re-runnable; a menu item `Planet/Bake Scatter Impostors`.
   One shared unit-quad mesh; one `ImpostorLit` shader (unlit-ish, alpha-cut, cylindrical billboard
   in the vertex stage, distance dither) + one material per prototype pointing at its card.
2. **DTO/authoring.** Add optional `Impostor { Material; float StartDistance; float EndDistance; }`
   to `ScatterPrototypeDto` (authored on `ScatterPrototype`). Absent ⇒ prototype behaves as today
   (hard cull at mesh end). Only coarse-spacing tree prototypes get one.
3. **Gather radius.** Extend a prototype's gather radius to `Impostor.EndDistance` when present.
   Guard the `CandidateBudget`: impostors are trees only (coarse spacing), so the extra far ring adds
   bounded candidates — bushes/grass/rocks keep their current short radius. Verify with
   `scatter.count` at the new radius.
4. **Renderer.** After the mesh-LOD passes, draw instances in
   `[meshEnd - crossfadeBand, Impostor.EndDistance]` as billboard quads
   (`RenderMeshInstanced`, per-instance matrix = position + uniform scale; billboard rotation in the
   vertex shader). **Cross-fade:** over `[meshEnd - band, meshEnd]` dither-out the mesh and dither-in
   the impostor (complementary Bayer clip) so there is no pop — this also retires
   [07-25 F5](../audit/2026-07-25-scatter-audit.md#f5--far-horizon-dither-stipple-at-the-cull-ring)
   (the stipple becomes a mesh↔impostor transition instead of a fade-to-nothing).
5. **Shadows.** Impostor quads cast a cheap shadow (alpha-clip shadowcaster) or none past the shadow
   distance (N5 in the 07-26 audit) — TBD, cheap either way.

## Perf

Draw the far ring as one instanced quad batch per prototype — far cheaper than the full canopy mesh.
Pairs with audit **N1** (bucket instances by prototype once per swap) so the far ring doesn't
re-scan the whole list. Net: far view distance goes *up* while per-frame cost goes *down* vs full
meshes at 400 m.

## Risks / open decisions (Bryan)

- **Single vs octahedral (B vs C).** B is flat from above — acceptable for a ground camera? (If the
  3rd-person fly camera often looks down at forests from altitude, C matters sooner.)
- **Distances:** mesh→impostor crossover (~150–250 m?) and impostor end (~1–2 km?).
- **Atlas resolution** per card (256²? 512²?) — memory vs crispness.
- **Which prototypes** get impostors (all trees, or only the biggest?).

## Scope

Option B is ~1 shader + 1 bake tool + DTO/authoring fields + a `ScatterRenderer` far pass + gather-
radius change. Landable in a few focused commits, each verifiable in the workbench. I can start once
the crossover/end distances and single-vs-octahedral call are made.
