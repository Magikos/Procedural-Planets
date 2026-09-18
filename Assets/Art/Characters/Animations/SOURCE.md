Humanoid animation clips shared by every humanoid character: the player, the
modular kit, and all NPCs. Provenance below is the record of what each clip was
adapted from. No vendor scripts, shaders, controllers, or presets were imported.

The sampled Motion and Performances assets derived from these clips live in
../Motion. The baseline body they retarget onto lives in ../Baseline.

HumanoidIdle.fbx, HumanoidWalk.fbx and HumanoidRun.fbx come from the Unity
standard character set at ECM2/Shared Assets/Models/UnityCharacter/Animations/.
Their AssetOrigin metadata attributes them to "Obi Rope", which is a stale
attribution from a multi-package import session, not their real source.

Owned swim animation art: Fantacode Studios / Swimming System / Animations.
Source: D:/Unity/Explore Assets/Assets/Fantacode Studios/Swimming System/Animations/
Imported: Surface Idle.fbx, Slow Swim.fbx, Underwater Idle.fbx, Underwater Swim.fbx.
Source events were removed. No vendor runtime or water effects were imported.

Owned jump art: Fantacode Studios / Third Person Controller / Animations / Locomotion / Jump.fbx.
On 2026-09-09, Jump Full.fbx was copied from the existing local Jump.fbx and imported with its complete source take (frames 20..74).
The earlier Jump.fbx import remains unchanged at frames 30.5..36.7.
Basic Performances.asset selects intervals from the full take. Events and material imports are disabled; the motor owns world displacement.
The phase intervals are initial review authoring, not automatically measured contact annotations.

Owned directional swimming art (2026-09-09): Kevin Iglesias / Human Animations.
Source: D:/Unity/Explore Assets/Assets/Kevin Iglesias/Human Animations/Animations/Female/Movement/Swim/SwimMovement/
HumanF@Swim01_Left.fbx, HumanF@Swim01_Right.fbx, and HumanF@Swim01_Backward.fbx were copied as Swim Left.fbx, Swim Right.fbx, and Swim Backward.fbx.
Their Humanoid import settings were retained with new GUIDs. No vendor scripts or controllers were imported.
Visual sampling confirms a reclined backward sculling motion; this is not an alternating overhead competitive backstroke.
Basic Wade.anim derives from the existing owned HumanoidWalk clip. HumanoidWaterAnimationAuthor changes muscle curves for arm position, leg effort, and spine sway.
The source walk remains unchanged. The derived clip shares its stride phase with walking; motor resistance slows both motion and animation.

Directional walk refinement (2026-09-09): Walk Forward.fbx and Walk Backward.fbx use Kevin Iglesias HumanF@Walk01_Forward/Backward from Female/Movement/Walk.
Walk Left.fbx and Walk Right.fbx use HumanF@StrafeWalk01_Left/Right from Female/Movement/Strafe/StrafeWalk.
Source root: D:/Unity/Explore Assets/Assets/Kevin Iglesias/Human Animations/Animations/
Measured cycle offsets align ankle-height support phases to Walk Forward: left 0.0625, right 0.05, backward 0.625. Measurements used 80 samples per cycle on the review avatar.
The review assigns this family. Earlier HumanoidWalk and ExplosiveLLC directional assets remain available.
Basic Wade.anim was rebuilt from Walk Forward with its existing asset GUID preserved.

Eight-way locomotion (2026-09-09): Walk ForwardLeft/ForwardRight/BackwardLeft/BackwardRight.fbx were copied from Kevin Iglesias Female/Movement/Strafe/StrafeWalk, HumanF@StrafeWalk01 variants.
Measured cycle offsets against Walk Forward are 0.0625, 0.025, 0.0625, and 0.05 respectively. The graph uses the dedicated clip at each exact diagonal and blends adjacent directions between them.
Surface Paddle.fbx is Kevin Iglesias Female/Movement/Swim/SwimMovement/HumanF@Swim01_Forward.fbx. Pose inspection shows its breaststroke motion; Shift selects it for faster forward surface swimming.
Normal forward surface movement shares Surface Idle's upright tread/paddle motion and synchronized sampling. Existing underwater and directional strokes remain available.

Relaxed walking arms (2026-09-09): Relaxed Walk Forward.fbx comes from
D:/Unity/Explore Assets/Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack/Animations/Relax/RPG-Character@Relax-Walk-Forward.FBX.
HumanoidWalkArmAuthor derives eight Relaxed Walk *.anim assets from the existing Kevin Iglesias directional walks.
It replaces only shoulder, arm, forearm, hand, and finger muscle curves. It preserves the original leg/root curves and cycle offsets.
The author measures donor alignment from both feet over 128 samples. Donor phase shifts are forward/forward-right 0.7734375;
left/forward-left/backward-left 0.7890625; right/backward/backward-right 0.78125.
The review assigns these derived clips. No vendor runtime code was imported. Basic Wade remains its separately authored motion.

Crawl transitions and backward motion (2026-09-09): Kevin Iglesias Human Animations, Female/Movement/Prone.
Crawl Enter/Exit use HumanF@Prone01-Begin/End. Basic Crawl Idle uses HumanF@Prone01_Idle01.
Basic Crawl Forward and Crawl Backward use Crawling/HumanF@Prone01_Crawling01_Forward01/Backward01.
Human Motion Reference.fbx comes from Kevin Iglesias/Human Animations/Models/HumanF_Model.fbx.
The five clips copy its Humanoid avatar. Creating an avatar from each prone clip produced invalid exit retargeting.
Vertical motion stays baked in the pose with original Y retained. Enter/exit also retain baked horizontal motion to match the prone endpoints.
The motor remains the authority for actor displacement. Source events and material import are disabled.
Earlier ExplosiveLLC Crawl Idle/Forward assets remain available.

Lateral crawl (2026-09-09): Crawl Left.anim and Crawl Right.anim derive from Basic Crawl Forward.fbx.
HumanoidCrawlStrafeAuthor rotates sampled hand/foot strokes and elbow/knee bend paths into lateral motion.
It uses the shared LimbPoseSolver during baking, bounds lateral reach, and reduces residual fore/aft travel.
Body/root/spine curves and source cycle timing remain intact. These are generated variants, not vendor lateral clips.
The runtime blends four crawl directions with synchronized cycle sampling. Baking adds no runtime IK solver work.

Lateral crawl retargeting correction: the derived clips also rewrite LeftFootT/Q and RightFootT/Q.
Keeping the source goals overrode the edited leg curves with the original forward trajectory.
The baker converts solved foot deltas into normalized body space, and preserves quaternion continuity.
Lateral feet use contact orientation and a foot-size-based inward clearance.

Basic fence vault (2026-09-09): Universal_Traversal_Anims, Art/Animations/Traversal_Fence_91cm_JumpOver.fbx.
Imported as Basic Fence Vault.fbx. Traversal Reference.fbx comes from the same pack's T_pose.FBX.
The clip copies that reference Humanoid avatar. Source material import, events, and looping are disabled.
Lazy Vault Right.fbx is the comparison candidate Traversal_Movement_LazyVault_Right.fbx; the review does not select it.
Source project: D:/Unity/Explore Assets/Assets/Universal_Traversal_Anims/Art/Animations.
HumanoidVaultMotionAuthor samples the retargeted basic clip into Basic Vault Motion.asset at 129 phases.
The shared traversal controller applies its horizontal hip displacement, obstacle fitting, and landing correction.
Presentation removes that same sampled displacement from the clip, including its outgoing blend.
The baked torso/head capsule supports collision checks. It does not cover every limb or mesh vertex.
No vendor runtime code was imported for this change.

Subdued walk replacement (2026-09-09): Neutral Male Walk.fbx comes from
D:/Unity/Explore Assets/Assets/Kevin Iglesias/Human Animations/Animations/Male/Movement/Walk/HumanM@Walk01_Forward.fbx.
Male Motion Reference.fbx copies that pack's male model and supplies its Humanoid avatar. GUIDs are independent of the scratch project.
Source events, external avatar-mask references, and material import are disabled.
The eight Relaxed Walk *.anim assets now use this donor for spine, chest, shoulders, arms, and hands.
Their original directional root, leg, and foot-goal curves remain unchanged.
The baker scales torso and shoulder oscillation to 40% around each cycle mean. Arm swings keep the donor's motion.
Measured donor phases: forward/backward/forward-right 0; left/forward-left/backward-left 0.015625; right/backward-right 0.0078125.
This supersedes the earlier ExplosiveLLC upper-body donor selection. Asset GUIDs and scene references remain unchanged.

Stair gait clips (2026-09-09): Stair Walk Up.fbx and Stair Walk Down.fbx come from
D:/Unity/Explore Assets/Assets/EverydayMotionPack/Motion/02_Move/@male_move_walk_stair_stepup.FBX and @male_move_walk_stair_stepdown.FBX.
Stair Motion Reference.fbx comes from EverydayMotionPack/Model/Chatacter/male.FBX.
Fresh GUIDs and remapped Humanoid avatar references isolate these imports from scratch.
Source events, material import, and external avatar masks are disabled. Both cycles retain their source loop/root settings.
Retargeted sampling showed stable local hip positions across each loop, with source forward/vertical travel in root motion.
The shared motor remains the actor-position authority; the Playables view samples in-place poses.
These are forward-facing ascent/descent cycles. Backward and sideways movement retain existing directional locomotion.

Artist locomotion fidelity (2026-09-12): the Human interaction review now uses the original imported Walk *.fbx motions instead of Relaxed Walk *.anim arm-composite variants.
The derived variants remain available but are not selected by this review.
Eight Run *.fbx files were imported from Kevin Iglesias Female/Movement: Run/HumanF@Run01_Forward and Backward, plus Strafe/StrafeRun/HumanF@StrafeRun01_{Left,Right,ForwardLeft,ForwardRight,BackwardLeft,BackwardRight}.
Source GUIDs and the existing female Humanoid avatar reference were preserved. No vendor code or controllers were imported.
Root displacement remains motor-owned. Clips loop, root XZ/rotation are baked, source events and material import are disabled.
A 64-sample ankle-height comparison against Walk Forward selected cycle offsets 63/64 for forward, 42/64 for backward, and 2/64 for the other six directions.
No muscle curves were rewritten. The shared runtime blends separate directional walk and run clips according to speed.
