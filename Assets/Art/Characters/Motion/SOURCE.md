Authored motion and performance assets. These are ours, not imported art.

Each asset is sampled by a project editor tool from a clip in ../Animations,
retargeted onto the baseline body in ../Baseline. They record phase intervals,
contact events, and hip displacement so the motor keeps world-space authority
while presentation replays the in-place pose.

Authoring tools and the assets they own:
- ActorPerformanceReviewAuthor -> Basic Performances.asset
- HumanoidReviewAuthor         -> Basic Vault Motion.asset (129 phases, from Basic Fence Vault.fbx)
- HumanoidRunningJumpAuthor    -> Authored Jump Performances.asset, Short Hop Performances.asset
- HumanLadderAuthor         -> Ladder Performances.asset and the Ladder * Motion.asset family,
                                  baked through LadderGaitAuthor
HumanoidStepUpMotionAuthor.Bake fills Authored Step Up Motion.asset in place
rather than creating it.

Basic Performances.asset selects intervals from the full Jump take. Events and
material import stay disabled on the source clips; the motor owns world
displacement.

The phase intervals are authored review values, not automatically measured
contact annotations.
