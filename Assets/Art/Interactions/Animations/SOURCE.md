# Interaction animation sources

Imported on 2026-09-12 from the owned scratch project at `D:/Unity/Explore Assets/Assets`.

| Source folder | Selected files |
|---|---|
| `Loot_Anim_Set/Animations` | `Loot_TreasureChest_Open_Only`, `Loot_TreasureChest_Close_Only`, right-hand tabletop inspection Enter/Loop/Exit, right-hand kneeling floor inspection Enter/Loop/Exit |
| `Simple_Activations/Animations` | `Activate_Wall_KeyTurn_DoorKnob`, `Activate_Floor_Box_Push` |
| `Kevin Iglesias/Human Animations/Animations/Male/Work/Carry` | `HumanM@Carry01_PickUp01`, `HumanM@Carry01_Idle01`, `HumanM@Carry01_Drop01` |
| `Kevin Iglesias/Human Animations/Animations/Male/Misc/Sit` | `HumanM@SitMedium01 - Begin`, `HumanM@SitMedium01 - Loop`, `HumanM@SitMedium01 - Stop` |

Only these 16 FBX files and their metadata were copied. No vendor scripts, controllers, or demo scenes were imported.
Source GUIDs remain intact. Each importer creates its avatar from the included skeleton and human description.
The importer no longer references a scratch-only source avatar. Material import is disabled.
Unity reports all 16 clips as Humanoid motion.

The chest feedback revision adds `Loot_Generic_Rummage_Crouching_Loop.fbx` from the same `Loot_Anim_Set/Animations` folder.
The selected total is now 17 FBX files. The new clip is Humanoid motion and lasts 2.833333 seconds.
Its source GUID remains intact. Its importer creates its own avatar and disables material import, matching the existing clips.
The inspection phase uses this loop instead of holding a short section of the opening clip.

The collection revision adds `Loot_TreasureChest_GrabItem.fbx` from that same source folder, bringing the selected total to 18.
The clip is Humanoid motion and lasts 4.333333 seconds. Its GUID is preserved, material import is disabled, and it creates its own avatar.
It supplies the replaceable `Collect from chest` phase between inspection and closing.

Reusable sequence assets live in `../Definitions`.
The control fixture adds `Activate_Wall_LargeLever_Pull.FBX` and `Activate_Wall_LargeLever_PushUp.FBX` from `Simple_Activations/Animations`.
Source GUIDs remain intact. Both use their included Humanoid skeleton, disable material import, and lock horizontal root motion and root rotation.
The equipment rack reuses the tabletop inspection clips and the already imported `Assets/Art/Characters/Human/Review/Sword_01.fbx` model.
The lever and rack supports are review geometry built from Unity primitives.
The push-button fixture adds `Activate_Wall_ButtonPush.FBX` from the same Simple Activations folder. Its source GUID is preserved; it uses its own Humanoid avatar, disables materials, and locks horizontal root motion and root rotation.
The wheel fixture adds `Activate_Wall_WheelValve_Open.FBX` and `Activate_Wall_WheelValve_Close.FBX` with the same importer policy and preserved source GUIDs. The quarter-turn review wheel uses primitive rim segments and spokes; the character clips remain artist-authored.
The sliding cabinet adds `Loot_CabinetSlidingDoor_GrabItem.fbx` from `Loot_Anim_Set/Animations`, preserving its GUID and using the same Humanoid import policy. Opening uses the initial sliding reach; closing reverses only that reach. Collection uses a separate segment and never runs automatically during closing.
`HumanInteractionDefinitionsAuthor` creates missing definitions and preserves existing definition edits.

The crafting pass imports the owned `Survival_Animations/Animations` hammering Start/Loop/End,
mortar-and-pestle Enter/Loop/Exit, kneeling cooking StirPot and Meat, and kneeling StartFire Flint clips.
These clips retain their source GUIDs and create Humanoid avatars from their included skeletons.
Material import is disabled. SticksRub Start/Loop/End are also copied for future ignition variants; they are not assigned.
Campfire construction reuses the existing ground reach and reverses that reach for recovery.
No named potion-pouring clip was found in the scratch project. Pouring remains unimplemented.

The pouring follow-up imports `ExplosiveLLC/Crafting Mecanim Animation Pack/Animations/Crafter@Item-Water.FBX`.
Its GUID is preserved, it creates its own Humanoid avatar, disables material import, and locks horizontal root motion and root rotation.
Unity reports two seconds of Humanoid motion. The review adapts this watering action for reagent pouring using a raised hand contact.

Rummage transition import (2026-09-13): owned `Loot_Anim_Set/Animations/Loot_Generic_Rummage_Crouching_Start.fbx` and `_End.fbx`.
Both FBX files retain exact source bytes and source GUIDs. Materials are disabled and avatarSetup is 1, matching the existing crouching loop.
The included HumanDescription creates the avatar; no scratch-only avatar import is required. The historical avatar-source GUID remains informational metadata.
Selected clip ranges remain source Start frames 0–15 and End frames 100–115. Full source FBX takes remain intact.
Source root/loop settings and curve timing remain unchanged. One-shot playback must use explicit phase bounds.
No additional scripts, controllers, meshes, or materials were copied. Unity validation remains pending.

| Added filename | GUID | FBX SHA256 | Source metadata SHA256 | Imported metadata SHA256 |
|---|---|---|---|---|
| `Loot_Generic_Rummage_Crouching_Start.fbx` | `c377b05550821df4d81448a6f355ddd4` | `240B40662BF12A45EF9DD719FCFD318988E161DD0D6D8B09A8F658AAA2E46DE0` | `60C74BB9BFACE30C0906F12111568DE0FC92E0C37EC848F0A4E98976EB4E8B72` | `5D7EA4465CC852A46EE20D8A64F75E65BBF8C72444BA550D10462495D906AF8D` |
| `Loot_Generic_Rummage_Crouching_End.fbx` | `6f413a95b4fcc054ea8b1ed129e76e89` | `EFF638AA4D99A3D55473E4E20F25C083CD587FBC8233AA6C3D03B54A9E4D182C` | `F3713ED440B41A0B89D06FC718559475132063524CB0D62FAF2973DBF480B438` | `7BF3D46355308DD5546E192AD51CAD0C456F3909E4F0D071613A6B5728C01179` |


## Tree chopping — 2026-09-15

Imported `Survival_TreeChop_Start.FBX`, `Survival_TreeChop_Horizontal_Loop.FBX`, and `Survival_TreeChop_Exit.FBX` from `D:/Unity/Explore Assets/Assets/Survival_Animations/Animations/`. Source FBX bytes and GUIDs remain intact. Importers create Humanoid avatars from their included skeletons instead of referencing the scratch avatar.

`Tree Prepare axe.anim`, `Tree Chop.anim`, and `Tree Finish chopping.anim` are independent editable copies. Their durations are 1.6667, 2, and 1.3333 seconds. No per-animation generator is required. The `Chop tree` definition owns impact timing and repeated-work behavior.

`../RoundOne/Axe_01.fbx`, the pack's `Axe_Mesh.FBX`, comes from the same pack under `Models/Axe/`; the fixture uses the project material `HarvestWood.mat`. No vendor scripts or controllers were imported.

### Chopping correction — 2026-09-15

The initial CreateFromThisModel import was incorrect for these motion files. They now use CopyFromOther with `Chopping Source Avatar.fbx`, copied from the pack's `Animations/T_pose.fbx` (GUID `410a28d0b2994ed4190fdcda58e299ff`). `Chopping Source Rig.fbx` comes from `Models/Android_SkeletalMesh.fbx` for independent reference. The three editable clips were refreshed from corrected imports; the rejected files are preserved under local-only/animation-review/chopping-2026-09-15/revision1-assets.

The demo attaches the axe to `hand_l`, not `hand_r`. HeldToolGrip stores primary palm orientation, a bounded secondary handle segment and blade contact. The scene adds a 15-degree grip-fit adjustment for the production hand. Source files remain unchanged.

### Mining — 2026-09-15

Imported Survival_PickAxe_LowHeight.FBX and Survival_PickAxe_MediumHeight.FBX from D:/Unity/Explore Assets/Assets/Survival_Animations/Animations with original GUIDs. Both use the existing Chopping Source Avatar.fbx (vendor T_pose). Mine LowHeight.anim and Mine MediumHeight.anim are editable extracted clips at their native 2.5-second duration; no authoring generator owns them. Only LowHeight is wired and rendered.

`../RoundOne/PickAxe_01.fbx`, the pack's PickAxe_Mesh.FBX, comes from the same pack's Models/Axe folder. The review uses its MeshFilter.sharedMesh directly, excluding the exported CATRig/Circle hierarchy. The demo identifies the left hand as the driving hand. The shared HeldToolGrip supplies physical palm and supporting-handle contacts.

Mining currently reuses editable Tree Prepare axe.anim and Tree Finish chopping.anim for entry and recovery. No source FBX bytes were edited.

### Gathering — 2026-09-15

Survival_Foraging_BerryBush.FBX and Loot_FloorPickUp_Kneel_HarvestItem.fbx were copied with source metadata from their matching owned packs. Gather berries.anim and Gather flower.anim are editable copies. Berry uses Chopping Source Avatar.fbx; flower uses Gathering Source Avatar.fbx from Loot_Anim_Set/Animations/T_pose.fbx. Runtime phase ranges select one pluck and recovery at native rate. See docs/design/2026-09-15-gathering-interactions.md for evidence and limits.
