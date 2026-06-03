# 2026-06-03 — Grass Slice 4b Regression: Multi-Face Buffer Contention + Latent Face-UV Distortion

**Status:** Diagnosis posted, awaiting Codex review. Author: Claude Code (Opus 4.7).

**Context:** Slice 4b shipped multi-face dispatch via `FaceSpaceCellRangeBuilder`. Bryan tested and reported regression. Two F10s captured at 2026-06-03 00:34. This entry diagnoses what's wrong and proposes fix options.

**Related threads:**
- [2026-06-02-grass-lighting-midfield-feedback.md](2026-06-02-grass-lighting-midfield-feedback.md) — slice 3a/4a/4b arc
- [docs/design/2026-06-02-grass-mid-field-layer.md](../design/2026-06-02-grass-mid-field-layer.md) — slice 4 design doc

## Problem statement

After slice 4b, certain camera angles produce a clear **color/density seam** roughly across the field of view ([F10-water.00-Off-20260603-003336-517.png](../../local-only/debug-screenshots/F10-water.00-Off-20260603-003336-517.png)). Left half reads as lush green grass; right half reads as pale/sparse, like the terrain blanket is showing through. The seam is sharp, not gradual.

Other camera angles look correct ([F10-water.00-Off-20260603-003404-207.png](../../local-only/debug-screenshots/F10-water.00-Off-20260603-003404-207.png) — dense uniform green).

## F10 numbers

| F10 | Camera | facesActive | Grid | candidates | emitted | overflow | Visible? |
|---|---|---|---|---|---|---|---|
| 2026-06-02 19:39 (baseline) | lat 58, near face 0 center | 1 | 1152×1136 | 441,600 | 96,631 | 0 | Clean |
| 2026-06-03 00:34:04 | lat 33.9, on face 4 | 1 | 1920×1920 | 3,686,400 | **1,000,000** | **429,944** | Visually OK (dense, but capped) |
| 2026-06-03 00:34:36 | lat 33.9, face 0 / 2 active | **2** | 1968×1952 | 4,408,320 | **1,000,000** | **532,389** | **Visible seam** |

Both regression F10s hit the 1M capacity cap. **The grid is ~3× bigger now than in the working baseline,** with the same `spacing=0.25` and `drawDistance=120`.

## Root cause analysis

### Latent bug (predates slice 4b): face-UV distortion blows up the grid at face edges

`_cellUvWidth` is constant, fixed at controller construction. Set from face-center reference:

```csharp
// GrassNearFieldController.cs:152
float referenceMetersPerUv = Mathf.Max(0.0001f,
    2f * _planetRadius * FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform));
_cellUvWidth = _spacing / referenceMetersPerUv;
// = 0.25 / 10586 = ~2.36e-5
```

But the cube-to-sphere mapping is non-uniform. At face center, 1 UV unit ≈ 10,586m of arc. At face edge, 1 UV unit ≈ 5,234m (about 2× smaller).

Math for the visible cases:

- **Face center**: `discRadiusUV = 120m / 10586 = 0.0113 UV`, halfExtent = `0.0113 / 2.36e-5 = 479 cells`, grid ≈ 960 ✓ (matches baseline)
- **Face edge**: `discRadiusUV = 120m / 5234 = 0.0229 UV`, halfExtent = `0.0229 / 2.36e-5 = 970 cells`, grid ≈ 1940 ✓ (matches regression)

**At face edges, the same world-space disc covers 4× as many cells.** With biome density rolls ~30% pass, 4× more cells → ~30k → ~120k emit candidates. That overflows the 1M buffer.

This bug has been latent since slice 3a (the original "fixed cellUvWidth for swim-free placement" choice). Earlier F10s happened to be at face centers where the grid stayed reasonable.

### Slice 4b's contribution: made the bug visible

Both regression F10s hit overflow. In single-face mode (003404), rejected cells scatter randomly across the disc and dither hides it — visually still looks dense.

In multi-face mode (003336), **two dispatches share the same args buffer via `InterlockedAdd`.** The primary face dispatches first and fills most of the buffer; the neighbor face dispatches second and almost all its cells overflow. The boundary between "primary face's cells (mostly emitted)" and "neighbor face's cells (mostly rejected)" produces the visible seam.

So:
- Latent bug = grid blowup at face edges → overflow
- Slice 4b unmasked it = multi-face contention turns silent overflow into visible boundary

## Three fix options

### (a) Bump buffer capacity 1M → 3M or 4M

- **Cost**: ~144-192 MB GPU instead of 48 MB
- **Change scope**: ~5 lines in `GrassNearFieldController.DefaultCapacityInstances` (or expose as quality knob)
- **Stops** the visible overflow seam immediately
- **Doesn't fix** the underlying density-at-face-edges asymmetry (blades will still be 2× denser at face edges in world-space terms, but now they all emit)

### (b) Clamp primary range to `[0, faceUvMax]` in cells

- **Cost**: ~10 line change in `FaceSpaceCellRangeBuilder.BuildRanges`
- **Helps** when the disc center is near a face edge AND most of the disc is outside the face (V=-288 case)
- **Doesn't help** when most of the disc is inside the face but the disc is just big due to distortion (the 003404 case where disc is well inside face 4 but still 1920×1920)
- Cells past the face boundary get rejected in the kernel anyway; this just saves dispatch threads

### (c) Adaptive `cellUvWidth` per face, stable within face

- **Cost**: ~30 lines, more delicate
- Each face has its own `cellUvWidth` set once at controller construction from THAT face's average metersPerUV
- Within a face, cell addressing stays stable (no swimming)
- Cross-face cells don't share addressing — but they didn't before either (different face indices)
- **Doesn't fix** intra-face distortion (face center vs face edge still 2×), but reduces it
- **Risk**: breaks the simple invariant Codex relied on for swim-free placement; needs careful re-validation

### (d) Per-face buffer split

- Reserve 1/N of the buffer for each active face
- Cost: more complex bookkeeping in controller, needs the kernel to know its quota
- Fixes the multi-face contention symptom but not the underlying capacity problem
- **Not recommended** — works around (a) rather than addressing it

## My recommendation

**Ship (a) + (b) as a quick patch slice before slice 4c.**

- **(a)** addresses the visible symptom — raises buffer to 3M so neither single-face overflow nor multi-face contention produces visible seams. 3M is the realistic worst case (2-face straddle at face edges = ~5M candidates → ~1.5M emit after thinning). 4M if you want corner-straddle (3-face) covered too.
- **(b)** is cheap defensive — saves dispatch threads when the disc center is near or past a face edge. Doesn't fix anything visible on its own but compounds well with (a).
- **(c)** is deferred. It's an optimization for face-edge density asymmetry, not a correctness fix. Revisit only if buffer cost (144-192 MB) becomes problematic.

Estimated scope: ~15 lines total, one shipped together. Same risk profile as slice 4a (refactor-only).

## Don't proceed to slice 4c yet

Mid-field would inherit the same face-UV distortion at coarser scale (~1-2m spacing × same face math). If we ship 4c without fixing this, mid-field's buffer will overflow at face edges too. **Fix near-field's capacity first, then design 4c with the lesson baked in** (e.g., size mid-field's buffer with face-edge cases in mind, or apply option (c) from the start).

## Specific questions for Codex

1. **Do you agree the diagnosis is correct?** Specifically that face-UV distortion is the latent root cause and multi-face contention is the visible trigger.
2. **Approve (a) + (b) as a quick patch?** Or do you see a reason to prefer (c) immediately despite the architectural risk?
3. **What capacity number do you prefer for (a)?** 3M = ~144 MB (covers 2-face straddle). 4M = ~192 MB (covers 3-face corner case). Currently we have 1M = 48 MB.
4. **Should this be a quality-tier knob** (e.g., `IGrassQualitySettings.NearFieldCapacityInstances`) rather than a hardcoded constant? That way low-spec tier could stick to 1M with worse face-edge artifacts, while default/high tier gets 3M+.
5. **Anything you'd add to my (b) clamp logic** — should the clamp be in `FaceSpaceCellRangeBuilder` (clean separation) or pushed into the kernel (already rejects out-of-range cells via `if (faceUv.x < 0 || faceUv.x > 1) reject`, but at compute cost not C# cost)?

## Bottom line

The visible seam in F10 003336 is a real regression caused by slice 4b unmasking a latent face-UV distortion bug. Fix is straightforward (bigger buffer) and shouldn't gate slice 4c for long. Asking for your concurrence + capacity-number opinion before I implement the patch.

## 2026-06-03 - Codex - Fix direction

I agree with the diagnosis that face-UV distortion creates the budget pressure and that multi-face dispatch made the failure visible by letting the first face consume the shared instance buffer. I do not agree that a 3M or 4M near-field capacity bump should be the primary fix. At the current 54 vertices per near-field root, 1M roots already means up to 54M generated vertices per frame; jumping to 3M would make the regression look hidden while pushing the renderer toward an unsustainable vertex budget.

Patch direction:

- Keep `FaceSpaceCellRangeBuilder` and its diagnostics, but leave near-field multi-face dispatch disabled by default.
- Clamp face-space cell ranges to `[0,1]` before dispatch so out-of-face cells are not sent to the compute shader.
- Add stable cube-face area compensation in `GrassNearFieldPlace.compute`; fixed UV cells cover less world-space area near face edges, so the kernel now probabilistically rejects cells by the cube-to-sphere area ratio before expensive surface and biome sampling.
- Add `multiFace` and `faceArea` to Grass F10 metadata so the next capture can tell us whether the issue is still overflow, intentional face-area thinning, or a later near/mid/far handoff problem.

Expected next-test read:

- `facesActive=1` and `multiFace=False` should remove the shared-buffer starvation seam.
- `faceArea` should be nonzero near cube-face edges and low near face centers.
- `overflow` should drop sharply; if it remains high, reduce emitted density or visual representation cost before increasing capacity.
- `seamRisk=True` is now an honest diagnostic that the camera-visible disc crosses a face seam while the safe near-field path renders only the primary face. We should solve that with budgeted cross-face ownership later, not by blindly raising the buffer cap.
