# Townsfolk clothing trial

Created 2026-09-10 from owned Synty Polygon Fantasy Kingdom art.

Source: `D:/Unity/Explore Assets/Assets/Synty/PolygonFantasyKingdom/Models/FantasyKingdom_Characters.fbx`.
Imported copy: `Assets/Art/Characters/Human/Review/Townsfolk_Characters.fbx`.
The FBX copy matches the source hash. Its original GUID had no collision in this project. No vendor scripts or shaders were imported.

The local importer enables readable meshes and Humanoid animation, preserves transforms, and disables material and clip imports. Its hips mapping corrects the source metadata from `Root` to `Hips`. The source file and source metadata remain unchanged.

The trial selects the source sub-meshes `SM_Chr_Monk_01` and `SM_Chr_Peasant_Male_01`, saved here as `Monk_01_*` and `Peasant_Male_01_*`. Each original prefab retains one complete source renderer. Each fitted prefab retains ten native head/facial renderers and one derived body renderer. The monk mesh has 4,264 vertices; the peasant mesh has 4,143 vertices. Each stores `SkeletonFit` plus four generated body shapes.

These are combined characters, not modular outfits. `HumanTownsfolkReviewAuthor.RemoveHead` excludes triangles that touch a vertex with more than 50% combined Head/Eyes/Eyebrows/Jaw weight. It retains vertex indices for correspondence with the source. Unreferenced head vertices remain in the derived mesh but do not render. This boundary is reviewed for these two characters, not a universal head separator.

The converter reuses joint-anchor fitting and nearest-triangle shape transfer. It adds no outfit-specific fit offsets. Source garment weights remain; the monk robe follows the legs. No cloth dynamics or collision was added.

Materials reuse `Review/Townsfolk.mat`, `Review/TownsfolkAtlas.png`, and `Human.mat`. The inspected Townsfolk skin swatch matches Fantasy Hero's (255, 204, 174), so the existing skin conversion applies. Clothing UVs and colours remain unchanged.

Review scene: `Assets/Scenes/Tests/HumanTownsfolkReview.unity`. Left to right: original monk, fitted monk, fitted peasant, original peasant. Earlier review scenes and the gameplay default remain unchanged. See `docs/research/2026-09-10-sidekick-trial-results.md` for validation and remaining limitations.
