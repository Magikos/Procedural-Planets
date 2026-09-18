# Sidekick character readiness

## Selected baseline

Use the Sidekick head and fitted POLYGON Rider outfit for the next interaction work.
The cached prefab is `Assets/Art/Characters/Human/Converted/SM_Chr_Rider_01_v2/SM_Chr_Rider_01_Sidekick.prefab`.
The working scene is `Assets/Scenes/Tests/SidekickGameplayReview.unity`.

The scene copies the current Humanoid movement review and uses its existing motor, animation graph, ground contacts, camera, swimming, and traversal controls.
It measures crawl cycle distances for this character. It retains the existing interaction panel and stations.
The Sidekick panel sits 5 cm farther forward. This lets the hand follow its full 60-degree swing within the existing wrist limit.
The original POLYGON scene and earlier Sidekick review scenes remain available for comparison and rollback.
This is an isolated gameplay workbench. It does not replace the planet player's presentation or add inventory authority.

## Wardrobe boundary

The character milestone does not require every owned mesh to work automatically.
The accepted starting outfit avoids long loose garments and large accessory collision envelopes.

| Asset type | Current use |
|---|---|
| Rider fitted clothing, boots, and gloves | Starting outfit |
| Soldier armor and modular helmet | Reviewed wardrobe candidates; rigid plates still use source skinning |
| Mage integrated hood | Reviewed palette preservation; hood remains part of the combined mesh |
| Monk robe | Reviewed shin masking and cloth clearance; extreme poses remain restricted |
| Priest robe and Blacksmith apron | Fitting samples; source leg weights do not provide cloth separation |
| Pouch and backpack cup | Existing spring motion review; fit and clearance need loadout-specific checks |
| Cape | Existing spring-chain review; extreme contacts and self-collision are not guaranteed |
| Arbitrary packs and palettes | Require verified rig, palette, and seam mapping |

Do not describe long robes as universally supported. Their remaining failures need garment-specific topology, weights, or cloth simulation.
Do not apply a global outward offset to hide those failures. That would damage the fitted clothing silhouette.
These garments do not block interaction work with the Rider baseline.

## Conversion workflow

Use **Tools → Actors → Sidekick → Outfit Converter** for the imported Kingdom bodies.
Keep output revisions immutable. Converter changes can invalidate receipts without damaging existing playable prefabs.
Use a new revision for a new bake. Do not overwrite an accepted fit.
See [the converter workflow](2026-09-11-sidekick-outfit-converter.md) and [the wardrobe matrix](../research/2026-09-11-sidekick-wardrobe-matrix.md).

## Verification

Core, Planet, and Editor builds passed during this pass.
All 39 focused EditMode tests passed in job `1ce4b73fa6ef44578721e722e26ec44d`.
The suite covers both rigs' hand contacts, rig binding, wardrobe meshes, hood preservation, and converter cache protection.
The live Sidekick scene completed the panel swing with one confirmed contact, a final angle of 60 degrees, and a completed release.
Live walking, crouching, crawling, and swimming produced finite skinned vertices and the expected motor states. The fresh console contained zero errors.
The Mage comparison capture shows the complete hood around the native Sidekick head. The source-specific preservation rule keeps its cloth and trim.
The gameplay workbench is ready for interaction development with the Rider baseline. This does not certify every loose garment or planet integration.

The initial shared contact suite completed 39 cases with one Sidekick failure:
`Panel angle 60: error 0.03160899, rotation 13.40705` followed by `Expected: True` and `But was: False`.
The source panel sat too close for this rig's wrist orientation. A diagnostic moved its depth from 0.30 m to 0.35 m.
Contact error then measured `2.107342E-07` m with zero rotation error. The solver retained its 60-degree wrist limit and existing reach limits.
The fixtures now use each scene's actual panel distance. This is a station placement correction, not a tolerance change.

Evidence directory: `local-only/actor-performance/2026-09-10/sidekick-swap/`.
Files: `readiness-initial-tests.json`, `readiness-tests.json`, `readiness-gameplay.png`, `readiness-hood-aligned.png`, and `readiness-*-build.log`.
An initial live harness call disabled the review component and triggered its normal teardown. It reported `Runtime error: Object reference not set to an instance of an object`.
The corrected harness kept the owner enabled, reinitialized it, and completed the live checks. No runtime code change was needed.

## Next work

Start interaction work in `SidekickGameplayReview.unity` with the Rider prefab.
Use the existing hand contact and reach systems. Keep object interaction rules outside the character art converter.
Add equipment or loose garments to that baseline only after checking their contact envelope against the relevant interaction poses.
