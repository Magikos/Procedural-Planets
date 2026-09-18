# Sidekick interaction room: round one

Scene: `Assets/Scenes/Tests/SidekickInteractionReview.unity`.

Current interaction behavior and authoring instructions: [Sidekick interaction sequences](2026-09-12-sidekick-interaction-sequences.md).
The round-one record below describes the earlier reach-only fixture.

## Purpose

Use the accepted Sidekick Rider character to study object contact, approach spacing, and grip heights.
This room reuses `HumanoidAnimationPrototype` and its shared motor, camera, hand-contact solver, and animation view.
`SidekickInteractionReview` owns target selection and explicit prop-motion previews only.

## Targets

| Group | Contents | First test |
|---|---|---|
| Chest | Chest with native lid and latch pivots | Low latch reach and lid clearance |
| Door | Wooden door, hinge, and frame posts | Handle reach and swing clearance |
| Table | Tankard, bottle, book, and small key | Tabletop reach, grip sizes, and far-side clearance |
| Ground | Book lying on the floor | Standing/crouched reach and later bend motion |
| Carry | Small wooden crate | Later two-hand lifting and carrying |
| Chair | Wooden chair | Seat height and later sit/stand transitions |

The room provides nine named targets and approach positions.
The existing movement scenes retain their water behavior and control panel.
The new scene sets `EnableWater=false` and `ShowControls=false` on its own controller.
These are additive fields with true defaults. No existing fields or asset identities were renamed.

## Controls

Enter Play mode. Walk toward an object to select a nearby target in front of the actor.
The panel displays the current target. **Visit** remains an optional approach-position shortcut; it does not pin selection.
Use WASD to adjust the position, Shift to run, RMB to look, and Tab to switch camera modes.
Use E or **Reach toward contact** to exercise the existing right-hand solver.
Target selection uses a 1.1 m horizontal range, a forward cone, and a blocking-collider check.
Changing targets, turning away, or leaving range releases the previous reach. Inactive targets are excluded.
Use Ctrl or **Crouch** for lower targets. The contact status distinguishes reachable and rejected poses.
Use **Preview open / close** for the selected door or chest lid.
Use R or **Reset room** to close the props and restore the selected approach position.
Leaving Play mode restores the authored room.

Prop previews do not assert hand contact and do not grant gameplay actions.
Pickup, inventory transfer, carrying, and sitting are the next behaviors to implement in this fixture.
The low targets deliberately expose the need for body positioning and bend motion; the fixture does not stretch the arm to fake contact.

## Verification

Core, Planet, and Editor builds passed. The dry-room regression test passed in job `42410c50d5d0464ea52c536ca8c03a70`.
All nine live target visits retained grounded movement, selected the correct hand target, and avoided the water state.
The chest preview reached its authored 105-degree opening.
The door preview reached 90 degrees. Reset restored both hinges with zero measured rotation error.
The first table approach was too far from the tankard. The reviewed placement moves the tankard toward the near edge and aligns the actor forward.
The revised door and tankard approaches produced confirmed hand contact without changing solver limits.
Measured contact errors were `2.665601E-07` m for the door and `1.192093E-07` m for the tankard.
The fresh live console contained zero errors. Visual approval remains with Bryan.
Reopening the scenes confirmed that the original gameplay scene retained its controls and water behavior.
The new scene reopened cleanly with nine targets, zero missing scripts, and zero missing materials.

Evidence directory: `local-only/actor-performance/2026-09-10/sidekick-swap/`.
Captures: `interaction-room-overview.png`, `interaction-table-reach.png`, and `interaction-controls.png`.
Logs: `interaction-*-build.log`, `interaction-tests.json`, and `interaction-graphify.log`.

## 2026-09-12 targeting correction

E previously toggled reach only for the last Visit selection. The review now resolves the actor's current nearby target every frame and again on E.
Two EditMode tests passed in job `dc9d57f6151b4698ad405ad11d3c9f63`, including movement between targets without Visit, stale-reach release, and behind/inactive/distant rejection.
Live position changes selected the door, tankard, ground item, and chair without calling Visit. Turning away cleared the target and reach. The console contained zero errors.
Core and Planet builds passed after restoring the missing NuGet assets cache. The initial no-restore attempts reported `error NETSDK1004` for missing `project.assets.json` files.
Build logs are `targeting-core-restored.log` and `targeting-planet-restored.log` in the evidence directory.
This correction changes target selection. It does not add low-body bending, pickup, carrying, or inventory transfer.
