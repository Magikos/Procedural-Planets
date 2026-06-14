# ProceduralPlanets Water Layer Reset Plan

Bryan explicitly chose to stop the current water-polish tuning loop and "start over" from the ground up using visible, testable render layers. This should be treated as the active water direction on resume.

Latest confirmed state:
- Clouds were fixed after F10 proved `QualityLevel: 0 (PC)` was being misclassified as low. `QualityController` now classifies quality by name, and the latest F10 sidecars show `CloudQuality: tier=High, low=False, stepMultiplier=1.00`.
- Water is still wrong in the final `Off` view. Latest F10 set around `20260524-223630` shows:
  - `Off` is a washed transparent sheet with little/no convincing surface effect.
  - `WaterNoPost` / `SurfaceOnly` show darker raw surface behavior before later compositing.
  - `SurfaceRawOpaque` shows the ocean shader can generate visible color/detail.
  - `SurfaceFxProof` clearly shows the generated wave/effect patterns.
- Conclusion: effects are being generated, but the production water stack/composite is not presenting them correctly. Stop adding more effect tuning on top of the current final composite.

New water rebuild strategy:
1. Bottom distortion / caustic layer only.
   - Render the world normally.
   - Suppress blue water surface color, foam, wakes, glint, and top waves.
   - Apply only shallow-water bottom distortion/refraction/caustic-like movement where terrain is under the water mask.
   - Acceptance test: from a lake/ocean shoreline view, the terrain under shallow water visibly shimmers/distorts like Bryan's reference image. If this is not visible in normal view, stop and debug this layer only.
2. Base water body.
   - Add only water tint/depth transparency.
   - Acceptance test: bottom distortion remains visible through the tint.
3. Top surface normals / ripples.
   - Add surface ripple/wave normal layer.
   - Acceptance test: surface motion appears without erasing bottom distortion.
4. Foam, shore wash, and wakes.
   - Add each separately with hard debug/proof modes.
   - Acceptance test: each is obvious in normal view, not only in debug view.
5. Glint / sun sparkle.
   - Add last because it depends on normals, sun direction, view angle, and final composite.

Implementation guidance for next session:
- Start by adding a `BottomDistortionOnly` water debug mode/capture path.
- Prefer a hard visual isolation mode over subtle tuning. It should make the bottom distortion unmistakable.
- The first layer likely belongs in `WaterVolume.shader` / refraction path, not the top `Ocean.shader` surface branch.
- Do not continue tweaking alpha, foam, glint, or wave values until the bottom distortion layer is independently visible in `Off`/normal view or an explicit bottom-distortion production mode.
- Keep F10 sidecar checks: quality, FPS, mode, focus, weather, wave, and surface effect metadata.
- Continue to preserve the hard diagnostic rule: isolate one layer, prove it in normal view, then add the next layer.
