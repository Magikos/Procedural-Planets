# ProceduralPlanets Shared Memory

Bryan approved all current animations in the 2026-09-13 focused review set: LADDER-01 revision 7, DOOR-01 revision 2, and JUMP-01 revision 4. Preserve these accepted visual baselines. Earlier pending-approval notes for these revisions are superseded. The broader audit remains open.

This is the canonical committed memory index for Claude Code and Codex.
Detailed source memories remain linked below so useful history is preserved
without loading all of it into every session.

## Precedence

1. Bryan's current explicit instructions.
2. Current code, captures, sidecars, and project documentation.
3. This shared memory.
4. Agent-specific historical memory.

Memory can be stale or checkout-specific. Revalidate dates, branches, visual
results, and implementation status before acting.

## Current Direction

- Check the active branch with `git rev-parse --abbrev-ref HEAD`; branch names in historical notes may differ.
- The current broad arc is audit-led architecture, maintainability, and
  performance work. Audit findings remain read-only until Bryan reviews and
  approves implementation.
- Earlier water, cloud, grass, biome, and terrain memories remain relevant when
  those topics resume, but they do not override the newer refactor focus.
- Build success is a code-health check. Unity reimport, regeneration, runtime
  diagnostics, and visual inspection determine rendering correctness.

## Working Preferences

- Bryan approved the [tree harvesting revision 2](../docs/design/2026-09-16-tree-harvesting.md): trunks settle against terrain, ground-contact branch groups break, and falling/chopping produces stronger dust, chips, and leaves. Preserve this shared behavior across generated species. Saplings use debris; dead trees omit leaves. He explicitly does not want a video of every species.

- The [2026-09-15 chopping slice](../docs/design/2026-09-15-chopping-interaction.md) adds opt-in repeated work, timed harvest damage/yield and an editable authored-clip fixture at `Assets/Scenes/Tests/HarvestInteractionReview.unity`. Bryan approved chopping revision 3 on 2026-09-15. It is not main-planet humanoid integration. No per-animation generator was added. Keep unfinished prop fitting separate from functional evidence.

- On 2026-09-15 Bryan prioritized missing interaction technology and animation coverage over existing-motion polish or a walk-variant trial. See [the coverage inventory](../docs/audit/2026-09-15-interaction-animation-coverage.md). Reuse existing phases, contacts and proficiency selection. Keep prop-fitting edits in editable clips; do not add per-animation C# generators. The inventory is source/file evidence, not new runtime validation.

- The [live animation follow-up](../docs/design/2026-09-13-live-animation-feedback.md) adds a standing hop, thin-platform grabs, W/S hanging controls, active chest inspection, and an authored crate lift. Feedback pass 3 adds a grounded hop dip, two supported rim contacts, and load motion from the authored body. Earlier fixed-pose and injected-acquisition reviews are superseded. On 2026-09-14, Bryan requested resting palm rotations and more body motion for CHEST-02. Feedback pass 4 fits the actual body rim and uses a deeper authored stance. Bryan approved CHEST-02 revision 4 (`chest-rest-final-v13`) on 2026-09-14; preserve this visual baseline. Bryan deferred unfinished CARRY-01 revision 3; do not treat that as approval or resume its polish without new scope. The standing hop remains unapproved. The prior accepted review set remains preserved.


- The [round 5 ladder review](../docs/design/2026-09-13-animation-quality-review.md#round-5-normal-posture-for-fast-ladder-climbing) replaces the inward wall-climb selection with faster normal authored climbing. Judge posture and clearance alongside duration. Select contact landing events from hand and direction; faster preparation can cross phase-quarter boundaries. Bryan approved ladder revision 7 (V29); preserve it as the accepted visual baseline. The broader audit remains open.

- The [ladder grip and fast-mode follow-up](../docs/design/2026-09-13-animation-quality-review.md#review-feedback-rung-grip-and-fast-ladders) records the latest implementation. Preserve authored fingers through shared contact release. Prepare fixed rung targets from the intended support pose, not an unfinished reach. Share motor fitting only when both bounded hand requests can fit. Use one sprint admission rule for entry and continuation. Keep native brake body motion when fitting the return rung. Review measurements and residuals before approval; the broader audit remains open.

- Bryan's [purposeful animation requirements](animation-transitions.md#purposeful-motion-and-context) apply to root and sub-agents. Read them with the animation review skill. The [hybrid trial checkpoint](../docs/design/2026-09-13-animation-quality-review.md#hybrid-implementation-checkpoint--2026-09-13) records implemented context-sensitive jumps, door approach, and ladder final-step work. The ladder entry slide, stopping idle-release movement, door source/height limits, and broader audit remain open. Preserve the recorded distinction between native humanoid foot solving and final contact correction.

- Animation continuity is mandatory by default. Bryan established the [animation transition rule](animation-transitions.md) on 2026-09-12. Read it for animation, IK, interaction, and animated-prop changes.
- The [ladder/door follow-up](../docs/design/2026-09-13-animation-quality-review.md#review-feedback-alternating-ladder-hands-and-visible-door-contact) records corrected hand alternation and visible panel contact. Validate current-pose contact evaluation and count failed support acquisition. Ladder rung/forefoot fit remains open; marker-only error cannot certify visible contact.

- Diagnose rendering by stage ownership. Use binary or extreme proof modes
  before tuning values.
- Let the latest F10 capture evidence choose the next debugging branch.
- Keep audits findings-first. Do not begin fixes until Bryan reviews them.
- Prefer focused edits and preserve unrelated work in a dirty worktree.
- Use `ILogger` / `LoggerProvider`, not new direct `UnityEngine.Debug.Log*`.
- Use Unity `Awaitable`; do not introduce coroutines, `async void`, or
  `Task.Run`.
- Do not introduce a test framework until Bryan defines a testing strategy.

## Architecture Memory

Read [architecture memory](architecture.md) before implementation or architecture decisions.

## Agent-Specific Indexes

- [Claude memory index](claude/MEMORY.md)
- [Claude code-refactor arc](claude/project_code_refactor_arc.md)
- [Claude current-focus history](claude/project_current_focus.md)
- [Claude audit workflow](claude/feedback_audit_workflow.md)
- [Claude settings DTO pattern](claude/feedback_settings_dto_pattern.md)
- [Claude subsystem decomposition](claude/feedback_subsystem_decomposition_wiring.md)
- [Codex memory summary](codex/memory_summary.md)
- [Codex memory registry](codex/MEMORY.md)
- [Codex water artifact runbook](codex/skills/proceduralplanets-water-artifact-debug/SKILL.md)

## Codex Rendering History

Read [rendering history](rendering-history.md) before water, cloud, grass, path, or surface-edit work.

## Unity MCP package maintenance

Read [Unity MCP maintenance](unity-mcp-maintenance.md) before MCP package updates or capture troubleshooting.

## Updating Memory

- Stable cross-agent knowledge belongs in this index or a linked topic file.
- Claude-specific auto-memory details may stay under `claude/`.
- Codex-specific evidence and imported historical notes stay under `codex/`.
- When two memories conflict, record the conflict and the evidence that resolves
  it instead of silently deleting history.
- Do not store credentials, tokens, private keys, or sensitive captures.

- Chopping source correction (2026-09-15): Survival chopping FBXs require the vendor T_pose avatar; demo axe ownership is LEFT hand. See docs/design/2026-09-15-chopping-interaction.md. Review revision 2 fixes source/attachment; supporting-hand separation remains open. Rejected anchored-v3 curve experiment was reverted. Bryan subsequently approved review revision 3 (hand-fix-v4); preserve that baseline.

- Mining uses the shared harvest fixture and Mine stone definition. See docs/design/2026-09-15-mining-interaction.md. The pickaxe FBX includes an unwanted authoring hierarchy; use its static mesh directly. Low mining review revision 1 is pending visual approval; medium-height is imported but unreviewed.

- Bryan flagged missing tool equip/stow on 2026-09-15. Ground pickup and/or hip/back draw must replace floating tool acquisition; reuse shared contact markers and ownership. Recorded follow-up, not implemented. Mining revision 2 corrects pickaxe handle anchors and includes low/medium complete captures; visual approval pending.

- Bryan approved mining review revision 2 on 2026-09-15: low rock (grip-v3) and medium rock (medium-v2). Preserve these accepted references. This supersedes pending approval for those captures only. Tool equip/stow and the broader animation audit remain open.

- Gathering review revision 1 adds berries and wildflowers through shared Collect markers, editable clips and separate depleted visuals. See docs/design/2026-09-15-gathering-interactions.md. Both complete/cancel captures exist; visual approval pending. Removal still shrinks the picked visual; storage transport and regrowth are unimplemented.

- Bryan approved GATHER-01 berries and GATHER-02 wildflowers revision 1 on 2026-09-15. Preserve berry-v1 and flower-v1 as accepted visual baselines. Shrink removal, storage transport, regrowth and broader coverage remain open.

- Crafting/cooking review revision 1 adds shared recipe transactions and editable grinding/roasting clips. See docs/design/2026-09-15-crafting-interactions.md. 71 tests passed; complete, cancel, missing-input, unlit-fire and target-loss checks recorded. Visual approval pending. Player inventory adoption, other recipes and authored tool pickup/stow remain open.

- Bryan approved CRAFT-01 grinding and COOK-01 roasting revision 1 on 2026-09-15. Preserve grind-final and roast-final as accepted references. This approval does not include unfinished recipe coverage, pickup/stow, or production inventory integration.

- LedgeTravelReview.unity now has straight hanging travel plus authored inside/outside right-angle corners in both directions. Revision 2 fixes raised wrists and corner elbow contact. A/D travels; W/Space queues climb; S/Ctrl drops. See docs/design/2026-09-15-ledge-travel.md. 101 tests passed; two complete revision-2 sequences await visual approval. Beam/ledge walking and arbitrary/moving ledges remain open. Static contacts must not inherit moving-tool FollowAuthoredMotion; store settled root-fit offsets in actor-local space across turns.

- 2026-09-15: Bryan reports Blender and its MCP installed. Use it when larger pose edits or missing authored motion warrant it. This ledge pass used Unity; Blender connectivity has not been verified.


- Ledge revision 3: Bryan approved the general set subject to elbow checking. Fixed inside-return elbow clipping by keeping bend-direction targets present across idle/travel/corner handoffs; nullable target-shape changes otherwise release contact influence before reacquisition. New captures outside-elbows-v7 / inside-elbows-v5 await this focused check.

- Ledge revision 4: Bryan clarified the defect was the outside elbow bending the wrong way. Outside turns now retain each arm's authored bend direction instead of a shared outward pole. Inside clearance guidance remains. Review outside-bend-v8; approval pending. Do not conflate elbow inversion with wall clipping.

- Bryan approved LEDGE-01 revision 4 on 2026-09-15, including outside-bend-v8 and inside-elbows-v5. Preserve these accepted baselines. Beam walking is the next coverage slice.

- BeamReview.unity now supports fixed level beam entry, forward/backward travel, stop, turn, exit, and support-loss release. Two BEAM-01 sequences await approval. See docs/design/2026-09-15-beam-walking.md. 19 tests passed. Keep camera facing aligned during authored turns through exit, or ordinary movement can reverse the actor afterward. Narrow ledge walking remains next.

- Bryan approved BEAM-01 revision 1 on 2026-09-15: forward-final and control-final. Preserve these references. Narrow ledge walking is next.

- LEDGE-02 revision 1 adds straight narrow ledge walking in LedgeWalkReview.unity through the shared ActorBeam/BeamInteraction route. Two captures await approval; 22 tests passed. See docs/design/2026-09-15-ledge-walking.md. Preserve combined authored rotation when transferring reverse-exit facing to the actor root. Corners, crouched travel, opposite entrance, and rope remain open.

- Bryan approved LEDGE-02 revision 1 on 2026-09-15: forward-v1 and reverse-final. Preserve these references. Rope climbing is the next missing family.

- ROPE-01 revision 1 adds fixed thick-rope ground mount, climb/hold/reverse/ground exit, and release/fall in RopeReview.unity. Two captures await approval; 28 focused tests and Planet build passed. See docs/design/2026-09-15-rope-climbing.md. Preserve authored body dips separately from collision-root motion; match complete ascent/descent travel to avoid stranded return heights. Top transfers, swings, and thin ropes remain open.

- FISH-01 revision 1 adds cast/wait/bite/hook/pull/catch and cancellation in FishingReview.unity. See docs/design/2026-09-15-fishing.md. Seven final fishing tests and Planet build passed; three captures await review. ROPE-01 remains pending because Bryan is away and authorized moving on. Rod equip/stow, physical fish unhook/storage, and main-game adoption remain open. Grip fitting must constrain handle roll; paused capture must refresh prop skinning as well as actor skinning.

- 2026-09-16: Bryan approved ROPE-01 revision 1. Preserve climb-final and release-final. FISH-01 revision 1 needs work: unclear pre-cast arm/line motion, fish jump near catch, missing free-hand line grip during securing, gaze, and cancellation that resembles a bite.

- FISH-01 revision 2 addresses Bryan’s 2026-09-16 feedback. Shorter cast, no-tug recovery range, editable catch arm curves retain right-hand pole ownership, free-hand line contact, continuous fish endpoint, and contextual gaze. See fishing design record. 13 tests and build passed; catch-v4-final/miss-v3/cancel-v3 await approval. Source hand transfers must match attachment ownership. Physical fish storage and equip remain open.

- FISH-01 revision 3 adds ActorFishingFight: shared fish/line/tension state, E reel/Q give line, tension-directed rod deformation, bounded torso response, and landing gating. Bryan accepted the other revision-2 animation fixes; revision-3 forward/sideways/slack captures await approval. 61 tests, build, and live cancellation/target-loss checks passed. Read docs/design/2026-09-15-fishing.md for mechanics and limits. Do not conflate this prototype with full fishing AI, line physics, or main-game integration.

- FISH-01 revision 4 removes the free-line kink (unsupported split sag plus water-plane clamp) and delayed retrieval (phase delay plus endpoint smoothing). A continuous free curve and immediate bounded lift replace them; actual hand contact still introduces two supported spans. line-forward-v4/line-sideways-v4/line-slack-v4 await review. 23 focused tests and build passed. See fishing design record; full inextensible line physics is not claimed.

- 2026-09-16: Bryan accepted FISH-01 revision 4 as good enough for now and requested moving on. Preserve line-forward-v4, line-sideways-v4, and line-slack-v4. Fishing limitations remain open. The next missing family is sword/shield combat, followed by bow and dodge.

- MELEE-01 revision 1 adds fixed-stance sword/shield training. Three captures await review: attack-final, block-final, interrupt-final. Existing health, attack data, playback, and held-tool anchors retain ownership. 12 tests and Planet build passed. See docs/design/2026-09-16-sword-shield.md for evidence and integration limits. Bow and dodge remain missing.

- 2026-09-16: Bryan approved MELEE-01 revision 1. Preserve attack-final, block-final, and interrupt-final. He explicitly reserved future physical/IK reactions based on hit location and impact force; direction must inform the response. This remains deferred, not satisfied by the current generic reactions. See the sword/shield design record for scope and continuity requirements.

- 2026-09-16: Bryan requires physically visible gear. Weapons/tools must remain on the ground or a rack, carried on the character, or in a hand; equip/stow must not make them appear or disappear. Hip/back sword, back shield, and chest/shoulder bow are placement examples. Prioritize shared physical equip/stow before bow, starting with sword/shield. See docs/design/2026-09-16-sword-shield.md for continuous attachment transfers, interruption handling, and combined-gear clearance. Implementation remains open.

- 2026-09-16: Bryan supplied the governing Physical Character, Equipment, and Animation System design. Read docs/design/2026-09-16-physical-character-equipment.md before equipment/motion architecture work. Intent and physical state drive motion; authored animation remains the foundation, augmented by bounded adaptation, reactions, and eventual partial physical control/ragdoll. The next milestone is Physical Equipment & Gear Transfer: unique visible items, location/ownership, contact-driven transfers, partial-draw interruption, pickup/drop/placement, and restrained secondary motion. Preserve approved animations. Future impact/balance/skill systems and exact inventory restrictions remain separate work.

- GEAR-01 revision 1 implements a first equipment transfer prototype in EquipmentReview.unity: persistent item IDs, stowed/hand/world custody, reversible draw/stow, and world-physics drop. Three captures await review. 18 tests and Planet build passed. The milestone remains incomplete: scabbard extraction, straps, shield grip/clearance, pickup/placement, shared slot reservations, secondary motion, persistence, and combat integration remain open. Read docs/design/2026-09-16-equipment-transfer-review.md before continuing; do not call this a finished physical equipment system.

- GEAR-01 revision 2 corrects hip/back mounts, shield grip, and lifted foot placements. Bryan requested further changes to transfer-v20 at frames 114/210/240/276. The revision remains preserved. Match humanoid foot goals to leg edits and bound twist curves; smoothing must not prevent contact.

- GEAR-01 revision 3 adds custody-specific ready clips, a raised elbow during shield reach, a closer grip, and a bent-arm guard. transfer-v25/interrupt-v25/pickup-v25 await approval. F adds approach, kneel, contact-driven pickup, lift, and recovery of the same dropped sword. 19 tests and five live pickup checks passed; build had 0 errors and 21 warnings. Copied one-shot clips must disable asset Loop Time as well as phase looping. See docs/design/2026-09-16-equipment-transfer-review.md for source-comparison limits and unfinished milestone scope.

- GEAR-01 revision 3 was rejected for a stiff free arm, folded/twisting shield arm, and upright sword during pickup. Revision 4 uses sparse native shield-arm keys and a softer free elbow. Pickup retains the cached ground grip orientation through the first lift, then releases the correction during recovery. transfer-v26/interrupt-v26/pickup-v26 await visual approval. 19 tests and five live pickup checks passed. Normal-speed playback was not inspected. Dense numerical contact fits can hide bad elbow/wrist paths; inspect consecutive poses. See docs/design/2026-09-16-equipment-transfer-review.md; the wider equipment milestone remains incomplete.

- 2026-09-16: Bryan accepted GEAR-01 revision 4 (transfer-v26, interrupt-v26, pickup-v26) as good enough to move on. Cleanup remains open; this is not final polish or completion of the physical equipment milestone. Preserve revision-4.html and revision-4-assets as the accepted comparison.

- 2026-09-16: BOW-01 revision 1 adds BowReview.unity with persistent bow/arrow, contact-gated retrieval, draw/hold/fire, cancellation, and stow. Three complete captures await visual approval at local-only/animation-review/bow-2026-09-16/index.html. 27 EditMode tests and eight live checks passed. Source A and matched shot B are included. Native .anim clips remain editable. One arrow and a fixed target are supported; free aim, ammunition, recovery, damage, and gameplay integration remain open. Normal-speed playback and a player build were not performed. Read docs/design/2026-09-16-bow-preparation.md before continuing.

- 2026-09-16: Bryan approved BOW-01 revision 1 and requested the next family. Preserve shot-final, cancel-final, interrupt-final, and revision-1.html. The accepted foundation does not close free aiming, ammunition, recovery, damage, or the broader continuity audit. Directional dodge is next.

- 2026-09-16: DODGE-01 revision 1 adds authored unarmed left/right/backward evasive hops in DodgeReview.unity. ActorDodge reuses swept ActorCollision checks and editable traversal motion data; existing playback owns blends. Q dodges backward, A/D+Q selects a side. Routes require level support; committed steps finish on stop requests. 31 focused tests, five live checks, and the Planet code build passed. The review awaits approval at local-only/animation-review/dodge-2026-09-16/index.html. Rolls, armed variants, combat rules, moving platforms, and dynamic bracing remain open. See docs/design/2026-09-16-directional-dodge.md.

- 2026-09-16: Bryan accepted DODGE-01 revision 1 as fine for now and requested continuation. Preserve lateral-final, retreat-final, interrupt-final, and revision-1.html. Foot settling and dynamic-obstruction bracing remain cleanup items. Forward roll is next.

- 2026-09-16: ROLL-01 revision 1 adds an authored unarmed forward roll in RollReview.unity (W + Q). ActorDodge reuses its movement and playback path with conservative standing and longitudinal clearance. Native Forward Roll.anim retains the source 0.8-second timing; production travel is about 2.93 metres. Stop, run continuation, and blocked/retrigger captures await approval at local-only/animation-review/roll-2026-09-16/index.html. Independent source A and matched stop B are included. 25 focused tests passed; Planet build passed with 23 warnings. Consecutive frames were inspected, not normal-speed playback. Head/hood ground clearance and foot settling need review. Armed rolls, low-obstacle clearance, dedicated impact recovery, and main-game integration remain open. See docs/design/2026-09-16-forward-roll.md.

- 2026-09-16: ROLL-01 revision 2 addresses Bryan's roll-to-run pause. Forward Roll motion.asset sets LocomotionExitNormalized=0.5; default one preserves other evasive actions. ActorDodge releases from supported recovery only when movement is requested, through existing blends and motor ownership. Matched input now starts running at frame 42 instead of during 54 (about 0.4 seconds removed). No-input full recovery remains. 17 tests passed; Planet build passed with 21 warnings. Before/after, late input, source A, and matched run B are available on the roll review page. Consecutive frames inspected; normal-speed playback uninspected. Approval pending. See docs/design/2026-09-16-forward-roll.md.

- 2026-09-16: Bryan approved ROLL-01 revision 2. Preserve revision-2.html, run-final-v2, run-before-v2, run-late-v2, and run-retarget-v2. The approval covers this review set. Armed rolls, main-game combat integration, and the broader continuity audit remain open.
