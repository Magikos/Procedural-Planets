# Style review art

- Sword_01: D:/Unity/Explore Assets/Assets/Synty/PolygonFantasyKingdom/Models/SM_Wep_Sword_01.fbx -> Assets/Art/Characters/Human/Review/Sword_01.fbx
- Shield_01: D:/Unity/Explore Assets/Assets/Synty/PolygonFantasyKingdom/Models/SM_Wep_Shield_01.fbx -> Assets/Art/Characters/Human/Review/Shield_01.fbx
- PriestHat_01: D:/Unity/Explore Assets/Assets/Synty/PolygonFantasyKingdom/Models/SM_Chr_Attach_Priest_Hat_01.fbx -> Assets/Art/Characters/Human/Review/PriestHat_01.fbx
- HouseWallDoor_01: D:/Unity/Explore Assets/Assets/Synty/PolygonFantasyKingdom/Models/SM_Bld_House_Wall_Door_01.fbx -> Assets/Art/Characters/Human/Review/HouseWallDoor_01.fbx
- HouseRoofThatch_01: D:/Unity/Explore Assets/Assets/Synty/PolygonFantasyKingdom/Models/SM_Bld_House_Roof_Thatch_01.fbx -> Assets/Art/Characters/Human/Review/HouseRoofThatch_01.fbx
- Townsfolk_Capes: D:/Unity/Explore Assets/Assets/Synty/PolygonFantasyKingdom/Models/FantasyKingdom_Capes.fbx -> Assets/Art/Characters/Human/Review/Townsfolk_Capes.fbx

Townsfolk palette: Textures/Alts/PolygonFantasyKingdom_01_A.png. Materials use Planet/PropLit.

Accessory follow-up (2026-09-10):

- `BagExplorer_01.fbx` (source `SM_Prop_Bag_Explorer_01.fbx`): copied from the same Fantasy Townsfolk Models directory. Source GUID `9ab6ef79b1e051b4b89872558a64fc35`.
- `Pouch_01.fbx` (source `SM_Item_Pouch_01.fbx`): copied from the same directory. Source GUID `08c367eca6f5ad1409c894dd50e43c7c`.
- Both models use the existing Townsfolk material and atlas. No vendor scripts or additional textures were imported.
- `Assets/Scenes/Tests/HumanAccessoryReview.unity` attaches the priest hat, explorer backpack, pouch, and mage cape to each comparison actor.
- Rigid attachments preserve their neutral fit relative to Head, Hips, or UpperChest. They do not run through the clothing surface-fit or blend-shape bake.
- The cape retains all seven original cape bones. Its root follows UpperChest. No cloth simulation, collision response, or body-shape correction is installed.
- Accessories have individual visibility controls. The scene starts with capes on monks and backpacks on peasants. Hats and pouches start visible.
- The original Townsfolk scene and character prefab meshes remain unchanged by this accessory trial.

Mount follow-up (2026-09-11): the two fitted backpacks and two fitted pouches now store four baked body-shape offsets in `HumanAccessoryFit`. `HumanAccessoryReviewAuthor.BakeMountOffsets` samples the nearest clothing triangle on each neutral fitted reference. It reuses `HumanBodyShapeAuthor.ClosestWeights` and transforms shape movement into the attachment parent's coordinates. Runtime sliders combine the four cached vectors; no mesh search or mesh generation runs per frame. These offsets preserve the reviewed neutral attachment positions. They do not provide collision detection or resize straps. Original source mounts and cape behavior remain unchanged.

Cape follow-up (2026-09-11):

- `MageCapeCloth.asset` derives from `SM_Chr_Mage_Cape_01` in the existing cape FBX. Two triangle subdivision passes preserve the original surface, UV corners, bind poses, and source vertex positions. The derived mesh has 4,655 render vertices and 6,016 triangles.
- `HumanCapeClothAuthor.Build` installs the derived mesh and native Unity Cloth on all four cape attachments in the accessory scene. Each cloth instance has 3,010 simulation vertices, including 813 pinned shoulder vertices.
- Four trigger capsule proxies per actor cover the torso, pelvis, and thighs. They are explicit Cloth collision references, not gameplay collision geometry.
- `HumanCapeClothReview` supplies simulation and reset controls. Motion changes and paused pose scrubbing reset cloth movement history.
- Collision proxies currently support the neutral body only. Other body shapes automatically use the rigid skinned cape. Resetting the body restores simulation.
- Floor, hand, backpack, and self-collision are not implemented. This is a fitting-room trial, not production cloth integration or a fix for the monk robe.
- The source cape FBX and character prefabs remain unchanged. The derived mesh is shared by all four cloth renderers.

Assets/AssetPacks/PolyperfectAnimals/Deer/DeerMale.prefab
Assets/AssetPacks/PolyperfectAnimals/Wolf/Wolf.prefab

Legacy torso uses the existing source hero geometry and original skeleton. Legacy cape model retains its original rig. Neither is fitted to our body.

Townsfolk clothing follow-up (2026-09-10): `Townsfolk_Characters.fbx` now also supplies monk and peasant bodies for the separate Townsfolk review. Its bytes match the owned source model. The local Humanoid importer maps Hips to Hips instead of the source metadata's Root. BodyReview/Townsfolk/SOURCE.md records the derived meshes and limitations.
