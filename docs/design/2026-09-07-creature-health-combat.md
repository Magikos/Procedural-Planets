# Creature health, attacks, and recovery

Status: Implemented and tested, authorized by Bryan on 2026-09-07. Visual and balance review remains with Bryan.
Baseline: Dirty harvest-vertical-slice. Preserve existing survival, disposition, animation, and corpse behavior.

Extend the shared actor model and existing creature FSM. Do not add a second state machine or vendor runtime.
Authority owns health, wounds, bleeding, stamina spending, and hit acceptance. Presentation reads these values.
Species author health, attack, and healing profiles as settings assets. Runtime uses immutable snapshots.

Attacks define damage, range, facing angle, wind-up, active window, recovery, stamina cost, wound severity, and bleeding.
One attack may damage its captured target once. An active window can connect after its first frame; a miss cannot grant damage.
Spend stamina once when an attack starts, even if the attack misses or is interrupted.
Hunters need full attack stamina. Defenders can use a partial reserve for proportionally weaker damage.

Health has a maximum and fractional current value. Wound severity and bleeding are separate normalized values.
Bleeding clots over game time and can cause health loss. A dead creature cannot heal.
Healing starts after a safe delay, pauses during combat or running, and accelerates with rest and sleep.
Ordinary hunger and thirst slow healing; severe deprivation blocks it. Food does not grant instant health.
Wounds fade gradually, restoring movement and stamina capacity. Damage restarts the healing delay.

Validation: verify one hit per attack, miss/interruption costs, exhausted damage, recovery timing, damage-driven retreat,
safe healing, bleeding cessation, no revival, deprivation blocking healing, and full regression coverage.
Run the ecosystem and an isolated wounded-deer recovery case. Report balance results without guaranteeing predator success.

World persistence and production host integration remain part of the existing survival migration.
Body-part damage, armor, permanent scars, and treatment items are outside this slice.

## Implemented profiles

| Property | Deer | Wolf |
|---|---|---|
| Maximum health | 100 | 90 |
| Direct damage | 12 | 18 |
| Reach | 1.6 m | 1.4 m |
| Half-angle | 35 degrees | 40 degrees |
| Wind-up | 0.65 seconds | 0.45 seconds |
| Active window | 0.30 seconds | 0.25 seconds |
| Recovery | 1.80 seconds | 1.20 seconds |
| Stamina cost | 18% of full reserve | 12% of full reserve |
| Wound increment | 12% | 20% |
| Bleeding increment | 6% | 15% |

The settings assets are `Assets/AssetPacks/PolyperfectAnimals/Deer/DeerCombat.asset` and the corresponding `Wolf/WolfCombat.asset`.
Restart the encounter after editing a combat asset to capture its immutable snapshot.
Attack clips stretch the pre-contact section to the authored wind-up and the remaining section across the active window.
Actors commit their target and stamina spend when an attack starts. Confirmation ends further hit requests for that attack.
Defenders need at least 25% of attack cost; damage and wound/bleeding increments scale with the amount paid.
The attack cooldown survives interruption. Three missed attempts use the existing hunt-failure mechanism.

Recovery uses game time, while attack timing uses simulation seconds.
Both profiles require 0.25 safe game hours. Full health recovery takes 24 hours at a nourished rest rate; full wound recovery takes 12 hours.
Walking heals at one-quarter the rest rate. Sleeping doubles it. Hunger and thirst reduce the rate, stopping it at 95% need.
Bleeding falls by one full severity level over 12 game hours. A 15% wound therefore clots after 1.8 hours without another injury.
Blood loss integrates bleeding severity across the step. At full severity, the drain duration is two game hours.
Healing cannot begin before both the safe delay and bleeding cessation. Bleeding can kill; death creates the existing carcass resource once.
Wounds smoothly reduce movement toward the existing Wounded Speed control and lower stamina capacity by up to 20 percentage points.
The existing 55% combined stamina-capacity floor remains intact.

## Validation results

- Core and Planet builds passed. The final Planet build reported 18 existing warnings and no errors.
- Final EditMode job `7d5d1dacb4804967b8452d891ed4a158`: 436 passed, zero failures.
- The four-minute ecosystem run produced 11 confirmed hits and one miss. The wolf survived, killed and consumed one deer, and healed while resting.
- The cornered single-pair test reached 30 seconds with both creatures alive. The wolf had 63.7/90 health; the deer had 27.6/100.
- The wolf subsequently killed that deer and consumed meat. This is a fixture outcome, not a claim that wolves always win.
- A real bite reached a deer by 54 seconds. Its health was 81.6/100, wounds 20%, and bleeding 15%.
- After controlled predator separation and holding needs near 20%, that deer reached 100/100 health, zero wounds, and zero bleeding.
- A separate diagnostic 25-damage wound also fully recovered. Core tests cover healing interruption, deprivation, no revival, partial stamina damage, and active-window confirmation.
- Final Graphify update completed: 11,538 nodes, 16,596 edges, 888 communities. Its size limit skipped HTML generation.

Evidence: `local-only/ecosystem-prep/health-ecosystem-240s.txt`, `health-cornered-40s.txt`,
`health-bite-recovery-120s.txt`, and `health-isolated-recovery-120s.txt`.
The isolated recovery tests deliberately remove pursuit and maintain nutrition; they establish recovery behavior, not autonomous ecosystem survival.
Different simulation step batching produced different chase outcomes. These runs do not prove frame-rate-independent encounter determinism.
Production hosts keep legacy attack timing unless they supply an attack definition. Production health/save migration remains pending.

The final HUD retains courage and adds numeric health, wounds, and bleeding. No runtime exceptions appeared in the final console check.
Unity is left paused near the beginning of the default ecosystem. Resume to watch; combat profile changes require restarting the encounter.

## Observation controls and contact spacing (2026-09-07)

The encounter HUD now draws white, wrapped text over an explicit dark background.
Its scroll view retains all health, needs, disposition, wounds, and resource details.
Overview frames living actors. Actor buttons follow one actor, including its carcass.
Free camera uses the existing FreeCameraController with a fixture-owned InputMapService
when no world input service exists. Controls: RMB look, WASD move, Q/E down/up, Shift faster.
No global input service registration or new camera movement implementation was added.

Attack wind-up stops at 90% of configured reach instead of a fixed 0.65 metres.
The fixture resolves horizontal body discs before hit validation: deer radius 0.65 metres,
wolf radius 0.55 metres. This is fixture contact handling, not production navigation or physics.
Corpses do not participate in living-body separation so consumption remains reachable.

Validation: 436/436 EditMode tests passed (job fa4c33cc32084eea8017bac6690b6bd8).
A 40-second cornered-pair run maintained a minimum living center gap of 1.2 metres,
recorded eight hits, and left the wolf alive. At 30 seconds both actors remained alive.
Synthetic W input moved the reused free camera; actor focus restored follow and disabled free input.
Runtime console contained no exceptions. Captures live under local-only/ecosystem-prep/captures:
readable-hud-focus.png and combat-spacing-30s.png.
