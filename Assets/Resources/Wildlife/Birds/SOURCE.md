# Bird art

Source: local asset catalog, polyperfect / Low Poly Animated Animals.
`Eagle.fbx` is `Meshes/Animals/Eagle/SKM_Eagle_Animations.fbx`.
`Atlas.png` is `Textures/Animals/Eagle_COL_2k.png`.
The mesh and texture are unchanged. The model importer retains the original clip definitions with a new asset GUID.
Runtime uses the project's planet-aware material and a manual animation graph; no vendor scripts are imported.
These assets retain their original vendor license. Do not redistribute them separately.

`Vulture.fbx` is `Meshes/Animals/Vulture/SKM_Vulture_Animations.fbx` from the same pack.
`VultureColor.png` is `Textures/Animals/Vulture_COL_2k.png`.
The original model contains `Vulture_Idle` and `Vulture_Fly` clips. It has no separate glide clip.
These files retain the catalog importer settings with new GUIDs. Unity validation is queued.

`Seagull/Model.fbx` is `Meshes/Animals/Seagull/SKM_Seagull_Rig.fbx` from the same pack.
`Seagull/Idle.anim` and `Seagull/Fly.anim` are `Seagul_Sitting.anim` and `Seagul_Fly.anim`.
`Seagull/Color.png` is `Textures/Animals/Seagull_COL_1k.png`.
The mesh, animations and texture are unchanged. Their metadata uses new GUIDs.
The original gull rig has no separate glide clip.

`Seagull/Source.prefab` preserves the owned `Prefabs/Animals/Seagul.prefab` art hierarchy.
Its unkeyed joint rest rotations differ from a freshly imported FBX hierarchy.
The supplied partial Sitting/Fly curves depend on those authored rest rotations.
Vendor scripts, controller, colliders, navigation components, and material references were removed.
Mesh/avatar references point to our copied FBX. Runtime material and animation remain project-owned.
