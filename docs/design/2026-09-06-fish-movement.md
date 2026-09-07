# Fish population and swimming

Status: Implemented, imported, and validated in the generated ocean and a freshwater lake on 2026-09-06.

## Species and habitat

`FishSpecies` contains the immutable rules used by spawning, movement, and rendering.
The existing `WaterSample.IsOcean` classification determines habitat. Height or visual water colour does not determine it.

| Species | Habitat | Target length | Body clearance radius | Minimum water depth | Normal speed |
| --- | --- | --- | --- | --- | --- |
| Freshwater fish | Lakes | 0.4 m | 0.25 m | 1.2 m | 1.2 m/s |
| Coastal fish | Ocean | 0.5 m | 0.3 m | 1.5 m | 1.5 m/s |
| Shark | Ocean only | 3.5 m | 1.8 m | 8 m | 2.5 m/s |

The renderer caps model scale to the enclosing body sphere, so imported animation bounds can reduce the target length.
Shark habitat checks apply to initial placement and every sampled movement point. Sharks cannot enter a lake by following another fish.
Body identity remains fixed for each group. A changed water type, removed water, or lost clearance invalidates that group.

## Ownership and behavior

`Planet` creates `FishPopulation` with the existing water-query service and threat registry.
Generation configures the population after the water service. Each frame advances simulation, then synchronizes `FishView`.
World regeneration and teardown clear bodies, animation graphs, and predator reports. `fish.status` reports group, fish, and shark counts.

The population searches near the observer once per second, with at most twelve placement attempts.
It maintains up to six small-fish groups and one solitary shark. Small groups have one, eight, or sixteen members.
Candidates start 12–102 metres from the observer horizontally, within 150 metres in world distance.
Groups leave simulation beyond 180 metres. Rejected habitat does not receive replacement fish above water.

`FishSchool` supports one to 64 members. Members turn gradually, follow a group heading, maintain depth, and separate from neighbors.
Each step reads the previous positions before writing the next positions. Large time steps split into steps no longer than 1/30 second.
Wildlife fish flee recognized threats at twice normal speed. The existing faction disguise changes this response for the affected player.
Sharks report the Predator faction. They cause nearby small fish to escape, but this pass does not implement hunting or bite damage.
The free camera determines the simulation area and never becomes an entity threat.

Aquatic group IDs use the reserved `EntityId.AquaticOwner` namespace, distinct from land-creature slots and carcasses.
These are transient ambient groups, not persistent harvestable residents. Fishing, damage, and save-state persistence need a later residency pass.

## Water constraints

`FishMovement` checks the body ID, species habitat, surface clearance, and bed clearance.
Four lateral body probes supplement the centre query. The clearance sphere encloses the animated model at every heading.
Movement samples the travel segment at intervals no greater than 0.25 metres or the body clearance, whichever is smaller.
Long teleports and excessive sample counts are rejected. Failed travel retains the previous position while the fish turns.

The existing service describes still water and analytic bed height. It does not supply a continuous collision sweep.
These probes cannot prove clearance for features narrower than the sample spacing or obstacles absent from the heightfield.
Fish do not currently avoid kelp, props, or caves through collider queries. No alternate water-height system was added.

## Catalog art

The local catalog contains Distant Lands tropical fish and sharks, Layer Lab fish/tuna/sharks, Quirky Series carp, and polyperfect fish/sharks.
This pass uses polyperfect's small fish and shark to match the animated bird asset family.
Only the model, swimming clip, and matching texture for each animal are imported.
See `Assets/Resources/Wildlife/Fish/SOURCE.md` for exact catalog paths and license provenance.

`FishView` owns manual animation graphs and shares one planet-lit material per model.
Swim phases vary between fish. The small-fish import faces +X, so presentation rotates it to the controller's +Z forward axis.
Normalization uses animated bounds in the root-bone frame. The model's import transform has a different scale and must not be applied twice.
Parent-scale compensation keeps body dimensions in world metres under a scaled planet.
The generic small-fish model currently represents both freshwater and coastal fish. Their habitat rules remain separate.

## Validation record

- Core build passed with zero errors and zero warnings. Planet build passed with zero errors and 18 existing warnings.
  Logs: `local-only/fish-core-build.txt` and `local-only/fish-planet-build.txt`.
- Final combined fixtures: 122 passed, zero failed, zero skipped, duration 1.2112103 seconds.
  Unity job: `0c683da361ba4fe49f24c93714ee8c33`.
- Tests cover ocean-only and minimum-depth rules, habitat crossing with the same body ID, body-side clearance, dry travel intervals,
  school movement, predator/player response, actual faction disguise, missing water, observer departure, regeneration, animation, and graph cleanup.
- Presentation tests run under translated parents at scales one and two. They check rotated body bounds against the clearance sphere.
- Initial visual inspection exposed models that were too small from duplicated import scale. Root-bone bounds corrected this.
- An intermediate test used world-aligned bounds and failed with `Expected: less than or equal to 0.330000013f` and `But was: 0.336712778f`.
  World-aligned bounds expand with rotation. The corrected test checks the transformed corners of the oriented bounds against the body sphere.
- A fresh Play Mode run generated seed 12345. Ocean body 1 contained seven groups, fifty fish including one shark.
  All sampled members passed the production habitat query. Observed group sizes included one, eight, and sixteen.
- Lake body 32 contained six freshwater groups, forty-nine fish, and zero sharks. All sampled members passed habitat checks.
- Live captures: `docs/agent-conversation/fish-2026-09-06/shark-live.png`, `school-live.png`, and `lake-school-live.png`.
  These use production materials in the generated water. The editor was paused for close-up captures.
- A thirty-step production simulation probe with fifty fish averaged 3.1411 ms per step and returned zero invalid fish.
  This is a small isolated CPU sample, not a full-frame performance benchmark. One sampled fish moved 0.7492 metres in half a simulated second.
- `fish.status` was checked through the scanned console registry after correcting its target type to `MonoTargetType.Registry`.
- The fresh run reported existing scatter tint diagnostics and the existing shader warning:
  `Shader warning in 'GrassNearFieldPlace': use of potentially uninitialized variable (LoadPathWearTexel) at kernel PlaceAndCullNearField at GrassNearFieldPlace.compute(250) (on dx12)`.
- Normal Unity compilation handled new types and attributes that Hot Reload could not apply. Final builds and fixtures passed.
- Restored the original `Assets/Scenes/Tests/DeerAnimationPrototype.unity` scene in Edit Mode without saving scene changes.

Remaining validation: actual terrain/water-edit workflows, longer shoreline traces, and full-frame rendering cost at the maximum population.
