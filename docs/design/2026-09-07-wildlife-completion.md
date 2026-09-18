# Wildlife completion work

Bryan requested continued implementation while away on 2026-09-07.
The work includes owned animal art and sounds, missing actions, procedural rigs, bird behavior, and habitat distribution.
The active goal remains open until implementation and runtime checks are complete.

## Required checks

- Existing saved species and scatter addresses remain stable.
- Every added ground presentation has valid locomotion and feeding/rest/sleep actions.
- Quadrupeds have four validated limb chains, sampled gait contacts, spine, look, and tail bindings.
- The snake uses articulated body/head/tail motion without fabricated legs.
- Grounded bird activities preserve support and do not consume food while airborne.
- Endurance values survive save/reload and do not reset through unloading.
- Habitat placement respects biome, slope, dry ground, density, and mapped lake shores.
- Expanded herbivore habitats have visible, renewable forage.
- Builds and the full EditMode suite pass after import helpers run.
- Runtime review covers action poses, locomotion turns, bird landing/takeoff, and populated Planet habitats.

## Authoring helpers

Run the helpers after Unity imports current scripts and source art:

1. `CreatureAnimalAssetAuthor.Build()`
2. `CreatureQuadrupedRigAuthor.Build()`
3. `CreatureBiomeAnimalAuthor.Build()`
4. `CreatureBirdAssetAuthor.ConfigurePlanetLibrary()`
5. `CreatureBirdAssetAuthor.BuildRigs()`
6. `CreatureSurvivalClipAuthor.RebuildGroundDrinks()`
7. `CreatureHabitatAuthor.Configure()`
8. `CreatureForageAuthor.Build()`
9. `CreatureAnimationReviewAuthor.Build()`
10. `BirdAnimationReviewAuthor.Build()`

The review helper saves `Assets/Scenes/Tests/WildlifeAnimationReview.unity` without replacing the active scene.
The scene extends the existing animation prototype rather than introducing another animation system.

## Starting density targets

These are gameplay population targets per square kilometre of territory with suitable candidate habitat.
They are not claims about real-world animal densities. Stable slot capacity caps each territory.
Lake-shore multipliers increase local capacity without forcing all animals onto shorelines.

| Species | Base density/km² | Main habitat | Lake multiplier |
|---|---:|---|---:|
| Deer | 25 | Woodland, grassland, scrub, steppe | 1.8 |
| Rabbit | 60 | Open vegetation and woodland; sparse desert/tundra | 1.5 |
| Wolf | 8 | Forest, taiga, grassland, scrub, steppe | 1.5 |
| Boar | 10 | Woodland and vegetated lowlands | 2 |
| Fox | 8 | Woodland and open vegetation | 1.5 |
| Bear | 1.5 | Forest, taiga, mountain, lake shore | 2 |
| Polar bear | 1 | Tundra with rabbit/goat prey | 1 |
| Goat | 15 | Mountain, tundra, steppe, scrub | 1.1 |
| Snake | 12 | Desert, scrub, savanna | 1 |
| Eagle | 12 | Existing bird habitats | 1.2 |
| Vulture | 3 | Existing scavenger habitats | 1 |
| Seagull | 18 | Beach and lake shore | 1.5 |

Rabbit density uses 0.15 weight in desert and 0.35 in tundra.
Existing saved animals remain alive even if a later habitat setting would reject their home.
New species use unused explicit addresses, preserving the original four species' cumulative addresses.

## Validation log

Core build passes. Planet build passes with 18 existing warnings.
Rabbit, boar, fox, bear, polar bear, goat, and snake authoring helpers ran successfully.
The first action review captured walking, sleeping, and feeding under `local-only/ecosystem-prep/full-wildlife/`.
Walking and feeding diagnostics produced finite procedural foot residuals.
The full EditMode suite passed 620/620 tests after bird, lake-home, carcass identity, orientation, and coverage corrections.
Job: `6fd600c9d3ca472d8b610441eb248a33`, 13.70 seconds of test execution. No tests failed or were skipped.
Core build: zero warnings and errors. Planet build: 18 existing warnings, zero errors.
Graphify updated 881 source files, producing 12,609 nodes and 18,350 edges.
The final wolf, boar, and fox carcass review passed at 70% meat remaining, with no console errors.
Ribs follow the posed anatomical direction. Flesh coverage uses triangle surface area across submeshes.
Capture: `local-only/ecosystem-prep/full-wildlife/carcass-coverage-final.png`.
Temporary review objects were removed. Unity was stopped after the review.

### Planet habitat and forage audit

Seed `1691104419`, radius 5,000 m, 1,536 territories, 92 lakes, and three ocean bodies.
The read-only audit sampled all 66,048 configured slots in 145.84 seconds.
These counts describe potential seeded homes. They exclude saved deaths and are not simultaneously simulated animals.

| Species | Potential homes | Occupied territories | Homes near mapped lakes |
|---|---:|---:|---:|
| Deer | 3,315 | 760 | 82 |
| Rabbit | 6,650 | 800 | 146 |
| Eagle | 2,272 | 859 | 351 |
| Vulture | 567 | 474 | 29 |
| Wolf | 917 | 525 | 32 |
| Boar | 1,139 | 571 | 28 |
| Fox | 863 | 556 | 28 |
| Bear | 126 | 126 | 10 |
| Polar bear | 6 | 6 | 0 |
| Goat | 575 | 251 | 0 |
| Snake | 833 | 375 | 0 |
| Seagull | 648 | 432 | 24 |

Snake homes include 264 desert, 341 savanna, and 228 scrub locations.
All six polar bear homes are tundra. Goat homes include 36 tundra locations.
This seed produced no accepted Mountain-primary homes. The recipe still permits that biome on other terrain.
Snow and ice bog are excluded from polar bear homes until they have suitable prey.
Land species reject lake beds deeper than 0.2 m. Flying and explicitly aquatic species retain their existing rules.

The forage audit queried actual edible scatter within 24 m of one occupied home per herbivore biome.
All 28 samples had reachable, unharvested food. Nearest food ranged from 0.3 to 8.2 m.
Cold samples were the sparsest: 10–11 grass clumps supplied 3.0–3.3 food units.
These checks prove local resource availability, not long-term ecological balance or universal water access.

The observed area had 31 live animals during the final authority timing sample.
One hundred `CreatureResidencyService.Tick` calls measured median 0.8963 ms, p95 1.4587 ms, and maximum 1.5587 ms.
This excludes rendering and asynchronous scatter work. The audit frame budget is a temporary editor diagnostic, not a runtime cost.

Raw results: `local-only/ecosystem-prep/full-wildlife/habitat-audit.json` and `forage-audit.json`.
The raw report uses the old display label `Placeholder Bird`; the authored library now calls that entry `Eagle`.

### Visual corrections found during review

- Bird bounds previously applied imported renderer scale twice, producing tiny birds. Bounds now use scale-aware posed vertices.
- The seagull requires the owned prefab's unkeyed joint poses. Its art-only source excludes vendor behavior components.
- Gull head and tail bindings now follow sampled joint positions. A generated folded-wing idle preserves the original clips.
- Eagle and vulture use two-leg IK with raised branch support. The gull mesh has no leg bones and uses support alignment.
- Generated drinking clips lower the muzzle through body lean and neck motion while preserving foot positions.
- Snake surface chains follow uneven ground and preserve horizontal slither motion.
- Planet ground carcasses use frozen species death poses and shared flesh removal. Skeleton geometry is combined per carcass.
- Corpse records preserve source appearance identity, including female deer, across feeding, loot, and reload.

Ground and bird review scenes expose action controls without adding gameplay state machines.
Captures are under `local-only/ecosystem-prep/full-wildlife/`.

The catalog contains goats and separate Synty dungeon ghosts.
An optional clarification asks whether Bryan meant goats, ghosts, or both.
Goat implementation proceeds independently. Supernatural habitat placement remains unconfirmed.

## Content and integration limits

The owned library supplied animal art and sound files. No vendor runtime plugin was installed.
Fox, eagle, and vulture vocal recordings were not identified. Rabbit audio currently uses grazing only.
Boar calls use restrained domestic pig snorts. Snake alerts use an original synthetic hiss.
Full audio provenance is in `Assets/AssetPacks/AnimalAudio/SOURCES.md`.

Ground locomotion, feeding, drinking, resting, sleeping, and applicable stalking share the existing animation view and procedural rig.
The gull has no leg bones in its source mesh. It uses grounded support alignment and head motion, rather than fabricated limb IK.
The skeletal scaffold remains a procedural anatomical approximation, not a species-specific skeleton asset.
Creature endurance persists in format 5 records; corpse appearance persists in format 12 records. Earlier record formats remain readable.
The habitat checks do not establish birth/death equilibrium, pack balance, or long-term water security.
The richer encounter prototype remains available for those longer simulations.
