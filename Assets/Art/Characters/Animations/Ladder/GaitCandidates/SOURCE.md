# Authored alternating ladder family

Source: `D:/Unity/Explore Assets/Assets/Character Controller Pro/Demo/Models/Characters/CCP Character/CCP Character-ladder.fbx`.

The original FBX and its original meta were copied on 2026-09-13. SHA256: `0FF46B95511C257118FD7AAC21A7ADF87A0B1E6CA9950A269C842EE643069334`. No vendor scripts, controllers, or demo scenes were imported.

Unity verified valid humanoid clips. `Ladder Up` and `LadderBottomUp` last 1.25 seconds at 24 Hz. `Ladder TopUp` lasts 2.125 seconds. `Ladder Idle` lasts 2.5 seconds. The source climb alternates the leading hand. The earlier Universal climb repeated a left-leading catch-up step.

The publisher's `Demo/Animations/Character/LadderClimbing.controller` uses the same climb clip forward and backward. It also reverses the top and bottom transfers for descent. The current runtime reverses the same canonical Up clip for descent. Earlier editable reversed `.anim` copies were an intermediate implementation, not the current runtime direction mechanism. It uses native 1x playback; the publisher controller uses 1.5x for climbing.

`LadderGaitAuthor` samples native root travel on the production avatar. `ActorLadder` retains the sampled timing and fits cycle travel to the declared rung pitch. The review must measure full expected support intervals, including failed contact acquisition. Source selection does not prove contact fit.

An earlier unused ExplosiveLLC candidate set was removed after comparison. A later fast-ascent survey imported new Crafter and RPG candidates. Crafter is rejected; RPG and Leap remain unselected. Root removed those unselected imports after checking scene and library dependencies. Source paths and sample evidence remain in the local review bundle.


## Grip and fast-ascent selection, 2026-09-13

Rendered runs and remaining contact limits are recorded in the round 3 review bundle. Source measurements and editable assets do not prove final rung contact.

The editable CCP grip variant changes exactly 46 finger, wrist, and forearm-twist muscle curves. `LadderGripAuthor` blends them toward phase zero of the owned `AnimSeq_Traversal_Ladder_Climb_Up_Loop.FBX`. All body, arm-stretch, and root curves remain unchanged. The original CCP FBX remains intact. This is baked asset authoring, not another runtime pose writer.

The selected skipped-rung candidates are RamsterZ `Traversal_Wall_Climb_Up_LeftHand.fbx` and `Traversal_Wall_Climb_Up_RightHand.fbx`. Scratch source root is `D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations/`. Matching `AnimSeq_` originals come from its `In_Place_Versions/` folder. All four FBX files retain their original bytes.

Root verified the actual `Unreal Take` as 1 second. Generic root references move upward approximately 0.6734 m; retargeted humanoid travel is approximately 0.591 m. Root references use Generic imports for curve extraction. In-place originals use Humanoid imports and the existing T_pose avatar. Import metadata is therefore intentionally distinct from stale scratch importer settings.

Editable `Sprint LeftHand.anim` and `Sprint RightHand.anim` apply a fixed origin offset of Y -0.47755 m and Z +0.18 m. The assets retain native timing and root travel. Production plays both pose and root at 1.5x; the independent source reference remains 1x. Runtime contact verification is recorded in the round 3 review bundle.

`Sprint LeftHand Rung Grip.anim` and `Sprint RightHand Rung Grip.anim` derive from those origin-aligned clips. They blend only 40 finger muscle curves toward the same owned ladder-grip donor. The leading hand closes by phase .46. The following hand holds through .5, releases during its reach, and closes by .96. Wrist, forearm, arm, body, root, and contact-goal curves remain unchanged. The original wall-climb finger shape remains visible in source A.

Crafter was sampled and rejected. Its approximately 1.033 m travel over 0.833 s includes about 0.15 m supporting-hand drift. Its 0.3–0.4 m hand lead does not support a forced two-rung stride without excessive correction. RPG and Leap remain unselected.

### Verified SHA256 of selected original files

Each imported FBX hash below matches the corresponding scratch source bytes. This check does not claim identical importer metadata.

| File in this folder | SHA256 |
|---|---|
| `CCP Character-ladder.fbx` | `0FF46B95511C257118FD7AAC21A7ADF87A0B1E6CA9950A269C842EE643069334` |
| `Traversal_Wall_Climb_Up_LeftHand.fbx` | `1E3DA3A38AE0588274450E96E93B39DE955C93E31AE4581CB8061FFAA9F92059` |
| `Traversal_Wall_Climb_Up_RightHand.fbx` | `580AAD194EF339B78BEC9B97A9D7915A036D1A140109AB402AF76026266A3650` |
| `AnimSeq_Traversal_Wall_Climb_Up_LeftHand.FBX` | `30BD9DFF49D64DB9EFEA4862A721DD1F8B50DE6123C8E462732039DD0D2C3E29` |
| `AnimSeq_Traversal_Wall_Climb_Up_RightHand.FBX` | `8EB48C3CAFC927247F98E4A9076B218828E0001506932E903656FB43FEA3E249` |
