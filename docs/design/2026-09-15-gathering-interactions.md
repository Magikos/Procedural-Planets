# Berry and wildflower gathering

Status: review revision 1, visual approval pending. Previous chopping and mining approvals remain intact.

## Shared implementation

The existing HarvestInteractionReview fixture now supports Collect, a named collection item, and a separate collected visual. HarvestService already resolves Collect yields. No new animation executor or per-animation authoring script was added. Each action grants one item at its collection marker. Cancellation before that marker grants nothing. The bush remains; only its berry cluster depletes. Wildflowers deplete as a small clump. Reset restores the collected visual and authority state.

Editable assets are Gather berries.anim and Gather flower.anim. Original FBXs and metadata came from the owned Survival_Animations and Loot_Anim_Set packs. Both retain their matching T_pose avatar dependencies. The existing original Android source rig is byte-identical to the Loot pack model (SHA256 D5CC6B720F8CDBE86E50C3CAEFAB0BD5B031A512A075555A0555FB8DB0AEFE2B).

Berry playback selects source 0–0.45 at native rate (2.25 seconds), then source 0.8–1 for recovery (1 second). Collection occurs at phase progress 0.58. Flower playback selects source 0–0.44 (1.98 seconds), then 0.72–1 (1.26 seconds). Collection occurs at phase progress 0.65. These ranges avoid repeated empty plucks after depletion. Positive blends join recovery and interruption through existing playback ownership. No new reach correction was added; props fit the authored reach.

Scenes: Assets/Scenes/Tests/BerryGatheringReview.unity and Assets/Scenes/Tests/FlowerGatheringReview.unity. Both reuse the existing humanoid interaction test room and approach behavior. PolygonGeneric bush and flower meshes supply the fixtures. Gatherable wildflowers.asset splits the existing flower mesh into stem and blossom material groups; it remains an editable mesh asset.

## Evidence

Bundle: local-only/animation-review/gathering-2026-09-15/index.html. Stable IDs GATHER-01 and GATHER-02. Current videos: berry-v1, flower-v1, berry-cancel-v1, flower-cancel-v1. Complete captures contain 270 frames at 30 Hz; cancellation captures contain 180. Each has side/rear views. A saved capture recipe and per-frame phase, hand-position, event and item metadata accompany the frames.

Both complete runs granted one item and returned to idle. Both cancellation runs granted zero and returned to idle. Berry target loss before contact granted zero. Eighty focused EditMode tests passed across harvest service, harvest interaction, interaction review and blending. Added exactly-once named collection and unavailable-target regression coverage.

Consecutive contact and recovery frames were inspected. Initial full clips continued picking after depletion; phase selection removes those extra plucks. berry-source and flower-source show full original-rig and raw-retargeted clips at their native rate, without procedural correction. They are not the production blend graph. A full production B capture with final corrections disabled remains absent.

## Remaining coverage and defects

Removal uses a short shrink effect, not visible attachment and transport into storage. Finger-specific grip, inventory pouch presentation, regrowth, environmental variants, and main-planet humanoid adoption remain open. The visual flower represents a clump, not a single botanical stem. This pass does not claim all gathering or other animations polished. Tool equip/stow remains separate missing work. User visual approval is required for the two current results.

## User approval

Bryan approved both revision 1 actions on 2026-09-15. This supersedes their pending visual status only; remaining coverage stays open.
