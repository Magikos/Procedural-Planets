# Accessory motion and robe refinement

Created 2026-09-11 in `HumanAccessoryMotionReview.unity`. The previous accessory scene remains unchanged for comparison.

- `BackpackWithoutCup.asset` and `HangingCup.asset` partition the existing `Review/BagExplorer_01.fbx`. Connected mesh islands isolate the cup, fittings, and hanging strap. Every source triangle belongs to exactly one result. Vertex indices and UVs remain intact; bounds use the retained triangles.
- The cup assembly moves from the backpack's side to its rear. Its upper strap supplies the swing pivot. The backpack body does not change shape.
- Pouches move 35 mm toward the body. Their existing body-shape offsets remain active. A pivot at the pouch neck provides independent motion.
- Fitted hats move down 45 mm. Source hat positions remain unchanged.
- Both monk robe copies omit covered shin-skin triangles and add lower-cloth clearance. The lower cloth widens by up to 15% laterally and 25 mm fore/aft, fading toward the waist. Feet and hem triangles remain. The original FBX and prefab meshes are untouched.
- Robe shape frames retain their position deltas within 10 micrometres after Unity rebuilds sparse frames. Normal frames are recalculated for the changed surface. The largest measured omitted displacement was 7.35 micrometres.
- Capes use the original 731-vertex mage mesh and six existing cape bones. `HumanAccessoryMotion` adapts the project's `BoneChainSpring`; it does not contain another spring solver. All four pouches, cups, and capes use that solver.
- Cape contact support uses six animated body capsules and the fitting room's flat floor. Capsule radius adjustments for body shapes are approximate. Bone-center collision does not prove clearance across the entire cape surface.
- The review resets spring history for motion changes, paused scrubbing, and body-shape changes. The secondary-motion toggle restores the authored pose. Runtime spring integration is required; the geometry edits and body-shape offsets remain cached.

Tail Animator 2 and Magica Cloth 1.12.13 were verified in the scratch project. No vendor runtime code was imported. The existing shared solver already supplies the required spring, gravity, angle-limit, and contact behavior.
