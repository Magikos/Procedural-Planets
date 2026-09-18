---
name: pp-creature-and-animation
description: Use for creature species, residency, persistent state, behavior transitions, movement, rigs, clips, procedural poses, or repeatable creature scenarios. Distinguish authority simulation from presentation. Asset discovery and import belong in the asset skills.
---

# Creature behavior and animation

Trace the behavior through simulation and presentation before changing either layer.
Follow [the animation continuity rule](../../.agent-memory/animation-transitions.md). Visible transitions must blend, including cancellation, target loss, and retargeting.
Read only the relevant source routes and scenario evidence.
For animation polish or unnatural motion, use [animation quality review](../pp-animation-quality-review/SKILL.md) for rendered comparisons and contact checks.

## Source routes

| Concern | Starting point |
|---|---|
| Simulation lifecycle and observation | `Assets/Scripts/Planet/Creatures/CreatureResidencyService.cs` and its partial files |
| Identity and persistence | `Assets/Scripts/Planet/Creatures/CreatureRecord.cs`; related Core world services |
| Species and authored visuals | `Assets/Scripts/Planet/Creatures/CreatureLibrary.cs`, `CreatureVisualSettings.cs` |
| Behavior | `Assets/Scripts/Planet/Creatures/CreatureBrain.cs`, `CreatureHuntStates.cs`, `CreatureSurvivalStates.cs` |
| View lifecycle and animation | `Assets/Scripts/Planet/Creatures/CreatureView.cs`, `CreatureAnimationView.cs`, `BirdAnimationView.cs` |
| Shared actor capabilities | `Assets/Scripts/Game/Actors` and `Assets/Scripts/Game/Animation` |
| Controlled survival fixture | `Assets/Scripts/Planet/Creatures/PredatorEncounterPrototype.cs`; `docs/design/2026-09-07-agent-systems.md` |
| Existing commands | `Assets/Scripts/Planet/Creatures/CreatureDebugCommands.cs` and residency command declarations |
| Rig and clip authoring | `Assets/Editor/CreatureQuadrupedRigAuthor.cs`, `CreatureSurvivalClipAuthor.cs`, `CreatureAnimationReviewAuthor.cs` |

Paths in the same table cell share the first file's directory unless another path is written.
Search existing actor capabilities before adding creature-specific copies.
Use `pp-architecture-contract` for broader subsystem rules and the asset skills for selected vendor content.

## Preserve ownership

Observation controls simulation residency. It is not a threat identity or a reason to delete a creature.
The current residency service keeps authority logic independent of cameras, input devices, GPU state, and GameObjects.
Preserve that boundary when adding decisions or movement; presentation consumes the resulting state.
Inspect current slot, generation, and record policies before changing identity or respawn behavior.
Verify persisted state across demotion, promotion, and reload when the change touches those paths.
Use realistic timestamps for expiry checks; tiny synthetic epochs can hide precision defects.
Do not infer multiplayer validation from architecture intended to support multiplayer.

## Diagnose behavior and presentation separately

1. Record the species, actor identity, initial needs, target availability, location, and relevant clock values.
2. Verify the decision and state transition before adjusting animation.
3. Check movement intent, gravity, support, and actual displacement before tuning apparent foot sliding.
4. Inspect the selected clip, rig, bone paths, root-motion convention, and blend state on the intended model.
5. Check procedural-pose ownership and reset behavior before adding another transform writer.

Use current shared animation facilities, including `ActorAnimationGraph` where the surrounding code uses it.
Check animation graph disposal, view pooling, and teardown when changing lifecycle behavior.
Do not use animation playback alone as proof of combat, consumption, death, or persistent state transitions.
Do not treat a correctly simulated state as proof that its pose or clip renders correctly.

## Scenario and acceptance

Use the scenario template in `pp-validation-and-evidence` rather than maintaining another fixture format.
Choose relevant cases: normal transition, unavailable target, interruption, death, reset, or residency change.
Specify what the fixture reset preserves and whether time is simulated or wall time.
Use existing commands and inspector controls after verifying their current names; do not invent test commands.
For command changes, follow `docs/design/console-command-authoring.md` and its regression requirements.
Use matching EditMode tests for simulation contracts and Unity runs for animation and lifecycle observations.
Report observation duration and unsupported cases; one encounter does not prove general predator behavior.

## History and maintenance

Use `.agent-memory/claude/project_creature_residency.md` for matching historical traps, not as a current feature inventory.
Keep completed scenario evidence in its existing plan rather than duplicating results here.

## Provenance and maintenance

Source routes and authority/presentation boundaries inspected 2026-09-09 in the dirty working tree.
Reverify tests with `rg --files Assets/Tests/EditMode -g '*Creature*.cs' -g '*Actor*.cs'`.
Reverify reset controls with `rg -n 'ResetScenario|RestartRequested' Assets/Scripts/Planet/Creatures/PredatorEncounterPrototype.cs`.
Reverify graph lifecycle in `CreatureAnimationView.cs`, `BirdAnimationView.cs`, and the current shared animation implementation.
