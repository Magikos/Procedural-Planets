# Interaction and animation coverage — 2026-09-15

Status: Initial source and asset inventory. No runtime changes or new visual validation.

Follow-up: Bryan authorized implementation. The [first chopping technology slice](../design/2026-09-15-chopping-interaction.md) adds repeatable work and a playable fixture. Findings below preserve the pre-implementation inventory.

Tree: `harvest-vertical-slice`, dirty working tree above `d1e0f62`. Preserve unrelated changes and accepted animation baselines.

Bryan's current priority is missing interaction technology and action coverage. Pause existing-animation polish and the manual authoring trial. Interpret “bean walking” as beam walking. Keep editable animation assets and prop fitting in the implementation requirements. Do not add individual C# animation generators.

## Evidence and limits

Inspected actor traversal, interaction definitions/session, performance selection, review marker handling, harvesting, imported interaction assets, and selected owned clip filenames. Graphify returned the existing animation review; current source supplied implementation evidence.

This is a family-level inventory, not a claim that every existing interaction works in the main game. File presence does not prove scene wiring, retargeting, visual suitability, or gameplay completion. No Unity playback or tests ran in this pass. Creature coverage uses the existing review record and was not re-audited.

## Coverage matrix

| Family | Current support | Missing or unverified work | Priority |
|---|---|---|---|
| Ordinary movement, stance, jump, swimming | Existing humanoid playback and motor; focused jump reviews exist | Broader start/stop, terrain, stance and water coverage remains open | Preserve; no polish now |
| Ladder | Dedicated `ActorLadder` and `LadderInteraction`; accepted focused baseline | Wider geometry and gameplay integration are not established by that approval | Preserve |
| Ledge grab, hang, climb, drop | `ActorTraversal` supports these states | Sideways hanging travel, continuous grip relocation, corners and end handling | 2 |
| Beam walking | Owned balance clips; no dedicated route found in actor traversal | Narrow support detection, constrained travel, entry/exit, reversal, fall handling | 2 |
| Ledge walking | Owned ledge-walk clips; no dedicated route found | Wall clearance, foot support, lateral travel, corners, safe exit | 2 |
| Rope climbing | Owned pole-climb candidates | Rope-specific grip suitability, route, attachment, entry/exit and release; flexible rope behavior is a separate scope | 3 |
| Doors, chest, cabinet, controls | Existing definitions and review target/marker handling | Main-game adoption and wider prop fits need verification | Preserve |
| Pickup, carry, place, sit, equipment | Existing definitions and review behavior | Heavy-crate artistic revision remains unfinished; production adoption needs verification | Defer polish |
| Chopping | Harvest service grants wood; owned start/loop/exit clips | Animated approach, equip/grip, timed strikes, repeated-hit state, safe cancellation | 1 |
| Mining | Owned low/medium pickaxe clips | Resource-specific yield and damage path, tool impact, target fit, depletion | 1 |
| Berry and flower gathering | Generic Collect yield and owned foraging/harvest clips | Distinguish fruit from whole-plant collection, target heights, contact-time reward, depletion policy | 1 |
| Crafting and cooking | Definitions for hammering, grinding, pouring, campfire building/lighting, roasting and stirring | Verify station wiring, recipe costs/results, repeated work and interruption semantics | 1 after gathering |
| Fishing | Owned character and fishing-prop clips | Cast/wait/bite/pull/catch/cancel logic, rod/line ownership and reward timing | 3 |
| Sword, shield, bow, dodge | Actor attack data and creature strike service exist; owned combat candidates | No complete player weapon/action path found in inspected source; equip, attack windows, blocking, aiming/projectiles, dodge collision and recovery | 4 |
| Skill variants | Performance library selects proficiency ranges and conditions | Meaningful alternate clips; interactions currently snapshot a single definition rather than selecting skill variants | Extend with first useful second variant |
| Creatures | Existing gait, drink, sleep and other sampled reviews | Species/action coverage and authored swimming remain open in the prior review | Separate follow-up |

## Findings

### A1. ARCH — Gathering needs an animation-to-gameplay connection

`Assets/Scripts/Planet/Scatter/HarvestInteractor.cs:67` calls the harvest service directly. `HarvestService.cs:65` resolves harvest effects without waiting for a character action. `DefaultNodeHp` is 1; there is no persistent per-node damage accumulator in this service.

Reuse `ActorInteractionSession` and its authority-side markers. Revalidate the target at impact. Apply one effect per intended impact, even during frame hitches. Cancellation before impact must not grant resources. Cancellation after impact must not repeat the reward.

Do not treat a repeating clip as repeated work automatically. `ActorInteractionSession` sends its marker once per phase; a waiting loop does not emit one marker per cycle. Extend the existing phase/session behavior only as required by repeated work.

### A2. ARCH — Traversal has discrete ledge actions but lacks continuous surface travel

`Assets/Scripts/Game/Actors/ActorTraversal.cs:4` lists `None, StepUp, Vault, JumpGrab, Hanging, ClimbUp, DropToHang`. No beam, ledge-walk, shimmy or rope route was found in the scoped actor implementation.

Start with straight hanging movement. Reuse collision and traversal authority. Add continuous hand support validation and blocked-end handling before corners. Beam and ledge walking need foot support, not the hanging solver copied under another name.

### A3. ARCH — Existing interaction content must not be mistaken for full gameplay

There are 31 assets in `Assets/Art/Interactions/Definitions`. `SidekickInteractionReview.ApplyMarker` handles tools, doors, pickup/place and seating, calls campfire handling, and publishes `InteractionMarker`.

Use this behavior as the starting point. Trace main-game consumers before moving it or building another executor. Recipe transactions and resource effects require separate verification; a crafting definition alone does not establish either.

### A4. ARCH — Skill selection exists, but interaction selection is incomplete

`ActorAnimationPerformanceLibraryData.Select` supports action, rig, proficiency and condition. `HumanoidAnimationView` uses proficiency. `ActorInteractionPlan` constructs one performance directly from its definition. The prototype's ladder selection also uses the default proficiency argument.

Extend these existing selection paths when an action receives its first skill variant. Preserve compatible phase names, contact intent and effect timing. Do not create a parallel variant framework.

### A5. META — Authoring remains an unproven workflow

Keep original clips, editable project variants and final runtime playback distinct. Very Animation is installed, but the current trial does not prove a reliable automated editing workflow.

For each new action, record stance distance, prop dimensions, grip orientation, contact intervals and effect timing in existing action/contact data where possible. Move or turn the actor before reaching. Reject incompatible geometry rather than stretching the pose.

If the source needs substantial posture changes, save an editable variant. Shared editor tooling may edit curves or solve poses, but no dedicated per-animation generator should overwrite those edits. First prove one prop-fitting edit with the first new action; do not build a general animation studio ahead of that need.

## Owned clip leads

All paths below were file-verified, not previewed. Root: `D:/Unity/Explore Assets/Assets/`.

| Need | Relative source path or verified family | Limitation |
|---|---|---|
| Chop | `Survival_Animations/Animations/Survival_TreeChop_Start.FBX`, `Survival_TreeChop_Horizontal_Loop.FBX`, `Survival_TreeChop_Exit.FBX` | Check axe and trunk height |
| Mine | `Survival_Animations/Animations/Survival_PickAxe_LowHeight.FBX`, `Survival_PickAxe_MediumHeight.FBX` | Check pick head trajectory |
| Berries | `Survival_Animations/Animations/Survival_Foraging_BerryBush.FBX`, `Survival_Foraging_BerryBush_Low.FBX` | Match foliage contact height |
| Flowers | `Loot_Anim_Set/Animations/Loot_FloorPickUp_Kneel_HarvestItem.fbx` | Check plant reach and return |
| Beam | `Universal_Traversal_Anims/Art/Animations/Traversal_BalanceBeam_Forward_Start.fbx`, `Traversal_BalanceBeam_Forward_Loop.fbx`, `Traversal_BalanceBeam_Forward_End.fbx` | Match motor speed and support width |
| Ledge walk | `Universal_Traversal_Anims/Art/Animations/Traversal_LedgeWalk_Left_Loop.fbx` and left idle/end/cancel files | Full directional set needs selection |
| Hanging travel | `Universal_Traversal_Anims/Art/Animations/Traversal_Ledge_Climb_Left.fbx`, `Traversal_Ledge_Climb_Right.fbx` and corner files | Match grip spacing and displacement |
| Rope candidate | `Universal_Traversal_Anims/Art/Animations/Traversal_Pole_Climb_Up.fbx` | Pole motion is not proof of suitable rope motion |
| Fishing | `Survival_Animations/Animations/Survival_PrimitiveFishing_Loop.FBX`, nibble/bite/pulling files | Separate animated rod files also exist under Models |
| Dodge | `Kevin Iglesias/Human Animations/Animations/Male/Combat/HumanM@Dodge01.fbx` | Check displacement and recovery |
| Weapons | Catalog sections 6.4–6.7 identify RPG Character Mecanim and Grruzam Archer sets; dodge and sword files exist in scratch | Bow/shield sequences still need individual selection and metadata checks |

No imports are needed for this inventory. Consult the existing asset catalog before searching additional packs.

## Recommended build order

1. One complete tree-chopping action: approach, equip, start, repeated impact, finish, cancellation and target loss. Connect actual resource effects.
2. Reuse that work for mining and plant collection, then verify crafting transactions at existing stations.
3. Add straight hanging movement, then beam and ledge walking. Add corners after straight routes pass.
4. Add rope climbing and fishing as distinct action families.
5. Add one complete sword/shield action path, then bow and dodge. Reuse existing damage ownership.
6. Add clumsy/confident content through existing selection after base actions work.

Each increment needs one playable fixture, complete-action capture and relevant functional checks. A/B/C diagnostics remain available; keep the user-facing queue small. This inventory does not close the [broader animation review](../design/2026-09-13-animation-quality-review.md).

## Crafting follow-up — 2026-09-15

Grinding wildflowers and roasting meat now use shared recipe transactions in CraftingInteractionReview.unity. Complete/cancel captures and source diagnostics exist. See ../design/2026-09-15-crafting-interactions.md. Visual approval pending. Other station recipes, production inventory adoption, and tool pickup/stow remain open.

## Straight hanging travel follow-up — 2026-09-15

Left/right hanging travel now uses authored steps and checked support paths. End rejection, stopping, reversal, queued climb and drop have focused evidence. See ../design/2026-09-15-ledge-travel.md. Revision 2 adds fixed right-angle inside/outside corners in both directions and corrects raised wrists. 101 tests passed; both rendered sequences await visual approval. Beam walking, ledge walking, arbitrary corners, moving supports and gap crossings remain open.

## Beam walking follow-up — 2026-09-15

The [beam slice](../design/2026-09-15-beam-walking.md) adds authored entry, forward/backward travel, balance, turnaround, exit, blocked-path rejection, and support-loss release. Two complete runtime sequences await visual approval. 19 focused tests passed. Main-game adoption and broader geometry remain open. LEDGE-01 revision 4 is approved. Narrow ledge walking is the next traversal gap.

## Narrow ledge walking follow-up — 2026-09-15

Bryan approved BEAM-01 revision 1. The [straight ledge-walking slice](../design/2026-09-15-ledge-walking.md) reuses its route controller with authored sideways travel and wall checks. LEDGE-02 has two complete runtime sequences awaiting approval. 22 focused tests passed. Corners, crouched travel, opposite-end entry, and main-game adoption remain open.

## Rope follow-up — 2026-09-15

Bryan approved LEDGE-02 revision 1. The [fixed rope slice](../design/2026-09-15-rope-climbing.md) adds ground mounting, authored climb/hold/reversal/descent/ground exit, and release/fall. Two ROPE-01 sequences await approval. Top transfers, swinging, thin-rope variants, and main-game adoption remain open.

## Fishing follow-up — 2026-09-15

Bryan authorized continuing while away; ROPE-01 remains unapproved. The [fishing slice](../design/2026-09-15-fishing.md) adds authored actor/rod phases, a bite window, catch rewards, missed bites, cancellation, and target-loss recovery. Three FISH-01 captures await review. Physical unhook/storage, equip/stow, water populations, and main-game adoption remain open. Combat remains the next missing family.

## Fishing mechanics follow-up — 2026-09-16

ROPE-01 is approved. FISH-01 revision 3 adds a bounded fighting-fish prototype with reeling, slack, shared force direction, rod response, and torso/gaze adaptation. Three complete captures await approval. See the fishing design record. Main-game adoption, species AI, physical unhook/storage, equipment, and full line physics remain open.

## 2026-09-16 — Fishing accepted; melee training added
Bryan accepted FISH-01 revision 4 for now. MELEE-01 revision 1 adds attack, block, hit response, and interruption in a fixed-stance training scene. Three captures await review. See docs/design/2026-09-16-sword-shield.md for evidence and remaining integration work. Bow and dodge remain missing; the wider audit remains open.
