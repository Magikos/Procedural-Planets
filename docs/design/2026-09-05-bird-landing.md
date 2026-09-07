# Resident bird landing

Status: Bird behavior and presentation pass implemented, including authored scene perches. Builds and 115 regression tests pass.
The strict arrival-time comparison and remaining visual/lifecycle checks are open. See the 2026-09-06 validation record.

## Scope

Resident birds previously stopped in midair and lowered their terrain-relative support directly beneath themselves.
They did not check water or terrain slope before landing. Their rest clock read requested altitude rather than actual body clearance.

The residency service now samples a ground destination ahead once per second while flying.
The bird brain approaches that destination, turns toward it, and stops horizontal movement within 0.2 metres.
The host lowers flight support along the approach, with the existing climb/descent speed limit.
The twelve-second rest starts only after arrival and actual ground contact.
Threats retain priority. Lost terrain support or changed water levels abort the landing.
Promotion revalidates a remembered ground rest before rebuilding the live bird.

`BirdLandingGround` uses the existing analytic sampler and `CharacterWaterFloor`.
It checks the centre and four footprint points for dry terrain and rejects estimated slopes above thirty degrees.
It includes the planet centre in world-space calculations. Resident behavior does not depend on camera-visible scatter buckets.

## Limits and next work

- Ground and authored scene perches are supported. Flight does not test collisions with trees or props.
- Five terrain probes cannot prove clearance between samples. Runtime slope and cliff checks remain necessary.
- Terrain altitude still drives the existing surface controller. This is not a free-flight physics model.
- Resident birds now use the imported polyperfect eagle with idle, flying, and gliding clips.
- Ambient birds now use animated models with solo, seven-bird, and 24-bird groups. These distant groups do not land.
- Authored scene sockets and exclusive reservations are connected. Moving a socket by more than 1 cm triggers takeoff.
- Instanced scatter trees still need authored socket data and a world-residency publisher. No branch points are inferred from mesh bounds.
- Player landing remains deferred.

## Queued validation

Do not run this queue until Bryan releases Unity. No recurring automation was created.

1. Compile Core, then Planet, serially. Let Unity import the new source when the editor is available.
2. Run the existing `CreatureThreatTests` and `CreatureResidencyTests` fixtures.
   New cases cover unavailable support, aborted landings, approach movement, arrival, water, slopes, and translated planets.
   Existing cases cover threat priority, twelve-second dwell, frame-rate timing, and non-flying species.
3. With a fixed seed, camera pose, and quality tier, observe at least three resident bird landing cycles.
   Require forward travel during descent, stable ground contact, twelve seconds of rest, and smooth takeoff.
4. Compare 30, 60, and 120 Hz simulation traces. Require equivalent arrival/rest transitions within one simulation step.
5. Place candidate sites over ocean, elevated lake water, steep ground, and unavailable terrain.
   Require continued flight without a ground rest at those sites.
6. Change terrain or water during approach and rest. Require departure when the site becomes invalid.
7. Introduce a real entity threat during approach and rest. Require immediate Flee intent and rising support.
   Moving the free camera must not generate a threat.
8. Demote, promote, regenerate, and travel across translated planetary coordinates.
   Require valid state restoration and no retained target from the previous world.
9. Capture the result for Bryan. Check the per-frame cost of support probes with multiple birds resting.

No visual baseline was captured before implementation because another agent owned Unity.
The approach constants and slope threshold require visual review before acceptance.

## Bird populations, Bryan's direction on 2026-09-06

Birds may fly alone, in small or large groups, or in flocks and formations.
The firefly maximum group size does not apply to birds.
Basic loose groups and V formations are implemented in `AmbientSwarms.Birds.cs`.
Birds steer toward moving group positions, separate from close neighbors, and flee nearby perceived threats.

Birds must escape players and predators such as wolves. Reuse `FactionRelationsDto` and `ThreatRegistry`:
the default Wildlife faction already fears Player and Predator, and `PlanetCharacterController` reports the real player.
`SetDisguise` already supports a temporary perceived-faction override for an individual entity.
A future spell can use that override without changing every bird or making other players friendly.
Spell casting, persistence, and individual loyalty or taming are not implemented by this landing pass.

Fish are the next proposed wildlife family. They can be solitary or form schools.
Share faction perception and applicable group steering rules with birds; use aquatic movement and habitat constraints.
Fish must remain within their water body, below its surface, above its bed, and clear of shorelines.
Steering toward a valid point alone is insufficient: validate the travel segment so a school cannot cross land between valid endpoints.

## Validation record, 2026-09-06

- Core and Planet builds passed. The final Planet rebuild reported 18 warnings outside the changed bird code and zero errors.
- Both creature fixtures passed: 101 passed, zero failed, zero skipped.
  Final Unity test job: `b8d3efe8a8d24049936add4ab431fb70`, duration 0.7449427 seconds.
- In Play Mode, reflection probes called the production `CreatureResidencyService.Simulate` method on cloned resident state.
  The probes used the generated planet, real grounding, and seed 1691104419. The editor was paused during controlled stepping.
  Each run covered 180 simulated seconds at 30, 60, or 120 Hz. Each completed three landings and three takeoffs.
  Every measured ground rest lasted 12.0 seconds. These are controlled simulation traces, not video of nine live cycles.
- The first comparison exposed timer drift: each scan reset its countdown and discarded excess elapsed time.
  The countdown now uses double precision and carries excess time into the next interval without replaying missed scans.
  The complete 101-test suite passed again after this correction.
- The final comparison still failed the planned one-step arrival agreement.
  First arrivals occurred at 28.1000, 28.0167, and 28.0167 seconds; maximum spread across corresponding arrivals was 0.0833 seconds.
  Rest duration agrees, but strict trajectory/arrival parity remains open. The two comparison runs did not share an identical initial live pose.
- A controlled contact capture placed the existing placeholder body at the simulated landing pose.
  Measured clearance was zero; horizontal target distance was 0.1802 metres. This checks contact, not bird art or animated flight.
- Injected missing support and flooded support each changed a resting bird to Wander on the next step.
  An injected predator entity changed it to Flee. All three raised flight support by 0.05833 metres on that 1/60-second step.
  Flooding used the support query's water threshold; this was not an actual lake-edit lifecycle test.
- The console reported the existing shader diagnostic:
  `Shader warning in 'GrassNearFieldPlace': use of potentially uninitialized variable (LoadPathWearTexel) at kernel PlaceAndCullNearField at GrassNearFieldPlace.compute(250) (on dx12)`
- Restored the original `Assets/Scenes/Tests/DeerAnimationPrototype.unity` scene in Edit Mode. No scene changes were saved.
- Graphify update completed after the timer correction.

Evidence: `docs/agent-conversation/birds-2026-09-06/landing-traces.json`, `landing-traces-fixed.json`,
`interruptions.json`, and `ground-contact.png`.

Remaining checks: strict arrival parity, actual elevated-lake and terrain-edit lifecycle, demotion/promotion during approach,
regeneration, full-frame support cost, continuous visual flight capture, and Bryan's visual review.

## Animated birds and groups, 2026-09-06

The local catalog contains polyperfect's animated eagle. The project imports its model, clip definitions, and eagle texture.
`BirdAnimationView` owns its manual animation graph and material. It blends flight/glide while airborne and folds wings when resting.
Resident snapshots expose behavior and actual altitude to the renderer. Reconfiguration releases flock animation graphs and models.
Diagnostics now count model birds and locate the nearest bird instead of reporting zero particle emitters.

Ambient birds use stable formation positions, including at creation, with one, seven, or 24 members per group.
The default profile requests up to two groups. This is the present population budget, not a firefly-style group limit.
Nearby player or predator entities cause escape steering through the existing faction rules; a free camera remains neutral.
The current flock controller avoids terrain penetration but does not implement branch or prop collision avoidance.

Validation:

- Planet build passed with zero errors and 18 existing warnings. Build log: `local-only/birds-build.txt`.
- Final fixtures passed 105/105, zero failed or skipped, in 1.2620194 seconds.
  Unity job: `ba4dd1596f2c4430b02e52e732f6ce12`.
- The old emitter test failed because the emitter was removed. Its replacement checks skinned bird models and animation-graph release.
- The first cleanup run exposed `Destroy may not be called from edit mode! Use DestroyImmediate instead.`
  Presentation cleanup now handles Edit Mode and Play Mode separately. Subsequent tests pass.
- Corrected an initial general-atlas mismatch to the vendor eagle texture. Art provenance is under `Assets/Resources/Wildlife/Birds/SOURCE.md`.
- Isolated captures show the flying and folded resting poses: `eagle-flight-fixed.png` and `eagle-rest-frame.png`.
  Earlier `eagle-rest.png` reused a render within the same editor frame and does not establish the resting pose.
- Live world probes observed solo, seven-member, and forced 24-member groups.
  The settled 24-member V formation had 2.215974 metres minimum pair separation in the sampled frame.
  Creation now starts members at their group positions; this final change passed the fixture suite.
- Day/night activity was temporarily forced during the group probe, then restored to the original profile values.
- Restored clean `DeerAnimationPrototype` in Edit Mode, ten scene roots, without saving scene changes.

The authored scene-perch pass below closes the missing branch support. Visual acceptance and the remaining integration limits remain explicit.
Fish movement has started; see `2026-09-06-fish-movement.md`.


## Authored scene perches, 2026-09-06

Add `WildlifeLandingSite` to a child object beneath the owning `Planet`.
Place its transform at the actual foot contact point, orient its up axis away from the surface, and set available clearance in metres.
Each component provides one exclusive perch. Add separate child components for several birds on a large branch or rail.
This does not add perches to existing instanced trees automatically.

The planet scans its child components once per second and publishes active snapshots before each creature tick.
The registry retains Unity's full 64-bit object identity in a scene-only target namespace. These IDs are never saved as creature IDs.
The authority service receives positions, normals, uses, and clearance, without querying scene objects or a visible scatter cache.
Insect use flags exist on the common target contract; current pollinator controllers are not migrated to it yet.

Birds prefer a suitable unclaimed authored site within 25 metres, with terrain as fallback.
They only approach a site below their current body. Site normals must remain within thirty degrees of radial up.
The support must remain above terrain and the local water floor, with enough authored body/wing clearance.
The bird claims the site when it starts its approach. Actual contact controls the rest clock and folded-wing pose.
An elevated support plane keeps the bird on the perch instead of lowering it to the terrain.

Takeoff rebases altitude to the current terrain clearance, preserving height, then climbs before returning to cruise.
A removed, disabled, moved, flooded, or newly unsuitable site ends the rest. Threats still override landing.
Demotion, retirement, death, and world reconfiguration release reservations.
Demotion stores Wander for a scene-perched bird because scene socket identities are ephemeral; later promotion resumes flight.
A moving platform is not supported: displacement beyond 1 cm invalidates its perch instead of dragging the bird.

Validation:

- `BirdPerchTests`, `CreatureThreatTests`, `CreatureResidencyTests`, and `FishMovementTests`: 115 passed, zero failed or skipped.
- Final Unity job `6336b32d699e422a8160bb7255897e23`, duration 1.1877705 seconds.
- Raised perches include an eight-metre sideways approach at 30, 60, and 120 Hz, contact, rest, and takeoff without dropping.
- Removed, moved, flooded, and predator-interrupted perches release their claim and climb on the next simulation step.
- Demotion and retirement release claims. Registry tests cover use filtering and removal/republication.
- The initial component compile rejected `Object.GetInstanceID()` with `error CS0619: 'Object.GetInstanceID()' is obsolete: 'Use GetEntityId instead.'`.
  The component now uses `UnityEngine.EntityId.ToULong(GetEntityId())`; the subsequent build passed.
- Tests drive the production simulation through a fixture with spherical terrain. They do not replace an authored forest playthrough or actual lake edits.
