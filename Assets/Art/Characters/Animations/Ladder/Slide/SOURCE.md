# Authored ladder slide family

Source: D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations/.

Imported 2026-09-13: three original root-motion FBXs and three In_Place_Versions Humanoid FBXs. No vendor scripts or controllers.

Unity verified the actual Unreal Take intervals: Start frames 0–10 (0.333333 s), Loop 0–20 (0.666667 s), End 0–15 (0.5 s), at 30 Hz. Scratch root-motion metas referenced stale Take 001 ranges. Root reference imports now select the actual default take as Generic. Production in-place imports retain their source Humanoid avatar. FBX bytes remain unchanged.

LadderMotionAuthor samples original root tracks and retargets travel to the production avatar. Native slide root travel: start down 0.096992 m and back 0.039492 m; loop down 1.579627 m; end rises 0.096988 m and returns forward 0.039492 m. End brakes to the climbing pose; it is not a ground dismount.

Editable `Slide Start.anim`, `Slide Loop.anim`, and `Slide End.anim` apply a constant +0.12 m forward body-origin offset. The authoring helper compensates Humanoid importer recentering before applying that offset. Relative limb motion and timing remain unchanged. Runtime plays the family at 1x and fades rail correction during the authored brake/release.

SHA256:
- AnimSeq_Traversal_Ladder_Slide_Down_End.FBX: 7DB80773E0FBF5C2C6EC58A6A62F64C183D5DEBE69C233A066547E376B380525
- AnimSeq_Traversal_Ladder_Slide_Down_Loop.FBX: 83FD71A43DB211419F8D809A29180EF7661E70C14228BB0C6C661CA0D8C9DB96
- AnimSeq_Traversal_Ladder_Slide_Down_Start.FBX: 65B42C4E928B0210568D8264FF21ECF835CD05CBEEABEAFF99F114128DE2837C
- Traversal_Ladder_Slide_Down_End.fbx: EAEA290464B2DA5D3D7E899158EF1A4FFE8E03858975BDAE5AE1B2C5BA90C07B
- Traversal_Ladder_Slide_Down_Loop.fbx: 1A8A1A2CDA4C60FA7D6F7FFCE9D8ADC4FB0141A8D9A19697C3687D3195B18731
- Traversal_Ladder_Slide_Down_Start.fbx: 991408BDBD2270275E660A26F3BCA7780D430C8D6761EDF06C31623241AC873D
