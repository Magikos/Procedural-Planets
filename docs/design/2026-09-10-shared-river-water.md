# Shared river and receiving-water rendering

## Result

Horizontal rivers now use the receiving water's actual material instance. Waterfalls retain the falling-sheet shader.
The previous river shader applied independent tint, absorption, reflection, and transparency formulas.
Fading that shader over the lake could not remove the material difference.

The new surface adds a small downstream ripple perturbation to the existing water waves.
Its strength decreases through the terminal approach and receiving tail.
It does not change base colour or add another transparent surface layer.
This is visual current, not a fluid simulation.

## Changes

- `PlanetWaterSurface.SurfaceMaterial` exposes the owned material to `RiverRenderer`.
- `Planet` skips river rendering when water is disabled.
- Horizontal meshes share the material. Disposal unregisters meshes and preserves the receiving material.
- Waterfall-only rendering replaces the obsolete horizontal path in `Resources/River.shader`.
- The depth prepass also owns receiving tails. A standing-water mask restricts their footprint.
- Visible and depth passes use the same displacement helper. Ordinary river reaches suppress large wind swell.
- The surface rejects the other geometry type selected by the prepass, preventing river/lake double composition.
- Surface and volume depth use the visible water radius and the opaque scene receiver.
- Packed water kind uses bit 0 for receiving-body optics and bit 1 for river geometry.
- Atmosphere and volume consumers decode both bits consistently.

The existing 11-bit packing still fits exactly in a half float: nine shoreline bits and two kind bits.
Kinds 0/1 remain lake/ocean surfaces. Kinds 2/3 select river geometry with lake/ocean optics.
These are rendering values. Gameplay water identity, terrain carving, fish habitat, and freshwater queries did not change.

The depth correction removes four standing-shore texture reads per depth calculation.
The old field interpolated nearby standing-water levels. It did not describe elevated rivers.
Both visible and depth passes now measure from the surface they rasterize.
The existing distant-receiver rejection and baked-depth fallback remain.

## Validation

Unity 6000.7.0a5, seed 1691104419, existing shared checkout.
Rivers held Unity ownership. Performance confirmed that it had no active tests or edits.

- Planet build: zero errors, 19 existing warnings.
- Existing focused river/water fixtures: 36 passed, zero failures.
- New renderer material/lifetime fixtures: two passed, zero failures.
- Ocean, water prepass, Atmosphere, and waterfall shaders: zero compiler errors.
- Ocean reported one warning; Atmosphere reported four warnings.
- Captures checked overhead, low angle, night, underwater, and a lake outlet.
- The final static capture disabled wave normals, motion, glitter, whitecaps, shore foam, wave amplitude, and swell.
- Diagnostic settings and the original frozen time were restored.

The initial build failed with `error CS0841: Cannot use local variable 'planet' before it is declared`.
The water-disabled guard now retrieves `PlanetDto` directly.
The initial test compile failed with `error CS1503: Argument 1: cannot convert from 'UnityEngine.MeshFilter' to 'string'`.
The disposal assertion now uses `CollectionAssert.DoesNotContain`.
Both corrections passed their subsequent checks.

| Warmed view | CPU average / P95 | GPU average / P95 | Samples CPU / GPU |
| --- | --- | --- | --- |
| RiverChannelFinal overhead | 15.81 / 19.83 ms | 14.62 / 18.90 ms | 120 / 115 |
| River mouth low angle | 18.90 / 23.76 ms | 17.21 / 22.85 ms | 120 / 115 |

These are complete-frame measurements, not isolated shader timings or a controlled before/after speedup.
The reported 1 FPS did not occur in these views.

## Evidence and replay

Evidence folder: `local-only/river-shared-surface-2026-09-10/`.
The folder contains source snapshots, capture PNGs and metadata, build output, and timing JSON files.

Final overhead: `F10-water.00-Off-shared-water-final-20260910-094144-620.png`.
Final low angle: `F10-water.00-Off-shared-water-oblique-final-20260910-093722-759.png`.
Static: `F10-water.00-Off-shared-water-static-final-20260910-094053-108.png`.
Lake: `F10-water.00-Off-shared-water-lake-20260910-093937-982.png`.
Night and underwater captures use the corresponding `shared-water-night` and `shared-water-underwater` labels.

Use the saved `RiverChannelFinal` camera after generation completes.
The ocean outlet is segment 11. Its terminal radius is 5000 m and half-width is 20 m.
The lake check used segment 27, terminal radius 5000.11035 m.
Local noon is 0.5; the night capture used 0.0.

## Limits and follow-up

Visual acceptance remains Bryan's decision. The same material does not remove differences caused by real depth or terrain shape.
The river still supplies baked channel depth and bank distance where the scene receiver is unavailable.
Grazing views can therefore retain differences in the fallback or swell inputs.
River temperature remains the previous liquid value; this change does not add river freezing.
The known waterfall terrain intrusion is separate and remains open.

Scatter System reviewed the existing Lake Reeds, Lake Cattails, Lake Lily, and Lake Rocks assets.
Its proposal is in `plans/river-margin-candidate/integration-review.md`.
No scatter code or assets were imported in this change.
Floating plants currently lose bed-depth eligibility, and proper freshwater filtering needs shared immutable body-kind inputs.
The proposed later slice uses irregular reed pockets, separate lily colonies, and occasional grounded rocks.
It preserves open water and defers river lilies until suitable habitat can be identified.
