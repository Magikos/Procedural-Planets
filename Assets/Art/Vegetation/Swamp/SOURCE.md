# Swamp and marshland vegetation

Source: D:/Unity/Explore Assets/Assets/Synty/PolygonNatureBiomes/PNB_Swamp_Marshland/
Pack: POLYGON - Swamp Marshland - Nature Biomes (Synty)

Taken: four models (`Tree_Swamp_01.fbx`, `Tree_Dead_01.fbx`, `Reeds_01.fbx`,
`LillyPads_01.fbx`) from `Models/`, three plant textures from `Textures/Plants/`,
and two ground textures from `Textures/`. Filenames were shortened; the `.meta`
moved with each file so the GUIDs survived.

`Reeds_01_Green.png` sits here rather than in a texture folder because its only
consumer, `FoliageReeds.mat`, serves the wetland scatter prototypes that use these
reed meshes.

Ours: the materials that consume these textures live in `../../Materials` and use
our `Scatter/FoliageLit`.

Not imported: no pack scripts, shaders, prefabs, demo scenes, or presets, and no
part of the pack's directory structure.
