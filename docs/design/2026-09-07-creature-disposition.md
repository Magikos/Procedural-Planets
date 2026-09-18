# Creature fear and encounter confidence

Status: Implemented in the shared actor model, existing brain, and encounter fixture. Runtime cases and 428/428 regression tests passed.
Current next action: Bryan reviews the encounter, then tune combat balance and extend production perception/save integration.
Baseline: Dirty `harvest-vertical-slice` at `d1e0f62`, 2026-09-07. Preserve concurrent weather and wildlife changes.

Bryan approved persistent courage, changing fear, and confidence calculated for the current encounter.
This extends the [agent systems](2026-09-07-agent-systems.md) and [endurance](2026-09-07-creature-endurance.md) work.

## Ownership and behavior

`ActorDisposition` is a Unity-independent authority model. Its constructor restores courage and fear; confidence is calculated from observations.
The host supplies elapsed simulation seconds and positive effective strengths. Invalid and non-finite inputs fail validation.
Fear rises at up to 50 percentage points per second and falls at up to 3.33 points per second.
Traumatic damage adds the lost health fraction immediately. Hunger never erases fear.

The encounter host calculates strength from the authored strength, health, and absolute stamina.
Fatigue, hunger, and thirst affect stamina through the existing bounded endurance model.
Confidence is own effective strength divided by combined effective strength. It is an estimate, not a calibrated win probability.
No observed opponent produces 100% confidence; this means no current opposition, not invulnerability.

The existing utility selector combines confidence, courage, fear, needs, escape availability, and recovery state.
Its 0.12 switching margin and two-second commitment remain active between defensive choices.
Fear at 85% interrupts willing defense when escape remains possible.
Ending a hunt by fleeing imposes a ten-second hunt cooldown.

Defense uses the appended behavior ID 12. Existing behavior IDs remain unchanged.
The new state faces the threat with small walking turns. It does not pursue a distant retreating opponent.
Close counterattacks reuse `AttackState`, `RecoverState`, clip timing, and host range/facing/life checks.
Only the host applies damage. Animations cannot grant hits.
A capable exhausted creature can defend within 2.5 meters, retaining that choice out to six meters until recovery.
A confident hungry hunter can continue against resisting prey instead of entering a permanent mutual standoff.

## Fixture controls

The existing `PredatorEncounterPrototype` inspector exposes these controls:

| Control | Default | Application |
|---|---|---|
| Deer Courage / Wolf Courage | 0.35 / 0.65 | Restart applies the value |
| Deer Strength / Wolf Strength | 1 / 1.5 | Live effective strength input |
| First Deer Cornered | Off | Diagnostic escape-blocked input for the first deer |

The cornered control does not create navigation obstacles. The production navigation host must provide actual escape feasibility.
The HUD shows fear, confidence, courage, and objective for every creature.
Both deer and wolf use the same assessment and defensive state. The fixture does not use sex as an aggression switch.
The existing deer attack clip supplies the prototype counterattack. Dedicated threat displays and species-specific attack reach remain future work.

## Validation cases

- Ordinary deer should initially flee from the hunting wolf.
- Exhausted or diagnostically cornered deer should counterattack at close range, with one hit request per attack.
- A wolf facing overwhelming strength should flee, then calm gradually after reaching safety.
- Small confidence changes should not alternate fight and flee each tick.
- A retreating hunter should not restart its hunt during the ten-second cooldown.
- Food, water, sleep, stamina, and carrion behavior must retain regression coverage.

Initial simulation exposed mutual defensive standoffs and immediate re-hunting after retreat. The decision corrections above address both.
A four-minute run also exposed critical thirst losing to fruitless food search when remembered water was far away.
Known food and water now gain up to 0.3 utility above 80% need. This preserves travel cost while overcoming search stickiness at critical need.

## Scope limits

The fixture supplies disposition inputs. Production world hosts retain their prior flee behavior until they supply the new senses.
Pack support, group morale, offspring protection, and dedicated threat displays are not implemented in this slice.
No bear asset was imported. Raising deer strength to eight tests an overwhelming opponent using existing models.
World saves and networking do not yet persist disposition. The model supports value restoration for the later authority/save integration.
Combat still uses the fixture's two health points and one point per hit. These results do not establish final ecological balance.

## Recorded results

- Core build: zero errors and zero warnings. Planet build: zero errors and 18 existing warnings.
- Final EditMode job `3d75dd7d487a45bc92c5547c05e90a1c`: 428 passed, zero failures.
- The default encounter initially produced fleeing deer. Deer 3 defended at approximately 37 seconds after exhausting its stamina.
- The wounded wolf retreated at approximately 39 seconds. The ten-second cooldown prevented the earlier two-second return to hunting.
- In the final four-minute run, the wolf selected remembered water, drank, and resumed hunting. A later defensive hit killed it.
- In the cornered single-pair case, both animals exchanged hits. The deer survived and the wolf died.
- With deer strength eight, wolf confidence was approximately 16%. It fled, rested beyond the threat range, and calmed gradually.
- No final runtime exceptions or C# compilation errors appeared in the console checks.
- Graphify updated 800 code files: 11,492 nodes, 16,527 edges, 884 communities. HTML output was skipped by its existing size limit.

Evidence lives under `local-only/ecosystem-prep/`: `disposition-survival-final-240s.txt`,
`disposition-final-240s.txt` (retreat trace before the critical-resource correction),
`disposition-cornered-30s.txt`, and `disposition-strong-opponent.txt`.
The close defensive screenshot is `captures/disposition-defense-close.png`.
Unity is left paused one second into the default ecosystem, with normal time scale and the fixture selected.
