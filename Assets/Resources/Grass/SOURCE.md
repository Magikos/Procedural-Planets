# Grass cards and meadow atlas

Source: `D:/Unity/Explore Assets/Assets/Synty/PolygonNatureBiomes/PNB_Meadow_Forest/`
Pack: Synty POLYGON Nature Biomes, Meadow / Forest biome

Seven files, each byte-identical to its source:

| Ours | Source path under the biome folder |
| --- | --- |
| `GrassLarge_01.fbx` | `Models/SM_Env_Grass_Large_01.fbx` |
| `GrassLarge_02.fbx` | `Models/SM_Env_Grass_Large_02.fbx` |
| `GrassLarge_03.fbx` | `Models/SM_Env_Grass_Large_03.fbx` |
| `Grass_01.tga` | `Textures/Plants/Grass_01.tga` |
| `Grass_Mid_01.tga` | `Textures/Plants/Grass_Mid_01.tga` |
| `Grass_Short_01.tga` | `Textures/Plants/Grass_Short_01.tga` |
| `Meadow_01.png` | `Textures/PolygonNatureBiomes_Meadow_Texture_01.png` |

The three `.tga` files are shared between the Meadow/Forest and Tropical/Jungle
biome folders and are identical in both. These copies are attributed to
Meadow/Forest because the `.fbx` and `.png` beside them exist only there.

Adapted: `Grass.mat` is ours. It carries the meadow atlas on `_MainTex` but runs
`Graphics/Shaders/Scatter.shader`, not the vendor's material or shader. No vendor
material asset was imported.

`Grass_01.tga` is the live file here. `Art/Materials/FoliageGrass.mat` reads it on
both `_BaseMap` and `_TrunkMap` through `Graphics/Shaders/FoliageLit.shader`.

Not imported: no package, no vendor directory structure, no vendor scripts,
shaders, prefabs, demo scenes, or presets.

Open, two items.

Nothing in the project references `Grass.mat`, the three `GrassLarge_*` meshes,
`Grass_Mid_01.tga`, `Grass_Short_01.tga`, or `Meadow_01.png`. They are
GUID-searched to zero users outside this folder. They read as leftovers of the
reverted textured-card grass experiment. They are kept, not deleted, until that
arc is settled.

The folder still breaks our conventions. `Resources/Grass` is kind-first
placement; the domain-first home is `Art/Vegetation/Grass`. The move waits on the
decision above, because moving art that may be deleted is wasted work.
