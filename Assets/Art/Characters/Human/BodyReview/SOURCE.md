# Body fitting experiment

Source base parts: `D:/Unity/Explore Assets/Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans/`.
The 22 body-part FBXs in `../BaseParts/` are renamed copies and retain source GUIDs.
They include the head, facial parts, torso, hips, arms, hands, legs, and feet.
The base hips include underwear. The imported meshes retain their native body blend shapes.

`BareBody.prefab` uses these parts on the existing Humanoid skeleton and project material.
`SourcePartsBody.prefab` replaces its torso, upper arms, and lower arms with five converted source parts.
The source parts come from `Assets/Art/Characters/Baseline/Baseline.prefab`.
The original source meshes, prefab, and materials remain unchanged.

The conversion maps explicitly named bones and uses weighted joint translations in the common rest frame.
Spine anchors preserve relative hip-to-neck height because equal-numbered bones occupy different anatomical positions.
The generated mesh uses body bind poses and retains the original weights, topology, and material UVs.
`SkeletonFit` interpolates between original rest proportions and these translated vertex positions.
It is an experimental fit for these five parts, not a general garment conversion guarantee.

The five converted parts now contain `defaultBuff`, `defaultHeavy`, `defaultSkinny`, and `masculineFeminine` blend shapes.
The Editor bake finds the closest triangle on the bare body head, torso, arms, hands, and hips for each fitted source vertex.
It interpolates that triangle's shape displacement with barycentric weights and recalculates normals on the garment's own topology.
All parts share the same reference surface. The calculation runs during authoring; the result is saved in the mesh assets.
The review sliders drive native and converted parts together. Converted shape weights also follow the skeleton-fit amount.
The transfer preserves the existing neutral fit; it does not weld pre-existing seams or match skin colour.
`Tools/Actors/Human/Bake Body Shapes` regenerates only the body frames and preserves `SkeletonFit`.
Authoring: `Tools/Actors/Human/Refit Review Source Parts` regenerates only the five derived mesh assets and hybrid prefab.
Refitting also rebakes the four body shapes so they cannot silently disappear after a geometry update.
It preserves derived mesh GUIDs. Do not use it after manual mesh edits without preserving those edits.

The transfer currently interpolates body displacement while preserving the garment's existing offset from that surface.
It does not preserve rigid armour thickness under every deformation or generate corrective shapes for extreme combinations.
