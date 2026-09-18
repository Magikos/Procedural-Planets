# Deer

Source: D:/Unity/Explore Assets/Assets/polyperfect/Low Poly Animated Animals/
Pack: Low Poly Animated Animals (polyperfect)

Taken from the pack: `Deer_Rig.fbx`, `Deer_Animations.fbx` and `Deer_Animations_Eat.fbx` and the `Deer_COL_1k.png` colour map. The eat animation is a second FBX from the same pack, kept separate
because it imports with different settings.

Ours: `Deer.mat` (uses our `Planet/PropLit`, not the pack's material), the
`DeerMale.prefab` and `DeerFemale.prefab` we assemble, the settings objects beside it (every species has a
`*Visuals.asset`; `*Audio.asset` and `*Combat.asset` exist where the species
needs them), and the `*Prototype.anim` survival clips our editor authors. Two prefabs, `DeerMale` and `DeerFemale`, share the mesh;
`DeerAntlerless.asset` drives the female variant.

Audio: `DeerCall.ogg`. Provenance for these clips is not recorded anywhere in
the repo. Re-establish it before production use.

Not imported: no pack scripts, controllers, demo scenes, prefabs, or materials. We
rebuilt the prefab and the material against our own shader and creature systems.
