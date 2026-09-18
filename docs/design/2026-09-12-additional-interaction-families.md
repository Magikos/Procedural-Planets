# Controls and equipment rack review

The review scene now has twelve targets. The new targets are a two-way lever and two separately selectable rack items.

## Behavior

- Face the lever and press E to pull it. Press E after completion to reset it.
- Face either rack item and press E to take it. The item follows the authored inspection animation.
- Press E near the rack to return the held item to its own slot. Returning requires the actor to face the rack within 1.1 metres.
- E or Escape cancels a return while retaining the item. A moved or disabled return slot also cancels an active return.

The rack demonstrates selection, hand attachment, and return. It does not add inventory UI, combat equipping, or weapon-specific locomotion. Both slots share the same TakeEquipment and ReturnEquipment definitions.

The lever shares the existing hinge behavior with chest and door targets. LeverOn and LeverOff use the owned Simple Activations clips. The Open and Close markers remain the authority for the lever state. Cancellation before a marker prevents that state change. Cancellation after a marker allows the committed hinge movement to finish.

ReturnAnchor supplies a rack slot without requiring a ground surface. The existing held-item movement, phase blending, markers, and release path perform the return. Moving the slot during a return invalidates the pending placement.

UseSourceHandRotation allows contact-position IK while retaining clip wrist rotation. Finger-grip IK requires an explicit rotation target, so this mode disables the procedural grip radius. A regression covers that combination.

## Existing interactors

The crate pickup/place and chair sit/stand cycles were rendered alongside the new targets. An experimental crate wrist change produced a worse grip and was removed. Their existing animation definitions remain unchanged. This pass does not claim a new crate locomotion animation or a redesigned chair-drag animation.

## Evidence

- Scene: `Assets/Scenes/Tests/SidekickInteractionReview.unity`.
- Manual 60 Hz Play Mode steps on the Sidekick Rider; 30 fps rendered capture.
- Review: `local-only/interaction-families/review.mp4`. Four six-second segments: crate, chair, lever, rack sword. The two new families are in the second half.
- Initial comparison frames and video: `local-only/interaction-families/first/` and `local-only/interaction-families/baseline.mp4`.
- Final EditMode job `b16004330b1a4a4db145463b45e5af51`: 50 passed, zero failed or skipped. Groups: SidekickInteractionReviewTests, InteractionPoseBlendTests, HumanoidContactIntegrationTests.
- Final Editor build: zero errors, 66 warnings. Log: `local-only/interaction-families/build.log`.
- Rack cancellation and retry: both slots retained the item on cancellation, then released it at the slot. Final position errors were 0.000216 m and 0.000327 m.
- Lever runtime cycle: 60 degrees after pull, zero degrees after reset.
- The first final-runtime attempt failed with `Interaction target must be finite.` The cause was positive GripRadius with a null rotation target. The corrected mode and regression test resolve that combination; the subsequent runtime cycle passed.

The fixture uses primitive supports and the existing sword model. Animation provenance is recorded in `Assets/Art/Interactions/Animations/SOURCE.md`. No vendor runtime scripts were imported.

Visual approval remains with Bryan. The broader continuity audit remains open.
