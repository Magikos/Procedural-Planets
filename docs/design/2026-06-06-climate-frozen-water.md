# Climate-aware frozen water

**Status:** Planned as biome/climate slice 1d.
**Trigger:** Surface F10 `20260606-164137` shows an unfrozen inland lake inside
a polar snow region near 73 degrees south.

## Decision

Implement static climate-aware freezing immediately after the slice 1b/1c
checkpoint commit and before broad terrain texture/look work.

Do not implement this as "snow biome = blue-white water." Frozen water is a
water state driven by local surface temperature and water-body classification.
Biome assignment supplies climate context but is not the final gate.

## Existing substrate

- Slice 1b provides deterministic normalized surface temperature.
- `WaterMeshBuilder` already computes connected water components.
- Water vertex color currently stores:
  - R: normalized depth
  - G: normalized distance from shore
  - B: small-body to ocean factor
  - A: unused constant `1`
- `Ocean.shader` already uses body factor to distinguish lake motion from ocean
  motion.

The alpha channel can carry a generated freeze factor without adding another
water mesh stream.

## Static v1 behavior

1. Sample climate temperature at every wet graph vertex at water elevation.
2. Preserve connected-component membership during `ClassifyWaterBodies`.
3. For small inland bodies, derive one component-level temperature and freeze
   factor so a lake freezes coherently instead of forming triangle patches.
4. For large ocean bodies, allow local temperature to control polar sea ice.
5. Blend between the component and local results using the existing body factor.
6. Store freeze factor in water vertex-color alpha.
7. In `Ocean.shader`, freeze factor must:
   - suppress vertex swell and fragment wave displacement/normals;
   - suppress shore foam, whitecaps, wakes, and liquid shimmer;
   - reduce liquid refraction/distortion;
   - blend toward an authored ice surface response with higher opacity,
     configurable roughness, tint, thickness/depth influence, and breakup.
8. Keep the water volume beneath the ice so underwater rendering still has a
   defined owner.

## Settings

Add a dedicated serializable frozen-water settings block rather than hiding
constants in the shader:

```text
Enabled
LakeFreezeStartTemperature01
LakeFreezeCompleteTemperature01
OceanFreezeStartTemperature01
OceanFreezeCompleteTemperature01
FreezeBlendWidth
IceTint
IceOpacity
IceRoughness
IceNormalStrength
IceBreakupScale
```

Lake thresholds should be warmer than ocean thresholds. This lets small,
shallow inland water freeze in cold climates while requiring stronger cold for
large ocean ice.

## Diagnostics

Add a targeted frozen-water capture set or modes for:

- water body factor;
- sampled water temperature;
- generated freeze factor;
- final ice contribution.

F10 metadata should include the thresholds and counts of fully frozen,
partially frozen, and liquid connected bodies.

## Validation gates

1. **Climate:** the captured snow-region lake has a cold water-temperature
   signal.
2. **Component:** the complete inland lake shares one coherent freeze state.
3. **Motion:** frozen areas have no liquid swell, wave normals, foam, wakes, or
   shimmer.
4. **Boundary:** ice-to-water transitions are broad and irregular, without
   triangle, chunk, or cube-face seams.
5. **Ocean:** ordinary warm oceans remain liquid; polar sea ice is possible but
   does not freeze an entire global ocean component.
6. **Performance:** no per-frame CPU climate queries; generation writes a
   static factor consumed by the shader.

## Deferred dynamic behavior

Seasonal freezing, thawing, cracking, footsteps, breakable ice, and walkable
ice collision belong to a later environment/state phase. That version should
update a runtime water-state atlas and notify physics/gameplay systems.

Static v1 should not add a collider or imply that ice is walkable.

