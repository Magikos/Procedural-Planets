# Authored ladder clips

Copied 2026-09-13 from owned Ultimate Traversal Anims 2.0, product 217762.
Selected in-place originals support controller-owned ladder travel. No artist curves or importer settings were changed.
Unity import, retargeting, full-phase contact, and visual quality remain unverified.

Source root: `D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations/In_Place_Versions/`.
Each destination retains its original filename. FBX and metadata copies are byte-identical to their sources.

| Filename | GUID | FBX SHA256 | Metadata SHA256 |
|---|---|---|---|
| `AnimSeq_Traversal_Ladder_Climb_Up_Start.FBX` | `62fd18cc2c6031545857bb6b40f77ab1` | `3E913A9D1A229D39047301B8094A1FF859790D277FD3EC8FB923BAFFD6F68BF2` | `E28D22375E899ECD546D24E05933050D2F0EAD8A5EFB530173610FD9B867C25D` |
| `AnimSeq_Traversal_Ladder_Climb_Up_Loop.FBX` | `4ce9324229540044f93836182c387b17` | `9A702F8B100C28233D774683E132F996DAFF325F1491B071714A007C18B42721` | `FA09690CA242950040C066B7B43E32F1D6308AC82AF60F1288A6322C41D78DD0` |
| `AnimSeq_Traversal_Ladder_Climb_Up_Cancel.FBX` | `e95ac204a614c114da8396bd855736ad` | `A5F20C8C91DB1AB57AB4C395E5F65A37598BBC673D26E746F7D55F718D92ED0A` | `2841BB817BFDA0F0E1C59F9B1BD463CB1660272FE19D40AD879D0497C1939D22` |
| `AnimSeq_Traversal_Ladder_Climb_Idle.FBX` | `099b9f979f5e0d6479f413aa12fa5883` | `D229C731015F5D613CEF163291D6E199BCD5E1C3DD64169995C034F432F17DB3` | `826F1D3DC618AB40515C1504662C569F6C15013E1BECA2E2E44712EFA556DA4D` |
| `AnimSeq_Traversal_Ladder_Climb_End_toPlatform.FBX` | `3afb34b07f8cf074da32aab55a057851` | `12367128AFE647FB9EB14B8D93B3E8DA9BE06CBB27B5CA127552D6DC099F79AF` | `DE9BB72EAA8DFA8FDA959230218921F81D9E42EFB4C0066D71CFEE5505F50834` |
| `AnimSeq_Traversal_Ladder_Climb_Down_Start.FBX` | `04b07415a40c7544483e76668f8fe3e0` | `FC3198BAEE99F1622A8F2C1F48F726ECD37A777F91C8B006C1E3E7EDF265586D` | `BCCDA5E9F5B5E737020076BC33AA4A6B5A5FA8378F1E77245F4D985647F13DF9` |
| `AnimSeq_Traversal_Ladder_Climb_Down_Loop.FBX` | `0c4400f6edd7fa84d887962cd24a48b3` | `8E0A370A391789B61A22B9BFA7301F50B8D72E47DF829A7ADFD970828A4EDA6C` | `99B48DF53475C0144BB9B7039F91374AA1A08AA7DE45EC85093F7C37C4422E66` |

## Dependency mapping

All seven source GUIDs were absent from project metadata before copying.
Each importer uses Humanoid (`animationType: 3`) and copies avatar fileID 9000000, GUID `2091e1aca366db648b573fbcebc047c6`.
That identity already exists at `../ReviewCandidates/T_pose.FBX`. No second avatar was copied.
The source metadata contains no other external GUID dependencies.

## Phase selection and timing

| Suffix | Intended candidate phase | Source frames |
|---|---|---|
| Up_Start | Bottom mount | 0–30 |
| Up_Loop | Ascend cycle | 0–20 |
| Up_Cancel | Bottom release candidate; preview required | 0–20 |
| Idle | Ladder hold | 0–110 |
| End_toPlatform | Top dismount | 0–30 |
| Down_Start | Top mount/descent entry | 0–65 |
| Down_Loop | Descend cycle | 0–20 |

Every in-place source importer sets `loopTime: 0`, including loop-named clips.
Configure runtime repetition deliberately, or record any required Unity importer change separately.
Source frame rate, rung count, vertical cycle displacement, contact phases, and Up_Cancel suitability require Unity sampling.
Do not derive cycle distance from names or frame counts. No ladder step distance is claimed by this import.

Corresponding authored root-motion originals remain in the parent scratch folder:
`D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations/Traversal_Ladder_Climb_<suffix>.fbx`.
They retain root-motion evidence for later sampling. The root Down_Loop importer selects frames 30–50; its in-place counterpart selects 0–20.
Catalog section 11.7 historically reports root-motion copied-avatar errors. The in-place family historically imports clean.
That historical report does not substitute for current Unity validation.

No vendor scripts, controllers, scenes, materials, or models were imported.
All source assets remain intact. No production scene or performance assignment changed in this copy pass.

2026-09-13 correction under visual review: ladder in-place importers bake root position Y and XZ into pose (loopBlendPositionY=1, loopBlendPositionXZ=1). Original FBX bytes remain unchanged. Original imports extracted ~0.34m of authored crouch from the top transfers, which runtime discarded with applyRootMotion=false. Root rotation remains extracted for authority-driven facing. Native original root trajectories remain separate reference assets.
XZ origin now uses the authored root (keepOriginalPositionXZ=1), avoiding a different center-of-mass origin for each phase.
Native generic root translations use the production/source Animator.humanScale ratio when baked into traversal assets. Measured production=.9421884, original T_pose=1.07354426 (ratio~.87765); native timing and root yaw remain unchanged.
Root orientation now also uses the original authored root (keepOriginalOrientation=1). Body-derived orientation extraction rotated the top-mount body lean by approximately90degrees relative to its original root. Runtime applies the original root yaw, so both extraction and authority must share that frame.

## Final-step production candidate

`Ladder Final Step.FBX` is the original Traversal_WalkForwardStartAndStop FBX with an editable importer window at frames 84–108 (2.8–3.6 s, native 30 Hz). Looping is disabled. Horizontal root motion is extracted for the existing collision authority; original root orientation and authored vertical body motion remain. `RootMotion/Traversal_WalkForwardStartAndStop.FBX` retains the full original track with a Generic importer for independent root sampling. Original artist curves are unchanged. The original full-file SHA256 is recorded in EntryCandidates/SOURCE.md.

The candidate start was moved four original keys earlier to frame 80 (2.666667 s); end remains frame 108 (3.6 s). This retains more of the lifted left-foot swing during the required entry blend. At frame 84 the swing was already descending, which risked hiding its clearance during the blend. Native candidate duration is now 0.933333 s.

The final-step `.anim` variant now normalizes only its constant trimmed XZ origin in RootT, using the original Generic root position divided by source-avatar humanScale. The FBX retains original-position extraction. This replaces the rejected body-based-root importer experiment, which caused supported foot drift. Key times, tangents, muscle curves, and vertical body motion remain unchanged. Runtime verification is required before accepting the variant.

The current derivation bakes XZ into the imported body, then subtracts the original sampled Generic root track from RootT.x/z. The motor adds the same native track. This preserves body motion relative to the artist's original root. Only the adapted XZ RootT curves use dense editable 60 Hz keys with finite-difference tangents; original muscle curves, their key times/tangents, vertical RootT, and root orientation remain unchanged. Constant-origin-only normalization and body-based extraction were rejected by v2/v3 support evidence.

The v5 candidate uses original frames 76–94 (2.533333–3.133333 s, duration 0.6 s). Native support samples select both boundaries: the starting right sole is 0.21334 m ahead of its root, matching the outgoing walk sample at 0.21328 m. The ending left sole is 0.07686 m ahead, close to Up_Start at 0.08356 m. This removes the late settling section that moved the body over the planted foot before mounting. Lateral and height differences still require rendered validation. HumanLadderAuthor owns the shared source-time constants used for baking and reference playback. Importer frame bounds must match those constants.

After disabling implicit Unity clip FootIK, v7 reproduced the independent adapted source at full weight (right-sole Z error about 0.1 mm). The outgoing walk support also changed. The v8 source window therefore starts at frame74 and ends at94 (native duration0.666667 s). Frame74 right-sole Z=.274428 m matches the unmodified outgoing walk at .2744 m. Source right-sole X=.10855 m versus outgoing .0778 m leaves about3 cm lateral mismatch. Its source sole height is .07548 m before motor ground offset, so height and contact fitting remain under review. No IK limit changed.

Global FootIK=false was rejected after jump regression. Matched original/adapted FootIK=true reconstruction passes at0.181 mm; body-relative foot goal curves need no root subtraction. The current v9 candidate uses frames72–94 (native0.733333 s) and restored native foot-goal retargeting. FootIK=true frame72 provides the smaller combined support mismatch among sampled neighboring starts: source right X=.1201,Y=.0711,Z=.2633 versus outgoing X=.0587,Y≈.0204,Z=.2388. After source-origin normalization, fore/aft mismatch is about2 cm; width and height still differ by6.1/5.1 cm. This remains a trial awaiting rendered support review, not an accepted fix.

The retained v10 ApproachStep blend is.08 s; other ladder phase blends stay.18 s. Rendered forward/back support excursion decreases, but an8.38 cm sideways entry shuffle remains. The clip window and native rate stay72–94 at30 Hz. This is a documented improvement with an open visual defect, not completed animation polish.
