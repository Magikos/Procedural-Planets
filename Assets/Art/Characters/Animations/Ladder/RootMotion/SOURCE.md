# Ladder root-motion references

Source: `D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations/`.

The FBX files retain their original bytes. Their independent import identities use Generic animation.
The scratch Humanoid import reports a copied-avatar root mismatch. Generic import permits inspection of native transform curves.
Material import and looping are disabled. These references do not replace the in-place production clips.

| File | SHA256 |
|---|---|
| Traversal_Ladder_Climb_End_toPlatform.fbx | B948015D5F0F416D3293976BE244A8F652D2C8A49FC8C7E83264BE01D1B9163A |
| Traversal_Ladder_Climb_Down_Start.fbx | 362432D0253565C546321EAE38A808C74BD1FF08B107610088E730A27A8FFE78 |
| Traversal_Ladder_Climb_Up_Start.fbx | 45739FA1758B254D1E37CC400BAFCAF32FEA92144902EDDC0A3813528B1D4E4A |

Imported on 2026-09-13 to diagnose floating top exits and mounts in the first complete ladder capture.
The top-transfer references now provide sampled translation and yaw for the production authority.
Up_Start was added for the subsequent user review of the initial ground mount.
Its native root rises 0.3943135m and moves backward 0.0221006m. The retargeted trajectory preserves this motion and timing.
The former runtime mount moved forward 0.25m with no rise. That path did not match the authored foot arcs.
