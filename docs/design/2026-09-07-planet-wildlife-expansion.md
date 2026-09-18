# Planet wildlife expansion

## Scope

Planet now registers deer, rabbits, wolves, boar, foxes, birds, and vultures.
Rabbit, boar, and fox presentation uses owned Polyperfect art and authored animation clips.
No vendor runtime scripts, controllers, or demonstration prefabs were installed.

The original population used three deer slots per territory and excluded grassland.
The original Planet library did not register wolves.
The character controller already reports its position through the threat registry.

## Population and saved identities

Existing species indices and legacy slot ranges remain unchanged.
AdditionalSlots assigns explicit persistent addresses without increasing earlier PerTerritory values.
The planner validates addresses once when settings change and caches the accepted slots.

| Species | Legacy slots | Additional slots | Habitat changes |
|---|---|---|---|
| Deer | 0–2 | 32–35 | Added grassland, scrub, steppe |
| Rabbit | 3–8 | 40–45 | Existing open habitats retained |
| Bird | 9–12 | None | Unchanged |
| Vulture | 13–14 | None | Unchanged |
| Wolf | None | 15–17 | Forest, taiga, grassland, scrub, steppe |
| Boar | None | 18–20 | Forest, taiga, grassland, scrub, steppe |
| Fox | None | 21–22 | Forest, taiga, grassland, scrub, steppe |

New species use explicit addresses. Future additions must choose unused addresses.
Do not increase legacy PerTerritory values in an existing save format.
Conflicting or invalid addresses produce diagnostics and do not spawn duplicate identities.

## Shared behavior

Ground actors use ActorPerception and ActorKnowledge for sight, hearing, smell, and remembered threats.
The existing CreatureBrain handles escape, vigilance, stalking, pursuit, attacks, and feeding.
Vigilance delays ordinary activities after a threat disappears; critical needs can interrupt that delay.
Predators prefer detected, reachable carcasses over live prey.
Live prey selection respects faction relations, perception, body size, and reachable terrain.
Attacks use the existing contact checks and execute after resident iteration.
Carcass feeding changes hunger and persistent meat stock, independently of hide loot.

## Cost controls

- Animal sensing runs at a half-second cadence per resident.
- Range checks precede terrain sight checks.
- Resource searches retain the bounded asynchronous scatter path.
- Carcass feeding writes at most once per second per feeding animal.
- Presentation uses hash sets for stale actor and carcass membership.
- Animal audio uses one eight-voice pool, distance limits, and staggered calls.

## Content and migration limits

The new models have authored idle, locomotion, and death clips.
Boar and fox also have attack clips. Rabbit has a lookout clip.
These models do not yet have procedural foot bindings or authored eating, drinking, and sleeping poses.
Their missing action clips use the existing idle fallback.
Fox and boar calls were not identified in the owned audio search.
Rabbit has quiet feeding audio, but no identified rabbit vocal recording.

Planet still has the legacy integer health scale and simple corpse presentation.
The richer habitat fixture retains pack coordination, wounds, fatigue, reproduction, and lifecycle diagnostics.
This pass does not claim that those fixture features have all migrated to Planet.
Planet pursuit uses the existing 20-metre hunt boundary and a conservative terrain corridor.
Placed obstacles and full spherical route planning require a later navigation integration.

## Review tools

`creature.status` reports accepted slot counts, live species counts, and the nearest animal.
`creature.goto` uses the existing wildlife viewpoint command.
`CreatureAnimalAssetAuthor.Build()` creates missing project presentation assets.
`ConfigurePlanetLibrary()` applies the explicit library expansion.

## Validation

Core and Planet builds pass. Planet reports 18 existing warnings.
All five ground species have valid imported Animator avatars and assigned locomotion clips.
Final Unity EditMode run: 552 passed, zero failed or skipped.
Job: `b97f016eae29472c9f1382a6e38a2de7`.
All assigned ground idle, walk, and run clips loop.
Geometry sampling exposed oversized importer bounds on rabbit and fox. Their scale now uses posed mesh height.

The loaded Planet area contained 53 live animals: eight deer, 21 rabbits, seven wolves,
five boar, three foxes, eight birds, and one vulture. The nearest rabbit was 29 metres away.
That distance uses surface distance; the initial debug camera was above the terrain.

A paused authority-only sample used 100 steps of 0.05 seconds with 54 live actors.
It measured median 1.0393 ms, p95 1.6565 ms, and maximum 2.5549 ms.
This excludes rendering and completion of background resource work. It is not a whole-frame benchmark.
The sample preceded the final stable sensing-phase spread.
Audio reported one played voice and zero dropped voices in that observation window.

The runtime model review used temporary objects, removed before leaving play mode.
Captures are under `local-only/ecosystem-prep/expansion/`.
The Planet rabbit capture demonstrates that dense foliage and dark lighting can still conceal small animals.
