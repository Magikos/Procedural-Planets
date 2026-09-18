# Full-body source fitting trial

Created 2026-09-10 from project-owned art copies. Source: `Assets/Art/Characters/Baseline/Baseline.prefab` and the adjacent `BareBody.prefab`.

`FullSourceBody.prefab` retains ten native head and facial renderers. Ten converted source renderers supply the torso, arms, gloves, hips, legs, and boots. The original vendor art and accepted upper-body trial remain unchanged.

`HumanFullBodyReviewAuthor` reuses the joint-anchor fitting and nearest-triangle body-shape transfer authors. Each derived mesh stores `SkeletonFit` and four generated frames: muscular, heavy, skinny, and feminine. These 40 shape frames are cached assets. Runtime controls apply weights; they do not repeat the surface search.

Mappings follow source bones, because the source leg mesh names do not match their left/right bones. Terminal finger and toe mappings preserve their source offset when they share a target bone with their parent. The source gloves couple several fingers into one chain; they do not provide independent finger control.

Review scene: `Assets/Scenes/Tests/HumanFullBodyReview.unity`. This is a reversible experiment, not the gameplay default. Skin colour, seams, and final visual acceptance remain open.

Skin matching added 2026-09-10: `HumanSkinAuthor` assigns exposed Fantasy Hero skin triangles to the existing body material and its skin palette texel. The head and clothing colours remain unchanged. Four parts gain a second material section. All ten meshes retain identical saved body-shape frames and bind poses. This palette mapping is specific to the reviewed Fantasy Hero swatch; other palettes require explicit review. The matched colour reduces the visible join, but this pass does not weld or alter seam geometry.
