# Vegetation vista baseline — 2026-09-12

Four captures record current placement. They do not demonstrate the proposed habitat improvements.
Interaction System explicitly released Unity for this bounded capture window.
Scatter restored Unity and explicitly released it back for the wheel pass.

## Capture setup

- Scene: `Assets/Scenes/Planet.unity`; Unity `6000.7.0a5`.
- Generated planet seed: `1691104419`; bootstrap world seed: `12345`.
- Base setting radius: 5000; generated maximum radius: 5288.13; sea radius: 5000.
- Existing `ScatterField.GotoCmd` selected each biome; camera height was 4 metres.
- Local noon was frozen after each move. Weather remained uncontrolled.
- Images use `Camera.Render` at 1280 × 720. These are not F10 diagnostic captures.
- Camera pose and capture metadata accompany each image. Temporary render resources were restored and released.
- The metadata field `baseRadius` contains `LastGeneratedRadius`, not the base radius setting.

Artifacts are under `local-only/debug-screenshots/baselines/2026-09-12-vegetation-vistas/`.
Each label has `.png`, `.txt`, and `-counts.txt` files.

| Label | Diagnostic biome | Accepted / candidates within 60 m | Visual observation |
|---|---|---|---|
| grassland-baseline | Grassland; secondary Forest, blend 0.12 | 8540 / 104331 | Dense foreground plants and trees obstruct the open-bank composition. |
| forest-baseline | Forest; secondary Grassland, blend 0.14 | 8070 / 113093 | Trees of several sizes mix with repeated understory plants across the visible ground. |
| lake-bank-baseline | LakeShore; secondary Forest, blend 0.02 | 3225 / 209344 | Water meets a narrow exposed bank beneath dense woodland. Aquatic colonies are not established by this view. |
| steppe-baseline | Steppe; secondary Tundra, blend 0.29 | 2421 / 85564 | The sampled slope has open ground, shrubs, rocks, and scattered trees. It does not establish a wide grassy plain. |

Counts come from synchronous `scatter.count` gathers. They are not rendered instance counts or frame performance measurements.
These samples do not prove biome-wide behavior or satisfy visual acceptance.
The lake capture also shows a conspicuous water-edge pattern. Its cause was not investigated in this placement window.

## Validation and limitations

The preceding test window passed 63 existing tests, with zero failures or skips.
See `2026-09-09-vegetation-scatter-validation-queue.md` for the job and result artifact.
No additional tests ran during this capture window. No source, asset, or scene changes were made.
Habitat placement, flower overlap, shallow-water colonies, age transitions, and broad plains still require implementation and dedicated validation.

Two diagnostic setup attempts needed correction:

- An early teleport returned `"scatter: not configured (generate a planet first)"`. It succeeded after generation completed.
- A reflection call returned `"Runtime error: Number of parameters specified does not match the expected number."` with `TargetParameterCountException`.
  The corrected two-argument `CountCmd` call succeeded.

The final error-filtered console returned these shader warnings, classified by the tool as `Exception`:

```text
Shader warning in 'RainParticleUpdate': Program 'RainUpdate', warning X4714: sum of temp registers and indexable temp registers times 64 threads exceeds the recommended total 16384.  Performance may be reduced at kernel RainUpdate (on dx12)
Shader warning in 'GrassNearFieldPlace': use of potentially uninitialized variable (LoadPathWearTexel) at kernel PlaceAndCullNearField at GrassNearFieldPlace.compute(250) (on dx12)
```

Earlier warnings reported three missing baked impostor cards and two foliage material tints above 1.05.
No baking or material changes were attempted.

## Editor restoration

After stopping Play Mode, `Planet.unity` was clean.
Scatter loaded `Assets/Scenes/Tests/SidekickInteractionReview.unity` without saving either scene.
The restored scene was the only loaded scene, active and clean, with 41 roots.
Unity reported `playing=False; changing=False; compiling=False`.
Scatter had no pending test or Editor operation at release.
