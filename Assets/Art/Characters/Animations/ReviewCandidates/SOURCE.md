# Running-jump original source review candidates

Copied 2026-09-12 for independent natural-rate review. These clips are unselected review candidates.
No production scene or performance asset references changed. Unity import and visual validation remain pending.

Source root: `D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations/`.
Owned package: Ultimate Traversal Anims, product 217762, package version 2.0.

Each FBX and its .meta file was copied byte-for-byte. Importer settings were not changed.
No scripts, controllers, scenes, materials, or renderer models were copied.

| Source relative path | Destination filename | FBX SHA256 | Metadata SHA256 |
|---|---|---|---|
| `T_pose.FBX` | `T_pose.FBX` | `DD194D7D45B124A1492036230CCA50B266B98D5E7A34923806CF0C693260EB97` | `CECF9EF8EFBA7C7BC7211DA53D7A7D27123D0A1CDC37B27FA46C927D643F2386` |
| `Traversal_Movement_Jump_fromRun_toRun.fbx` | `Traversal_Movement_Jump_fromRun_toRun.fbx` | `4EB6FAAECD0CEA444FCC78BE670F6B25CB6734B8654DEDEA0766694B276FE17C` | `4A199D09027D3EBE6C28287B2870010DCF0723F1F4C66BD16BC145A459FF2C0E` |
| `New Animations 2.0/Traversal_JumpRunForward.FBX` | `Traversal_JumpRunForward.FBX` | `7DE0D7C8B9021DCED607545B86FE0031A75C5F7D82350B786A34014DBCF9AC40` | `CDE2754D1AA5070A79110D6E5CDD9785A5B729985224C373E67B71F83E4B0385` |

## Dependencies and identity

Both jump importers reference avatar fileID 9000000, GUID `2091e1aca366db648b573fbcebc047c6`.
`T_pose.FBX` supplies that avatar with avatarSetup 1 and animationType 3.
The jump files retain GUIDs `69ae6502a6c2e964c8ca34e0731deb0a` and `3417b8f4eaadf2c4ca5d3bbe47169c89`.
A complete Assets metadata search found none of these three identities before copying.
The three source metadata files expose no other nonlocal GUID references.

The existing `../Traversal Reference.fbx` has identical FBX bytes but GUID `ca511cddbf264378a0d9832053bac483`.
The temporary original-avatar copy preserves source references without changing importers.
A later Unity-controlled remap may reuse that existing avatar after equivalence and import validity checks.

## Preview conditions and known hazard

The first jump source selects frames 0–40; the 2.0 source selects frames 0–75.
Both source importers enable looping and root blend flags. These settings remain unchanged for source inspection.
Do not infer clip duration from frame counts without retrieving the source sample rate.
Catalog section 11.7 historically reports Universal Traversal root-motion avatar mismatch errors:
`Copied Avatar Rig Configuration mis-match. Transform 'root' not found in HumanDescription.`
Current metadata resolves the declared avatar dependency, but offline inspection cannot certify rig compatibility.
Verify both imports in Unity before preview. Record any necessary importer adaptation as a separate comparison stage.

## Existing original-rig references

`../Human Motion Reference.fbx` is byte-identical to Kevin Iglesias `Human Animations/Models/HumanF_Model.fbx`.
`../Male Motion Reference.fbx` is byte-identical to Kevin Iglesias `Human Animations/Models/HumanM_Model.fbx`.
Use these existing models for Kevin Iglesias original-rig references after checking their import settings.
Kevin Iglesias models are not original rigs for the RamsterZ chest clip. Label that chest playback as retargeted.
No additional humanoid model was imported.

Current project versions checked: Unity 6000.7.0a5, URP 17.7.0.
This candidate import does not close the animation continuity audit or grant visual approval.
