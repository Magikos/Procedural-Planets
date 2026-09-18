# Forest floor undergrowth

Source: D:/Unity/Explore Assets/Assets/LMHPOLY/Low Poly Nature Bundle/Vegetation/Vegetation Assets/
Pack: Low Poly Nature Bundle (LMHPOLY)

Taken: seven two-sided flower meshes from `Meshes/Flowers/TwoSided/`, three
mushroom meshes from `Meshes/Mushrooms/`, one reed mesh from `Meshes/Plants/Reeds/`,
and the shared atlas from `Textures/`. The `LMHPOLY_` prefix was stripped and the
`.meta` moved with each file, so the GUIDs survived.

Ours: `Undergrowth.mat` and `Mushroom.mat`, both on our `Scatter/FoliageLit`
shader, both binding `Undergrowth_Atlas.png` from this folder. They sit here
rather than in `../../Materials` because they serve only this set. Renaming them
rewrote their internal `m_Name`; the GUIDs did not change.

Not imported: no pack scripts, shaders, prefabs, demo scenes, or presets, and no
part of the pack's directory structure.
