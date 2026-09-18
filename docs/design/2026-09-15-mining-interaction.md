# Mining interaction review

Status: implemented low-stone fixture; visual approval pending. Chopping revision 3 remains approved.

## Implementation

`Assets/Scenes/Tests/MiningInteractionReview.unity` reuses `HarvestInteractionReview`, `SidekickInteractionReview`, `HeldToolGrip`, and `ActorInteractionSession`. `ScatterInteraction.Mine = 3` preserves existing serialized values. HarvestService grants three Stone once after three fixture impacts. ToolTier.BasicPickaxe supplies one damage per hit. The fixture retains partial damage after cancellation and resets through its existing reset control.

The low mining clip comes from the owned Survival_Animations pack. Its separate T_pose avatar is required. The editable `Mine LowHeight.anim` plays at its native 2.5-second rate. Entry and recovery reuse the editable chopping preparation and exit clips. There is no per-animation C# authoring script.

The vendor pickaxe contains a CATRig/Circle authoring hierarchy. Instantiating that hierarchy under the hand reversed the working motion. The scene uses the imported static mesh directly. The left palm owns the tool transform. Existing bounded supporting-hand correction preserves the authored reach direction and elbow plane.

The low rock uses the existing PolygonGeneric rock mesh. Its fixture dimensions are 0.8 × 0.25 × 0.8 m. The actor approaches the stored stance instead of extending IK to reach. The contact marker runs at work progress 0.5333333, after the downstroke reaches the rock. Depletion leaves the strike surface in place until recovery, then smoothly reduces the fixture scale.

## Evidence

Bundle: `local-only/animation-review/mining-2026-09-15/index.html`.

- `low-v1.mp4`: 420 frames at 30 Hz. Approach, preparation, three strikes, recovery, depletion. Side and rear views.
- `stop-v1.mp4`: complete stop after one strike; one impact, zero Stone, final idle.
- `cancel-v1.mp4`: cancellation before contact; zero impacts, zero Stone, final idle.
- `source-retargeted.mp4`: 75 frames, one full work cycle. Original FBX on original rig at 1× beside raw retargeted editable clip at 1× without corrections. This is not the production blend graph and does not establish entry/exit B equivalence.
- Per-frame metadata stores phase, impact count, Stone, working-point position, and supporting-palm samples. Initial failed fits remain under `initial`, `fit`, and `fit2`; they are not current review output.

Consecutive contact frames and full-action overview were inspected. Final complete run: three impacts, three Stone, inactive session. Target loss before contact: zero impacts, zero Stone, inactive session. Eighty focused EditMode tests passed across harvest, interaction review, and limb solving. The shared three-impact test now checks both Wood and Stone yields.

## Remaining limits

Supporting-hand separation remains during repositioning. Pre-correction supporting-hand distance reached about 20 cm; this is not a final contact-error measurement. Acquisition and return use the existing prop blend, not a dedicated pickaxe pickup. Depletion scale is fixture feedback, not rock fracture. Audio, chips, ore types, persistence of partial damage across saves, and main-planet humanoid integration remain open.

`Mine MediumHeight.anim` is imported and editable but not wired or visually reviewed. Other animation families and the broader continuity audit remain open. Tests do not establish visual approval.

## Review revision 2 — supporting grip and medium mining

Bryan reported the right hand detached during preparation. The inherited axe support segment did not follow the pickaxe handle's slope. The pickaxe now uses mesh-derived support endpoints (-0.1486,-0.1102,-0.6259) and (-0.105,0.055,-0.05). The shared support request clamps correction distance rather than releasing the whole correction when that distance exceeds 12 cm. Existing blending still owns acquisition/release. The reported frame and complete action were rendered again.

Current low capture: grip-v3.mp4. Current medium capture: medium-v2.mp4. Both contain approach, three impacts, recovery and three Stone. Medium uses Mine medium stone.asset and MiningMediumInteractionReview.unity, with a 1.05 m high rock fitted to its authored strike. Stop/cancel captures updated to stop-v2/cancel-v2. The original source diagnostic remains LOW only; medium A/B diagnostics and medium-specific interruption capture remain unreviewed.

79 focused tests passed. Chopping regression was rendered through all three strikes and inspected at windup/contact; its approved reference remains preserved. No new arm/wrist flip was observed in that comparison. This is agent verification, not a new user approval.

Tool equip/stow is explicitly missing for both axe and pickaxe. Replace floating acquisition with authored ground pickup and/or hip/back draw when the corresponding storage model exists. Reuse shared pickup/equipment phases and attach only at actual hand contact; retain held ownership until the release marker during stow. Do not call the current prop blend a finished equip animation. Bryan requested recording this while prioritizing grip and continued coverage.

## User approval

Bryan approved review revision 2 on 2026-09-15: low rock grip-v3 and medium rock medium-v2. Preserve these visual baselines. This approval does not close the recorded equip/stow work, missing medium diagnostics, or broader animation coverage.
