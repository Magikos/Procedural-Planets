# Vulture carcass observation

Status: Unity import and automated circling tests passed on 2026-09-07. Isolated art preview captured; production circling capture remains pending.
Bryan reserved Unity for another agent. This pass did not control Unity.

The catalog contains an animated Polyperfect vulture. The project now contains its model and color texture.
No vendor controller or behavior scripts were copied.

The production creature library appends two vultures per suitable territory. Existing species indices remain unchanged.
Vultures use the existing resident identity, persistence, movement, flight grounding, landing, and faction escape systems.
The species `Scavenger` field enables carcass observation and vulture presentation.

`CreatureCarrion.Find` reads the existing `CreatureCorpseStore` and returns a `CreatureResourceTarget` value.
It selects the nearest carcass within the species awareness radius. Equal distances prefer the lower entity ID.
Fresh, bloated, and rotting carcasses qualify. Bones and gone carcasses do not.
Taking hide does not remove carrion interest because current loot and meat availability are separate concepts.

The shared utility decision selects `InvestigateCarrion` above roaming. Needs and threats can override it.
The shared state machine runs `Circle`, persisted as the new value 11. Existing values are unchanged.
The state approaches a tangent orbit with an identity-derived radius between 14 and 26 metres.
The existing flight motor maintains terrain clearance. Loss of the target returns the bird to normal behavior.
Vultures retain ordinary ground and authored perch landing between investigations.

## Carcass agent integration

This pass does not add a second consumption authority or modify carcass stock, decay, loot, or persistence.
Circling is observation, not feeding. Vultures do not yet descend to consume a carcass.
When the carcass agent supplies meat availability, use that eligibility in `CreatureCarrion.Find`.
Feeding must use the same validated consumption path as wolves. Do not grant nutrition from animation.
Current circling can continue above a depleted carcass until bones if depletion arrives without that eligibility update.

## Queued validation

1. Import the new model and texture. Confirm generic rig, idle/fly clips, loop settings, and material references.
2. Compile all affected assemblies. Run `CreatureCarrionTests`, `BirdPerchTests`, `CreatureThreatTests`, and `CreatureResidencyTests`.
3. Check the active carcass agent's final stock API. Add depleted-source coverage before accepting the integration.
4. Spawn a production vulture near fresh carrion. Capture approach, sustained orbit, wingbeats, and model scale.
5. Check multiple vultures, sloped terrain, another planet location, and target decay/removal.
6. Approach as player and predator. Check escape and the existing faction disguise behavior.
7. Check ordinary landing, demotion, reload, death, and animation graph cleanup. Confirm existing bird art still works.

No Unity tests have run for this change. No visual result has been accepted.

## Validation results — 2026-09-07

Unity loaded Vulture_Idle, Vulture_Fly, Vulture_Walk, Vulture_Attack, and Vulture_Death from the imported model.
CreatureCarrionTests passed in the combined 124-test wildlife run, job `64f43144d4ac4a0ba1d5425723d5f111`.
The run covered the 30/60/120 FPS circle cases, carcass eligibility, threat interruption, and missing targets.

`docs/agent-conversation/wildlife-2026-09-07/vulture-poses-front.png` shows the production BirdAnimationView with vulture idle and two flight poses beside the eagle.
This was an isolated Play Mode art preview with controlled material lighting, not a production circling capture.
The initial `vulture-poses.png` was inside the water effect and is not accepted lighting evidence.
No separate glide animation exists in the imported vulture asset.

The current corpse store still has no meat-stock API. The consumption integration remains pending.
No carcass stock, loot, decay, or persistence behavior was changed during validation.
