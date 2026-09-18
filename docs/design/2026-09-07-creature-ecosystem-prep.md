# Creature ecosystem preparation

Status: Integrated into the encounter fixture after Bryan released Unity. Runtime checks passed; prototype art awaits Bryan's review.
Current next action: Review the expanded scene and refine corpse art. Connect these tested mechanisms to persistent world authority in a separate integration.
Follow-up: [Stamina, fatigue, source memory, and carrion switching](2026-09-07-creature-endurance.md) now extend this fixture.
Baseline: Dirty working tree on `harvest-vertical-slice`, observed at `d1e0f62` on 2026-09-07. Other agents are making changes.

## Current behavior and prepared work

This section records the preparation baseline. The implementation results below supersede its not-yet-implemented statements.

Hunger and thirst currently influence decisions but cannot kill an actor. Earlier work authored rest, sleep, and wolf eating poses.
The existing wolf locomotion, attack, and death clips came from Polyperfect. A wider catalog search occurred during this preparation.

Prepared files live in `local-only/ecosystem-prep/`. They do not participate in Unity compilation.

| Prepared item | Scope | Remaining work |
|---|---|---|
| `ActorDeprivation.cs` | Grace periods, accumulated exposure, fractional health damage | Host integration, persistence, damage cause |
| `ActorResourceSource.cs` | Cap remaining stock without granting nutrition or refilling it | Apply the cap from corpse authority |
| `CreatureCorpse.cs` | Game-day decay profile, age queries, meat-aware fly eligibility | Persistent game time and remaining meat |
| `CreatureEcosystemTests.cs` | Five focused tests | Compile and run after Unity becomes free |
| `scenario.json` | Actor positions, needs, resources, proposed controls | Extend the existing prototype host |
| `asset-candidates.json` | Exact asset paths, sizes, SHA256 hashes | Import and visual inspection |

These files are drafts. The multi-actor scene, skeleton presentation, blood, and animation conversion are not implemented.

## Asset findings

All paths below are relative to `D:/Unity/Explore Assets/Assets/`.

| Asset | Verified finding | Integration decision |
|---|---|---|
| `Malbers Animations/Animal Controller/Wolf Lite/Animations/WL_Sneak.anim` | Named sneak clip with looping enabled | Preview crouch and foot contacts on its source rig |
| Same folder: `WL_Sneak Idle.anim` | Sneak idle clip exists | Candidate for stationary stalking |
| Same folder: `WL_Actions.FBX` | Import metadata includes eat, drink, howl, dig, and crawl | Preview useful actions before authoring more poses |
| Same folder: `WL_Sleep.FBX` | Sit, lie, sleep, and transition clips exist | Candidate source for baked poses and transitions |
| `Devdog/InventoryPro/Demos/Assets/Textures/BloodSplat.png` | Inspected red splatter with alpha, 256 by 128 | Art-only ground stain candidate |
| `Synty/PolygonNatureBiomes/PNB_Arid_Desert/Models/SM_Prop_Bones_01.fbx` through `_09.fbx` | Files exist; meshes not visually inspected | Review scale, anatomy, and style |
| `Survival_Animations/Models/Skinning/AnimalCorpse_Mesh.FBX` | File exists; mesh not visually inspected | Review before choosing corpse art |

Malbers and Polyperfect use different generic skeletons. These clips are not drop-in humanoid retargeting assets.
Preview Malbers on its source rig. Then assess rest-space conversion and an IK-assisted bake onto the accepted Polyperfect wolf.
Normalize body proportions and preserve foot-contact phases. Keep current clips until replacement quality is verified.
Do not replace the accepted models or install vendor runtime code.

No matching Polyperfect deer skeleton mesh was confirmed. Animation bones are transforms, not visible skeletal anatomy.
Build or adapt matching ribs, spine, skull, and limb meshes if the candidate art does not fit.

## Reuse and preservation

| Existing system | Extension | Preservation requirement |
|---|---|---|
| `AdaptiveStateMachine<T>` and utility decisions | More actors and survival outcomes | Keep commitment, emergency interruption, and failed-target memory |
| `ActorNeeds`, actor health | Deprivation damage after grace | One health/death path; no healing from drinking alone |
| `ActorResourceSource` | Shared food, water, and corpse stock | No duplicated stock per consumer |
| `CreatureCorpseStore`, `CorpseDecay` | Game-time age and persistent meat | Read legacy format 10 and preserve hide harvesting |
| `AmbientSwarms` | Delayed corpse flies from accepted corpse state | Reuse fly rendering and preserve other swarm kinds |
| `SurfaceEditController` and stamp ledger | Blood stain records and rendering | Preserve path/scorch records and terrain channels |
| `PredatorEncounterPrototype` | Multi-actor preset | Keep the single-pair preset and movement controls |

Concurrent work has added scavenger behavior, `Circle = 11`, and `InvestigateCarrion`. Rebase against that work before integration.
Carrion observations must use the same corpse stock and eligibility rules as other consumers.

The current corpse store uses UTC, with stages at one hour, six hours, one day, and three days.
The prepared profile does not replace those legacy defaults.
`CreatureView` also reads UTC directly. It must receive an authority snapshot when game-time decay is connected.

The store delays deletion near observers, but `CollectNear` already filters Gone records. This can still remove a visible corpse.
Align presentation removal and record retention during integration. Add a boundary test with an observer beside the corpse.

Current surface stamp encoding supports path and scorch only. Unknown kinds can fall through to path.
Add an explicit versioned blood kind and reject unsupported new kinds. Do not pass a blood label through the existing fallback.

## Scenario

The prepared preset contains four deer, one wolf, three plant patches, two fresh-water sources, and one salt-water rejection control.
The deer start with different hunger and thirst levels. The wolf starts half-full and can rest before hunting.
Deer use 100% escape speed. A successful hunt is an outcome, not a scripted requirement.
Use seed 72026 and keep a restart control. Exact positions and initial quantities are in the prepared `scenario.json`.

Extend the existing host with actor records and shared source records. Each actor keeps its identity, needs, health, brain, motor, and view.
Sample observations from one pre-tick snapshot. Resolve movement, attacks, and consumption in a stable authority order.
Resolve simultaneous consumers against shared remaining stock. Presentation receives the accepted result and cannot grant resources.
Apply diet and relationship eligibility before scoring targets. Keep target commitment and exclude packmates from predation.

## Time, deprivation, and decay

Use one authority-owned monotonic game clock. `CelestialManager.TimeOfDay` wraps and cannot represent corpse age.
Persist accumulated game time. A debug speed change must affect future advancement, not multiply all past age.
Proposed offline behavior: pause the new game clock while the world is closed. Keep legacy UTC semantics until an explicit migration.
Never reinterpret a legacy UTC timestamp as game seconds. Version new records and test the migration boundary.

| Proposed decay event | Game age |
|---|---|
| Flies appear while flesh remains | Six hours |
| Bloated stage | Two days |
| Rotting stage | Five days |
| Bones stage | Twenty-one days |
| Final removal | Ninety days |

These are adjustable gameplay defaults, not biological measurements. The current default day length is 120 real seconds.
Keep carcass age preview separate from advancing the whole simulation. Previewing thirty days must not silently starve every live actor.

Keep remaining meat separate from chronological age. Feeding reveals skeletal areas immediately; age changes appearance and limits available flesh.
The staged stock cap uses the smaller of current stock and the age-derived capacity. It never refills stock or grants nutrition.
This is a gameplay approximation. It does not apply a proportional decay rate to each remaining portion.
Preserve hide loot independently. Feeding must not duplicate inventory rewards or consume the hide implicitly.

Proposed deprivation defaults start damage after one day at maximum hunger or six hours at maximum thirst.
Further starvation kills a full-health actor over three days. Further dehydration kills it over one and a half days.
Both exposures can contribute damage. Consumption stops exposure when the corresponding need leaves saturation.
Use fractional accumulation for actors with small integer health. Persist exposure and remainder so reload cannot reset the grace period.
Split large time steps at need saturation, consumption, damage, and death boundaries. The prepared evaluator assumes a constant saturation interval.

## Corpse and blood presentation

Drive flesh coverage from remaining meat. Use bounded flesh sections over a matching skeleton, with consistent male and female variants.
Drive discoloration and decomposition from age. Do not use a uniform body fade as the final exposed-bone implementation.
Retire flies when flesh reaches zero or the bones stage begins. Adapt the existing swarm anchor/up contract for the test ground.

Accepted traumatic wounds emit blood events. Starvation and dehydration do not create blood trails.
Place stains using ground positions and normals. Emit trails by travel distance with bounded density; cap stationary pooling.
Stop trails after healing or death. A death event can create a bounded pool once.
Persist stain identity, position, age, and seed through the shared ledger. Fade and retire stains using game time.
Do not reuse terrain state channels without an explicit rendering contract.

## Integration and queued validation

1. Run `local-only/ecosystem-prep/Verify-Baselines.ps1`. Rebase conflicts; do not overwrite another agent's changes.
2. Integrate the shared multi-actor host and deprivation path. Preserve the existing single-pair controls.
3. Connect the game clock, versioned corpse stock, and existing swarm presentation.
4. Preview animation and corpse candidates. Bake or author matching assets after confirming rig compatibility.
5. Add blood records and rendering through the existing surface ledger.
6. Compile and run focused EditMode tests, then the relevant prior regression suite.
7. Inspect the scene in Unity and record runtime outcomes and visual evidence.

Queued checks:

- Five staged tests: grace and tick-size stability, drinking recovery, lethal deprivation, shared stock, and game-day decay.
- Prior 179-test baseline, plus current tests added by other agents. Earlier results do not validate these drafts.
- Multiple consumers cannot overdraw a source. Empty sources trigger a new decision without rapid state switching.
- Full-speed deer can escape; a failed hunt does not force a kill or endless pursuit.
- Death creates one corpse. Reload preserves needs, exposure, meat, age, and hide availability without duplicate rewards.
- Legacy corpse format 10 and existing path/scorch stamps retain their behavior.
- Flies obey delay and flesh availability. Bones remain through day 89 and retire under the selected visibility policy.
- Wounds produce bounded trails; deprivation does not. Healing and death stop moving trails.
- Crouched stalking retains foot contacts. Skeleton exposure fits both deer variants and the accepted wolf.
- Pause, large time advances, salt-water rejection, surface grounding, and bounded visual counts behave correctly.

No build, Unity import, or functional test was run for this preparation. Only the read-only staging baseline check ran.

## Implementation results after Unity became free

The scene remains `Assets/Scenes/Tests/WolfDeerEncounter.unity`. Its default preset now contains the five actors and six sources described above.
The original single-pair preset remains available. Inspector needs and speed controls remain live.
The buttons switch presets, restart, select full deer speed, toggle resources, wound the first deer, and age carcasses independently.

Implemented shared mechanisms:

- `ActorDeprivation` accumulates exposure and fractional damage. The encounter resolves this damage through its existing local health/death path.
- `CreatureCorpseResource` owns finite meat and accumulated game age. Its constructor supports restoring age and remaining stock.
- `CorpseDecay.GameDays` supplies the proposed schedule. The encounter exposes individual stage durations, sampled when an actor dies.
- `AmbientSwarms.SyncCorpseFlies` accepts surface-independent anchors. Both the production corpse adapter and fixture use this same presentation path.
- `CreatureCorpsePresentation` reveals a rig-fitted skeletal scaffold as flesh decreases. Age changes the remaining flesh tint.
- `CreatureBloodPresentation` places bounded ground stains from accepted traumatic wounds. Trails stop after the wound timer or death.
- `SurfaceEditStampCodec` writes blood as format 2 with a surface normal and game timestamp. Existing path/scorch format 1 remains unchanged.
- `CreatureAnimationView` blends an optional stalk clip. Production and fixture presentation both set the stalk state.

The custom stalk clip lowers the accepted wolf's body while retaining its walk foot targets through the existing limb solver.
It was baked with `CreatureSurvivalClipAuthor.BuildStalk`. Malbers clips were not imported or retargeted during integration.
The blood splatter texture was imported without Inventory Pro code. Its source is recorded beside the texture.

Runtime evidence, captured with the fixture paused between manual bounded steps:

| Check | Observed result |
|---|---|
| Expanded preset, 15 seconds | Deer fed and drank from separate sources; wolf entered Stalk; all five actors lived |
| Expanded preset, 75 seconds, deer at 100% | All four deer survived; wolf had two missed attacks; source quantities decreased |
| Single pair, deer at 40%, 45 seconds | Two accepted hits, one corpse, meat reduced from 1.5 to 0.58; wolf returned to sleep |
| Isolated starvation, saturated hunger, one-second test days | Both actors died after the grace and damage interval; two corpses; zero blood stains |
| Isolated dehydration, saturated thirst, one-second test days | Both actors died; two corpses; zero blood stains |
| Carcass preview +1 day | Fly system appeared while 38% meat remained; living actor needs stayed effectively unchanged |
| Carcass preview past day 21 | Zero meat and exposed skeleton; no new fly emission |

Evidence folder: `local-only/ecosystem-prep/captures/`. It includes the baseline, expanded scene, feeding, and bones views.
An early screenshot showed temporary cyan shader compilation placeholders. Later captures showed the red splatter texture correctly.
A source name initially collided with the scavenger helper `CreatureCarrion`. Renaming the new class to `CreatureCorpseResource` resolved compilation.

Code-health builds: Core passed with zero warnings; Planet passed with 22 existing warnings; Editor passed with 43 existing warnings.
The first integrated EditMode run passed 413/413 tests, job `4a6ce0002faa4e5d8f1eb2450672e940`.
The final regression run also passed 413/413, job `87b3c68087374ba6b8f778d893c0e643`, after fly-path consolidation and blood-record validation.
This includes seven ecosystem tests and the existing world persistence, surface stamp, creature, and scavenger tests.
The final live check confirmed fly emission at day one, stopped emission after bones, and hidden corpse presentation after final removal.
`stalk-close.png` records the custom crouched wolf pose. Unity was left paused one second into the restored expanded preset.
Graphify updated successfully. No runtime exceptions appeared during the final scene checks.

### Explicit limits

This is an encounter-fixture integration. The production world still uses its existing UTC corpse records and health/needs persistence.
The fixture holds game time, deprivation, meat, and blood records in memory. Restart intentionally resets them.
Constructor restoration and codec round trips are tested; this does not prove save/reload integration for the new fields.
The production world must adopt one persistent game clock and version corpse/actor records before these states survive world reloads.
Existing hide harvesting, legacy corpse format 10, and production source authority remain intact.

The skeleton is a procedural art blockout, including an oval skull placeholder. It is not final deer or wolf anatomical art.
Flesh removal uses discrete spatial mesh sections. Its visual coverage is approximate, and cut boundaries need art refinement.
The test scene uses ground markers for plants and water. It does not add production plant detection or lake geometry.
The fixture samples deprivation after consumption on 0.05-second steps. Saturation-boundary timing is accurate to that bounded step, not analytically exact.
The production Gone-stage visibility mismatch described above remains separate from this fixture's explicit age-preview removal.
No pack hunting, navigation rewrite, network transport, or offline-aging migration was added.
