# SDF and Distance-Field Implementation Plan

This document tracks where signed distance fields, analytic distance functions,
and related occupancy fields should be used in Procedural Planets. The goal is
to improve visual quality and performance without mixing experimental rendering
work into unrelated water fixes.

## Terms

- Signed distance field (SDF): returns signed distance to the nearest surface.
  Negative is inside, positive is outside, zero is on the surface.
- Analytic distance function: computes distance/intersection from a formula,
  such as a sphere, capsule, or plane.
- Occupancy field: stores whether a region contains meaningful density. It is
  useful for empty-space skipping, but it is not a true SDF unless it stores
  signed distance.

## Current Priority

Water remains the active feature area. SDF work should be introduced where it
directly helps water quality, water diagnostics, or performance measurements.
Large terrain/grass/general LOD work should wait until the water system is in a
solid state.

## Implementation Order

### 1. Analytic Water-Sphere Intersections

Status: partially present in the water-volume experiments.

Use ray-sphere intersections against the sea-level sphere for water-entry,
water-exit, and underwater path length decisions. Do not use a scalar
`SDFSphere(cameraPosition)` value as if it were a distance along the camera ray;
path length needs ray intersection distances.

Expected uses:

- water-volume path length at grazing angles
- underwater fog/tint distance
- source-color occlusion behind the sea-level sphere
- debug modes that compare raster water coverage against analytic sea coverage

Validation:

- F10 `VolumeOnly`, `WaterOff`, `SeaRay`, `SeaVsMesh`, and `SeaPath`
- above-water low-horizon view
- underwater shore-facing view
- through-planet regression view

### 2. Wake Capsule Distance Functions

Status: good next water-polish target.

Wakes should use analytic capsule or swept-ellipse distance functions per wake
emitter. This gives smoother elongated wakes than circular radius falloff and
keeps the feature cheap because each wake source is a small constant-size loop.

Expected uses:

- wake mask
- wake foam
- wake normal distortion
- optional wake debug isolation

Validation:

- F10 wake capture set
- moving emitter test
- extreme wake intensity mode to prove the wake path is drawing before tuning

### 3. Shore Distance Field

Status: useful, but should be designed carefully before implementation.

A shore distance field can provide stable distance-to-land for shore foam,
run-up bands, shallow-water effects, and shoreline contact polish. It should be
baked after planet/water generation and reused by shaders.

Preferred shape:

- cube-map or texture-array layout that matches existing spherical weather data
- distance stored in meters or normalized meters with an explicit max distance
- regenerated when terrain/water topology changes

Avoid:

- per-frame CPU raycasts
- treating scene depth as a persistent shore distance source
- equirectangular UV assumptions unless the current cube-face data path is
  deliberately replaced

Validation:

- compare old vertex shore data to the field in a debug view
- verify cube-face seams
- verify near-shore and far-shore from above and below water

### 4. Cloud Occupancy Field

Status: performance follow-up, not a true SDF.

A coarse 3D occupancy grid can skip empty cloud-space before expensive cloud
noise sampling. This is an optimization field, not a signed distance field.

Preferred shape:

- low-resolution 3D texture or spherical-layer volume
- built on compute when weather changes
- stores max density or conservative occupancy
- cloud raymarch uses it only to skip empty regions

Validation:

- include FPS/frame timing in F10 metadata
- compare cloud-on scenes before/after with identical camera/sun/weather
- verify no popping when weather evolves

### 5. Terrain SDF

Status: future system work.

A true terrain SDF belongs with marching cubes, caves, deformation, physics, and
spawn placement. It should not be started as part of the current ocean polish
unless a smaller water-specific shore field has proven the data path.

Expected uses:

- terrain deformation and caves
- surface normals for physics/spawning
- cave/terrain ambient occlusion
- future grass placement and culling

## Non-SDF Rendering Follow-Ups From Shader Audit

The old shader audit document has been removed because it was stale: it said no
edits had been made even though many findings had already been applied. The
remaining useful follow-ups are tracked here:

- Verify the new atmosphere/cloud/precipitation frustum culling from orbit,
  surface, underwater, and horizon-tangent views.
- Verify `CLOUD_QUALITY_LOW` on low and high Unity quality levels with F10 FPS
  metadata.
- Validate ocean terrain shadow variants in Unity shader import, not just C#
  builds.
- Revisit precipitation lighting under dense cloud cover only if visual tests
  show rain is too bright in storms.

## Rules For SDF Work

- Prove the target path first with a hard debug mode or extreme value.
- Add one field at a time and keep a before/after capture set.
- Measure performance in the same F10 workflow used for visual debugging.
- Prefer compute shaders for high-volume parallel field construction.
- Keep generated field data out of scene files unless there is a deliberate
  persistence requirement.
