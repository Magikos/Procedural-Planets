# Ladder urgency candidates

Source: D:/Unity/Explore Assets/Assets/Plugins/Threepeat/ParkourAnimations/animations/mantle/high/
Pack: Parkour Animation Set

Two candidate clips for fast ladder and mantle exits:
`mantle-high-3m-climbup-run-to-run.fbx` and
`mantle-high-climbup-sprint-to-sprint.fbx`, each beside the
`<clip name> Motion.asset` our sampler baked from it.

These are candidates, not shipped motion. They were imported to compare against the
authored ladder climb and have not been adopted. The accepted ladder baseline is
revision 7 (V29), which does not use them.

The two FBXs keep the pack's filenames on purpose. Every clip in the ladder tree
is source-named, and an animation FBX's filename is also the AnimationClip
sub-asset name that controller states and our baked `Motion.asset` bind to.
Renaming two of several hundred buys no consistency and risks those bindings.
The animation tree gets one rename pass of its own or none at all.

Not imported: no pack scripts, controllers, demo scenes, or avatars.
