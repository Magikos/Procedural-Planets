# Synty terrain material study

Status: approved by Bryan on 2026-09-09: "It looks good to me."

Bryan approved a comparison of current terrain textures with flatter alternatives, starting with grass, dirt, and rock.
The acceptance target is less fine detail and exaggerated relief, with broad surface shapes that fit the Synty props.
Biome identity, texture scale, and terrain geometry must remain intact.

## Findings and candidate

The previous grass texture contains photographic strands. The rock texture contains granular contrast.
The existing `Planet.mat` also sets `_BiomeNormalStrength` to 8 and `_BiomeNormalReliefShadow` to 0.55.
Reducing those values alone removed much of the harsh relief in the captured view.
Replacing the grass texture then removed the remaining fine strand pattern.

The candidate uses the owned Synty POLYGON Nature Biomes Meadow Forest terrain textures:

| Family | Albedo | Normal |
| --- | --- | --- |
| Grass | Grass_Texture_01.png | Ground_Normals_01.png |
| Dirt | Dirt_Texture_01.png | Dirt_Normals_01.png |
| Rock | Rock_Texture_01.png | Rock_Normals_01.png |

Destination: `Assets/AssetPacks/PolygonNatureBiomes/PNB_Meadow_Forest/Terrain/`.
The subset README records provenance and import settings. Source PNG bytes and GUIDs are preserved.
The project-authored `TerrainMatte_ARM.png` contains AO=1, roughness=1, metallic=0.
This matches the source terrain layers' matte material settings and avoids using unrelated photographic surface data.

`Assets/Graphics/Materials/PlanetSyntyTerrain.mat` clones the previous material.
It sets normal strength to 0.5 and relief shadow to 0.15. Other material settings remain unchanged.
`Planet.asset` now references that candidate. The original material and textures remain available.

The three texture families were replaced wherever the existing biome assets shared them.
Affected definitions: Tundra, Steppe, Taiga, Swamp, Scrub, Grassland, Forest, Savanna, Tropical, Mountain, and LakeShore.
Only affected primary textures receive the matching new normal and ARM maps.
Secondary-only changes retain the primary surface's normal and ARM, as required by the existing albedo-only variant design.
Unity also serialized the existing white `SurfaceAlbedoTint` default in definitions that omitted it.

Sand, snow, wet mud, cracked dirt, snowy rock, and other texture families remain for a later pass.
No shader or runtime code changed. No vendor code or shader was imported.

## Comparison and validation

Interactive local comparison: `local-only/terrain-style-review.html`.
Evidence root: `local-only/debug-screenshots/baselines/2026-09-09-synty-terrain/`.

| Folder | Purpose |
| --- | --- |
| 01-current | Original textures and normal strength 8 |
| 02-reduced-relief | Original textures, normal strength 0.5, relief shadow 0.15 |
| 03-synty | Synty textures with the same reduced relief |
| 04-inland-current | Additional inland baseline; night lighting limits its use |
| 05-fresh-generation | Persisted candidate after a new generation |
| 06-fresh-daylight | Fresh candidate at the first comparison pose, with the captured sun direction restored |
| 07-with-grass | Candidate with the grass system enabled |
| authoring-before | Exact authoring backups for this task |

The first three captures share seed 1691104419, world seed 12345, quality 0 (PC), and one stationary camera.
Sun direction was frozen at approximately (0.3454, 0.8606, 0.3742).
The camera position was (3443.5759, 3271.3972, 1893.9667), with quaternion (0.37749866, -0.3436934, -0.31873098, 0.7986114).
Grass blades and the grass overlay were disabled for the material comparison. Scatter plants remained visible.
F10 captures include normal, AO, roughness, and albedo diagnostic views with sidecars.
Weather and vegetation can vary across frames; these are visual comparisons, not pixel-invariance tests.

Core and Planet builds passed with zero errors. Core reported zero warnings; Planet reported 19 existing warnings.
Logs: `local-only/synty-terrain-core-build.log` and `local-only/synty-terrain-planet-build.log`.
Unity completed a fresh planet generation. All 18 slots loaded with 512-pixel RGBA32 albedo, normal, and ARM arrays.
No texture-array format mismatch or missing-texture errors appeared.
Existing scatter warnings reported three missing prebaked impostors and two overbright foliage materials.
Those warnings concern separate assets and were not changed in this pass.
Targeted `git diff --check` passed. No code changed, so no new logic tests or graph extraction were required.

The captured view supports reduced relief and quieter grass texture detail.
It does not establish final visual acceptance of every biome or of rock and dirt at every slope and distance.
Bryan approved this terrain style on 2026-09-09.

## Recovery

Restore only this task's texture-reference changes in the affected biome definitions and the `PlanetMaterial` reference in `Planet.asset`.
Use `authoring-before` to identify the previous values; preserve any later edits from other work.
Switching the material alone restores relief strength but does not restore texture references.
Do not reset whole files or the shared working tree to undo this candidate.
