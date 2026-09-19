# Wardrobe trial sources

Created 2026-09-11 by `HumanWardrobeReviewAuthor`.

The combined outfits come from the existing `Assets/Art/Characters/Human/Review/Townsfolk_Characters.fbx` integration. They reuse `Townsfolk.mat` and its palette. No new vendor files or runtime scripts were imported.

Each entry names our prefabs first, then the source sub-mesh they are fitted from.

- `Rider_01_*` (`SM_Chr_Rider_01`): fitted riding clothes, boots, and hand covering.
- `Soldier_Male_01_*` (`SM_Chr_Soldier_Male_01`): armor and limb protection.
- `Blacksmith_Female_01_*` (`SM_Chr_Blacksmith_Female_01`): female-authored clothing and apron.
- `Mage_01_*` (`SM_Chr_Mage_01`): short tunic with an integrated hood; saved separately.
- `Priest_01_*` (`SM_Chr_Priest_01`): long robe, used in the scene's fourth pair.

Each source prefab retains its complete original mesh. Each fitted prefab uses the existing head-weight cut, landmark fit, four body-shape projections, and skin-palette conversion. This pass applies no outfit-specific clearance or body masks. Combined head accessories can be removed by the head cut and require separate extraction.

The helmet comes from `Assets/Art/Characters/Baseline/ModularCharacters.fbx`, renderer `Chr_HeadCoverings_No_Hair_01`. Its derived mesh uses the existing fit and body-shape bake. Additional attachment bones map to their verified anatomical parents. The soldier helmet prefab shares the existing cached soldier body mesh.

Geometry is baked once. Runtime body sliders select cached frames. See `docs/research/2026-09-11-sidekick-wardrobe-matrix.md` for fit exceptions and validation.
