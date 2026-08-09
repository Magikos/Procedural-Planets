# Surface unification — decision note (2026-08-09)

Status: **investigation + decision, findings-first.** The two low-risk siblings from the same request
(rock orientation, capsule lighting) shipped this session; this one is the architectural root-cause item and
is left as a reviewed decision, not an unreviewed rewrite. Read alongside
[2026-08-09-collision-strategy.md](2026-08-09-collision-strategy.md) and
[../../plans/look-fixes-backlog.md](../../plans/look-fixes-backlog.md).

## The problem

The planet exposes two surfaces that disagree:

- **Analytic** — `IPlanetSurfaceSampler.TryGetSurfaceRadius` → `ChunkedSurfaceProvider.TryGetLocalSurfaceRadius`
  ([ChunkedSurfaceProvider.cs:288](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs#L288)):
  `FindLeafContaining(faceUv)` then `leaf.TrySampleRadius(...)`. Answers along **any** direction.
- **Visible mesh** — `IPlanetSurfaceRaycaster.TryRaycastSurface` → `TryRaycastVisibleSurface`, which walks the
  camera-selected `GetVisibleLeaves(face)`. Answers only where the **camera** currently has selected leaves.

They diverge by the LOD tessellation error (~3–24 u measured previously; the coarse visible triangle chords
the finer analytic curve). Anything **placed** on the analytic surface floats above / sinks below what is
**rendered**. This one mismatch drove the character fall-through and the "scatter floats" report, and will
bite collision if a query and a collider disagree on where the ground is.

## What was measured this session (play mode, radius 5257 world)

- `TryGetSurfaceRadius` answered at every probed direction (near and far), returning sensible radii.
- `TryRaycastSurface` **missed at every distance** from a camera spawned over ocean — even the point directly
  under the camera. The raycast only hits **camera-selected visible leaves**; a probe over unselected
  (here, ocean-floor / far) leaves returns nothing.

The raycast miss is the important finding, independent of the exact delta number: **the visible surface is a
camera-scoped query.** It is the right ground truth for things that live near the camera (the character — it
already grounds on this raycast), and the wrong thing to couple a **deterministic, camera-independent**
producer to.

## The trap in the naive fix

The backlog's "Option A — make `TryGetLocalSurfaceRadius` sample the visible leaf" would make the analytic
answer depend on the **current camera LOD**. Scatter placement is deliberately built the other way: the gather
runs off-camera in a tile cache and must be **deterministic** (same tile → same instances → same heights,
regardless of where the camera is or was). Feeding it a camera-dependent height would make a tile's props
jump as the camera approaches and the visible LOD refines — pop-in and non-determinism, the exact things the
tile cache exists to avoid. So: **do not make the analytic sampler camera-dependent.**

## Decision

**One ground truth, two roles — already the collision-strategy stance, restated for surfaces:**

1. **Render mesh = ground truth for things that sit ON the surface near the camera** — character grounding
   (done: `PlanetRaycastGrounding` uses the raycast), and future streamed collision (per the collision doc,
   colliders are cooked from the render mesh). These are camera-local by nature, so a camera-scoped query fits.
2. **Analytic sampler = deterministic approximation** for camera-independent producers (scatter placement, AI
   height, coarse spawn) and for queries far from the camera where the raycast has no selected leaf to hit.
   It stays camera-independent.

**Why this is enough for the reported symptoms:**
- **Character fall-through** — fixed (grounds on the render-mesh raycast).
- **Scatter "parts float" up close** — was dominated by the **orientation** bug (props stood radial on a
  slope, so the downhill edge lifted off). Fixed this session (`ConformToSlope`). Near the camera the terrain
  is at max LOD, so the analytic↔visible **height** gap there is a max-LOD triangle sagitta (cm-scale), not
  the metres-scale gap that shows only at distance, where it is sub-pixel.
- **Distance float** — sub-pixel; not worth camera-coupling placement to erase.

## If a real height-unification need appears later (recommended approach, for review)

Do **not** route scatter through the camera raycast. Instead place props on the **max-depth leaf mesh
triangle** (barycentric interpolation of the leaf's three vertex radii at the prop's UV) rather than on the
raw analytic field. That height is (a) deterministic — the max-depth leaf is camera-independent — and (b)
exactly what the leaf renders when it is at full LOD, so a prop lands on the mesh instead of the curve, killing
even the max-LOD sagitta. Cost/《confirm before building》:
- Needs the max-depth leaf's CPU mesh (vertices / radii) available to the gather. Coarse chunks null
  `CpuVertexRadii` while keeping `CpuVertices`
  ([ChunkedSurfaceProvider.cs](../../Assets/Scripts/Planet/Surface/ChunkedSurfaceProvider.cs) ~1488) —
  confirm the finest leaf a gather touches actually has radii, or derive radius from `|CpuVertices|`.
- **Confirm** whether `FindLeafContaining` returns a fixed max-depth leaf or a camera-driven-depth leaf today;
  if the quadtree subdivision is itself camera-driven, the "analytic is camera-independent" claim needs the
  gather pinned to a reference depth, not the live tree.
- Keep it off the hot path (gather is already Burst/parallel); this is extra sampling, not a per-frame cost.

This is a **plan, not a landed change** — it touches the surface provider and scatter determinism, so it goes
through review before any code, per the audit workflow.
