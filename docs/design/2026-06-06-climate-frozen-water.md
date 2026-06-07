# Climate-aware frozen water

**Status:** Static slice 1d visually validated by Frozen Water F10
`20260606-184523` through `20260606-184527`.
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

The alpha channel carries effective water temperature without adding another
water mesh stream. The shader derives freeze factor from that temperature,
body factor, and the serialized lake/ocean thresholds. This preserves both
temperature and freeze as independently inspectable diagnostics.

## Static v1 behavior

1. Sample climate temperature at every wet graph vertex at water elevation.
2. Preserve connected-component membership during `ClassifyWaterBodies`.
3. For small inland bodies, derive one component-level temperature and freeze
   factor so a lake freezes coherently instead of forming triangle patches.
4. For large ocean bodies, allow local temperature to control polar sea ice.
5. Blend between the component and local results using the existing body factor.
6. Store effective water temperature in water vertex-color alpha.
7. Derive freeze factor in `Ocean.shader` from temperature, body factor, and
   the lake/ocean threshold pairs.
8. In `Ocean.shader`, freeze factor must:
   - suppress vertex swell and fragment wave displacement/normals;
   - suppress shore foam, whitecaps, wakes, and liquid shimmer;
   - reduce liquid refraction/distortion;
   - blend toward an authored ice surface response with higher opacity,
     configurable roughness, tint, thickness/depth influence, and breakup.
9. Keep the water volume beneath the ice so underwater rendering still has a
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

The `Frozen Water` capture set includes:

- water body factor;
- sampled water temperature;
- generated freeze factor;
- final ice contribution.

F10 metadata should include the thresholds and counts of fully frozen,
partially frozen, and liquid connected bodies.

Use `debug.capture-set "Frozen Water"` and then press F10, or run
`debug.capture-set "Frozen Water"` followed by `debug.capture`.

## Implemented defaults

- Lake freeze range: complete at `0.26`, begins thawing through `0.36`.
- Ocean freeze range: complete at `0.10`, begins thawing through `0.20`.
- Lake vertices use a connected-component average temperature.
- Ocean vertices use local climate temperature.
- Intermediate bodies blend component and local temperature using body factor.
- Ice appearance defaults: opacity `0.88`, roughness `0.72`, normal strength
  `0.35`, breakup scale `95 m`.

These values are initial semantic defaults, not a visual tuning conclusion.
The diagnostic capture determines whether threshold adjustment is warranted.

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
