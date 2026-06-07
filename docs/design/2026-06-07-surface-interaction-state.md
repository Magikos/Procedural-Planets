# Surface Interaction and Modification State

**Date:** 2026-06-07
**Status:** Approved architecture; implementation follows terrain texture mixing.

## Purpose

Grass bending, recovering trails, worn paths, foundations, weather, and snow
must share positional surface state without writing directly into generated
grass buffers. Grass is regenerated from world data, so gameplay systems write
surface state and renderers consume it.

## State ownership

### 1. Immediate interaction

`GrassInteractorBuffer` contains the current player, creature, vehicle, and
movable-object volumes.

- Not persisted.
- Rebuilt from active interactors.
- Produces immediate grass displacement while an object is present.
- Does not write a texture for simple momentary contact.

### 2. Recoverable force state

`TransientForceMap` stores positional grass deformation:

```text
R,G = tangent-plane bend direction
B   = flattening strength
A   = accumulated wear
```

- Written by footsteps, creatures, vehicles, impacts, and explosions.
- Decays according to biome recovery settings.
- Produces trails that slowly recover.
- Accumulated wear may promote a repeatedly used route into a persistent path.

### 3. Persistent surface modification

`SurfaceModificationMap` stores durable world changes:

```text
R = grass exclusion
G = path or dirt material blend
B = foundation or constructed-surface coverage
A = reserved modification type or strength
```

- Written by path tools, construction, paving, and permanent world actions.
- Saved as chunk delta state.
- Does not decay unless the owning gameplay action explicitly removes it.
- Read by near grass, chunk grass, terrain materials, spawning, and later snow.

Permanent construction must also retain authoritative footprint records.
The GPU map is a derived cache. If two foundations overlap and one is removed,
the remaining footprint must still own the shared area; a destructive texture
erase cannot represent that ownership correctly.

### 4. Environmental state

`WeatherStateMap` remains separate because it has different producers and
decay rules:

```text
R = wetness
G = snow depth
B = burn or scorch
A = heat
```

Deep terrain or snow deformation can use a dedicated `TrackMap` when a scalar
height/depth field is required.

## Rendering contract

The supported grass LOD stack is:

```text
near grass -> chunk grass -> far terrain blanket
```

All three representations must derive coverage from the same persistent and
environmental state. Near and chunk grass reject excluded texels. The terrain
blanket removes vegetation tint over the same areas. Terrain shading blends
path, foundation, scorch, wetness, and snow materials from the shared maps.

Gameplay systems never mutate grass instance buffers directly.

## Update and persistence rules

- Immediate interactors update every frame without persistence.
- Transient force updates run only around active interaction regions and decay
  on GPU.
- Persistent modifications are event-driven and mark affected chunks dirty.
- Only modified chunks are serialized.
- LOD subdivision resamples state into children; merging conservatively
  combines child state into the parent.
- Cross-face writes use the same cube-face remapping rules as biome and surface
  atlases so paths and foundations do not split at seams.

## Implementation sequence

1. Finish terrain texture mixing and terrain-state consumption.
2. Ship immediate grass interactor bending with debug volumes.
3. Add recoverable force-map painting and biome-driven recovery.
4. Add the persistent modification API and authoritative footprint records.
5. Connect path tools and building foundations when those gameplay systems
   exist.

