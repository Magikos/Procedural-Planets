---
name: project-ocean-wave-approach
description: "The correct architecture for visible ocean waves on the planet — vertex-displace the existing mesh, not a camera patch"
metadata:
  node_type: memory
  type: project
  originSessionId: 97829702-a6c8-47a8-a3db-f18c9ac1f8af
---

Getting visible 3D ocean waves: the **camera-following patch was the WRONG approach** (tried 2026-05-28). A separate disc that follows the camera slides relative to the water as you move and only covers a circle, not the whole ocean.

**Correct approach (confirmed by Sebastian Lague's reference `local-only/Geographical-Adventures-main/Assets/Scripts/Shaders/Game/Waves.hlsl`):** displace the **existing** ocean mesh's vertices **radially** in its vertex shader — `newPos = spherePos * (radius + waveHeight)`. Whole ocean, world-fixed (no sliding), attached to the water. Pole distortion is handled by blending two angular projections (xz + xy) by latitude weight. Waves faded near shore (Lague uses a shore-distance map; our mesh already has shore/depth in vertex color, so use that).

This is the GPU Gems "two-layer" model: **large geometric waves displace the mesh** (our ~21 m vertex spacing handles big swell fine), **small detail stays as fragment normals** (the far `Ocean.shader` already does this).

**Boats/buoyancy still favor Gerstner** (closed-form, CPU-evaluable — see [[reference-local-only]] FFT-Ocean is hard to query on CPU). So the mesh displacement should use a Gerstner/analytic-gradient height that can be mirrored in C#.

**How to apply:** Don't reintroduce a separate camera water patch for waves. Modify the existing far-ocean mesh's vertex stage. `Ocean.shader`'s `vert` is currently pass-through; `ComputeSurfaceWaves` already computes a world-stable wave field in the fragment. See [[feedback-async-no-coroutines]] is unrelated; the water context doc is `.amazonq/rules/memory-bank/water.md`.
