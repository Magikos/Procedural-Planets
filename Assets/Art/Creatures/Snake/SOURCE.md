# Snake

Source: D:/Unity/Explore Assets/Assets/polyperfect/Low Poly Animated Animals/
Pack: Low Poly Animated Animals (polyperfect)

Taken from the pack: `Snake_Rig.fbx`, with `Snake_Idle`, `Snake_Slither`, `Snake_Attack`
and `Snake_Death` extracted from it as standalone `.anim` clips and the `Snake_COL_1k.png` colour map.

Ours: `Snake.mat` (uses our `Planet/PropLit`, not the pack's material), the
`Snake.prefab` we assemble, the settings objects beside it (every species has a
`*Visuals.asset`; `*Audio.asset` and `*Combat.asset` exist where the species
needs them), and the `*Prototype.anim` survival clips our editor authors.

Audio: `SnakeHiss.wav`, `SnakeRattle1.ogg` and `SnakeRattle2.ogg`. Provenance for these clips is not recorded anywhere in
the repo. Re-establish it before production use.

Not imported: no pack scripts, controllers, demo scenes, prefabs, or materials. We
rebuilt the prefab and the material against our own shader and creature systems.
