# Round-one interaction props

Source: `D:/Unity/Explore Assets/Assets/Synty/PolygonFantasyKingdom/Models/`.

Selected model files. Ours on the left, the filename inside the pack on the right:

| Ours | Source |
| --- | --- |
| `Chest_01.fbx` | `SM_Prop_Chest_01.fbx`, including the authored lid and latch hierarchy |
| `CastleDoorSingle_01.fbx` | `SM_Bld_Castle_Door_Single_01.fbx` |
| `TableWood_04.fbx` | `SM_Prop_Table_Wood_04.fbx` |
| `ChairWood_01.fbx` | `SM_Prop_Chair_Wood_01.fbx` |
| `CrateWood_01.fbx` | `SM_Prop_Crate_Wood_01.fbx` |
| `MugTankard_01.fbx` | `SM_Item_Mug_Tankard_01.fbx` |
| `Bottle_01.fbx` | `SM_Item_Bottle_01.fbx` |
| `Book_01.fbx` | `SM_Item_Book_01.fbx` |
| `Key_01.fbx` | `Bonus/SM_Item_Key_01.fbx` |
| `Shelf_01.fbx` | `SM_Prop_Shelf_01.fbx` |

Each `.meta` moved with its file, so the GUIDs and every serialized reference
survived the rename. The node names inside the chest FBX, `SM_Prop_Chest_01_Lid`
and `SM_Prop_Chest_01_Latch`, are the source's own and are unchanged;
`HumanInteractionReviewAuthor` finds the lid and latch by those names.

Two tools in this folder come from a different pack,
`D:/Unity/Explore Assets/Assets/Survival_Animations/Models/Axe/`:
`Axe_01.fbx` is its `Axe_Mesh.FBX` and `PickAxe_01.fbx` is its `PickAxe_Mesh.FBX`.
Both FBXs export a CATRig/Circle authoring hierarchy that we do not want; the
harvest and mining fixtures read `MeshFilter.sharedMesh` directly and leave it.

The files preserve their source metadata identities through `HumanTrialAuthor.CopyArt`.
Scene renderers use the existing `Assets/Art/Characters/Human/Review/Townsfolk.mat` and its project shader.
No vendor scripts, shaders, demo scenes, or packages were imported.
Scene instances apply scale and placement adjustments. Source meshes remain unchanged.
