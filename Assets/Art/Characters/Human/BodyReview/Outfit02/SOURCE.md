# Outfit 02 fitting trial

Created 2026-09-10 from the already imported `Assets/Art/Characters/Baseline/ModularCharacters.fbx`. No new vendor files or scripts were imported.

The ten `_Male_02` torso, arm, hand, hip, and leg sections replace the corresponding `_Male_01` sections. The source rig stays unchanged. Mesh-specific bone lists map by bone name, including their differing order and terminal joints.

`HumanOutfitReviewAuthor` calls the same full-body fitting, body-shape transfer, and skin-palette conversion used by Outfit 01. It adds no outfit-specific fitting offsets. The ten derived meshes contain 7,168 vertices and 40 cached body-shape frames. Ten native head and facial renderers remain.

`Assets/Scenes/Tests/HumanOutfitReview.unity` compares the bare body, Outfit 01 fitted, Outfit 02 fitted, and Outfit 02 on the original source rig. The original comparison retains its original skin colour.

Skin matching uses the reviewed Fantasy Hero skin swatch and the existing body material. It preserves clothing UVs and adds one material section to each part with exposed skin. Different palettes need a reviewed swatch mapping. Coupled finger chains and loose-clothing collision remain limitations.

All 129 focused tests passed. Live captures cover neutral, muscular, heavy, skinny, and feminine shapes with walking, running, and crouching poses. Final visual acceptance remains with Bryan.
