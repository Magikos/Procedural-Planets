# Polar bear

Source: D:/Unity/Explore Assets/Assets/polyperfect/Low Poly Animated Animals/
Pack: Low Poly Animated Animals (polyperfect)

Taken from the pack: nothing — this species reuses the bear rig and clips from `../Bear` and the `PolarBear_COL_2k.png` colour map. Only the texture is specific to this species; the mesh and animation
come from the shared bear source.

Ours: `PolarBear.mat` (uses our `Planet/PropLit`, not the pack's material), the
`PolarBear.prefab` we assemble, the settings objects beside it (every species has a
`*Visuals.asset`; `*Audio.asset` and `*Combat.asset` exist where the species
needs them), and the `*Prototype.anim` survival clips our editor authors.

Not imported: no pack scripts, controllers, demo scenes, prefabs, or materials. We
rebuilt the prefab and the material against our own shader and creature systems.
