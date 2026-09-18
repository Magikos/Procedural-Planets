# Meadow grass and bushes

Source: D:/Unity/Explore Assets/Assets/TEM/
Pack: TEM — Toon Enchanted Meadow

Taken: five grass patch meshes, two flower bush meshes, and the `Meadow_Atlas.png`
texture the set shares. The `TEM_` filename prefix was stripped and the `.meta`
moved with each file, so the GUIDs survived and every serialized reference stayed
intact.

Ours: `Meadow.mat` and `Bush.mat`, both on our `Scatter/FoliageLit` shader, both
binding `Meadow_Atlas.png` from this folder. They sit here rather than in
`../../Materials` because they serve only this set. Renaming them rewrote their
internal `m_Name`; the GUIDs did not change.

Not imported: no pack scripts, shaders, prefabs, demo scenes, or presets. These
files carry no `AssetOrigin` metadata because they were copied in by hand rather
than through a package import.
