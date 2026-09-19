# Human

The modular humanoid kit. It builds the player and every humanoid NPC, so this is
not a role folder — a role folder under `Characters/` is named for its role, and
this one is named for what it is.

Source: D:/Unity/Explore Assets/Assets/Synty/SidekickCharacters/Characters/Starter/Starter_01/

Taken: the Starter_01 prefab, its combined mesh, its saved Humanoid avatar, and
the ColorMap. Mesh, avatar, and texture GUIDs are preserved from the original
import, so every serialized reference survived the move into this folder.

Adapted: the prefab is ours and uses `Planet/PropLit` through the hero material
convention, not the vendor's shader. `Human.mat` and `Human.prefab` carry that
name, not the pack's.

Not imported: no vendor scripts, shaders, or creator tools. The pack's special
character shader effects are not reproduced by our material conversion.

Sub-folders: `BaseParts` holds the 22 modular body-part meshes, `BodyReview` and
`Review` hold our review scaffolding, and `Converted` holds outfit conversions
onto this skeleton. Each carries its own `SOURCE.md`.

The humanoid animation clips this kit retargets live in `../Animations`. The
clothing and baseline body live in `../Baseline`.

The vendor vocabulary is gone from every filename and serialized name here. Two
source-side names remain and are not ours to change. `SK_HUMN_BASE_01_*` is the
sub-mesh name inside each `BaseParts` FBX. `SM_Chr_*` is both the sub-mesh name
inside a source character FBX and the key the converter looks a part up by;
`HumanOutfitConverter.RoleName` maps that key to the name we write, so nothing we
generate carries the prefix.
