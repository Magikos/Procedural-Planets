# Grass overlay angular color correction

Bryan reported sharp triangular terrain color patches on 2026-09-10.
Similar marks reproduced at the saved Grass review viewpoint on seed1691104419.

The grass overlay's fine fleck modulation caused the reproduced marks.
The terrain's base biome color and grass tint were smooth in isolated captures.
Removing the fleck modulation removes the marks in both albedo and normal rendering.

## Change

`Assets/Graphics/Shaders/PlanetVertexColor.shader` no longer uses the fine anisotropic
fleck noise to modulate grass coverage and brightness. Its derivative filter is also removed.
Macro noise, detail noise, patch noise, and distance-faded fiber remain.
The existing fiber debug modes now display fiber alone.

Biome weights, authored tints, grass placement, rock slope masks, and snow masks are unchanged.
The change removes one noise evaluation. No GPU timing improvement is claimed.

## Isolation and verification

Unity was shared with Rivers. All captures below ran after its editor release.
The review camera matches `local-only/grass-audit/2026-09-10/surface-smoke.json`.
Time was frozen at0.644489348. Near grass was temporarily disabled to expose the surface.

| Capture suffix | Result |
|---|---|
| biome-triangle-blanket | Angular marks visible with normal lighting |
| biome-triangle-blanket-albedo | Marks survive terrain lighting bypass |
| biome-triangle-no-blanket | Marks disappear when the overlay is disabled |
| biome-triangle-tint | Grass tint is smooth |
| biome-triangle-coverage | Final overlay coverage contains the pattern |
| biome-triangle-no-macro | Suppressing macro/fiber variation leaves the marks |
| biome-triangle-final-albedo | Marks removed after fleck removal |
| biome-triangle-final-off | Normal rendering retains shadows without the marks |
| biome-triangle-final-grass | Near grass restored |

PNG captures and metadata are archived in `local-only/biome-triangles/`.
The exact viewpoint from Bryan's screenshot was not available. Validation used the reproducible Grass viewpoint.
The world regenerated between source changes; albedo proof avoids weather-lighting differences.

An initial derivative experiment moved fleck filtering to unconditional fragment inputs.
It did not remove the marks. That experiment is absent from the final shader.
The captures identify the fleck layer; they do not prove a specific GPU derivative fault.

Unity imported the shader successfully. Final shader compilation finished with no errors.
Two existing warnings remained:

- `use of potentially uninitialized variable (WeatherCloudConvectivity)`
- `use of potentially uninitialized variable (EvaluateGrassOverlay)`

`git diff --check` passed. No C# code changed, so no C# build or EditMode test run was required.
The nine CPU polygon tests from the separate Rivers proposal do not validate this shader change.

Camera, time, debug mode, and grass layers were restored after capture.
The original screen-capture attempts returned black and are not validation evidence.
