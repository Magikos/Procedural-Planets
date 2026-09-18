# Planet animal resources

## Implemented boundary

Planet wires `PlanetCreatureResources` into the existing residency service. Ground animals use the existing
`CreatureBrain` feeding and drinking states. Test-scene markers are not involved.

- `ScatterPrototype.FoodUnits` opts foliage into herbivore food. Zero remains inedible.
- `FoodRegrowSeconds` controls full replenishment. The first authored sources are six existing bush prototypes,
  with two hunger units each and 1,800 seconds for full regrowth. These are initial balance values.
- `CreatureSpecies.Diet` selects plants, meat, freshwater, or saltwater. `ConsumeUnitsPerSecond` controls intake.
- `FoliageFoodStore` uses `ActorResourceSource` for consumption. It lazily replenishes stock and records only
  consumed instances. `DeltaKind.FoliageFood` occupies a separate compaction key space from harvest records.
- The host checkpoints food every 30 simulation seconds and during its existing save/unload flush.
- Regrowth uses the existing residency authority clock, passed explicitly. It does not read renderer lifetime.
- Water queries use `IWaterQueryService`, including solved lake levels and ocean identity. Animal volumes do
  not deplete whole lakes or oceans.
- Searches refine a dry bank position beside water. A sampled direct corridor rejects steep and submerged routes.
- Animal perception profiles gate sight and scent. Solar direction supplies local daylight. Threat perception
  remains on the existing planet threat registry; this change does not replace that system.
- Each host tick permits one resource search. Each actor searches at most once per five simulation seconds.
  Searches cover at most 24 metres and gather only edible scatter prototypes through existing placement code.
  Foliage gathering uses `Awaitable.BackgroundThreadAsync` and the existing background-safe scatter core.
  Only one query is pending per host. Consumption and result publication stay on the main thread. World epochs
  reject results after regeneration or teardown. Species changes and retired actors also invalidate pending results.
- `creature.status` reports consumption and the latest search time.

## Performance correction

The habitat fixture performed physics linecasts before checking range and field of view. `ActorPerception.WithinRange`
now provides a shared broad-phase rejection before occlusion queries. It preserves strength-scaled hearing range.
No asynchronous state mutations were added. Physics queries remain on Unity's main thread.
The initial planet foliage query measured 17.92 ms on the main thread, versus 0.41 ms for water. This motivated
moving the existing pure placement work to a background thread. Burst conversion is not required for the first
bounded query; a spatially shared cache or the existing Burst batch gather can follow if throughput needs it.

Initial paused-editor measurements: perception 38.92 ms; total step 42.50 ms. A reset habitat after the change
measured perception 4.34 ms over 120 calls and total step 12.78 ms over 20 calls. These populations and positions
differ, so this is diagnostic evidence, not a controlled speedup claim or rendered-frame benchmark.

## Validation requirements

`PlanetCreatureResourceTests` checks stock reload/regrowth, harvest-key isolation, freshwater/saltwater selection,
bank placement, consumption revalidation, and strength-scaled perception culling. Runtime validation must show
the generated planet supplies resources, without prototype scene objects.

Final validation on 2026-09-07:

- Core and Planet builds passed. Planet reports 18 existing warnings and no errors.
- All 532 EditMode tests passed after the placement snapshot correction.
- The first asynchronous run failed with `get_bounds can only be called from the main thread.` Placement
  rules and gather radii now capture those mesh-derived values during main-thread configuration. The corrected
  planet run reported no resource-search failures.
- Background and synchronous edible gathers returned the same 26 instance IDs at the sampled planet position.
- Main-thread resource selection measured 0.776 ms there, after background placement completed.
- A live planet deer entered `Feed` beside a generated Steppe Bush. It consumed 0.168 hunger units; hunger fell
  from 0.9 to 0.733, including hunger growth during the test. The probe moved the deer into feeding reach.
- A generated lake (body 2) supplied a reachable freshwater bank. A consumption probe reduced thirst from 0.8
  to 0.68. This verifies the real query and consumption path; it is not an end-to-end deer travel test.
- The Planet scene contained zero `PredatorEncounterPrototype` components.

## Limits and next integration

- This connects ground-animal resource behavior. It does not move the full pack/lifecycle fixture onto the planet.
- The corridor test is conservative terrain sampling, not a global navigation graph. It rejects routes requiring
  a detour and does not account for placed-object obstacles. Spherical obstacle navigation remains separate work.
- Airborne feeding, carcass nutrition, plant-specific diet categories, and visible bite/regrowth states remain pending.
- Water scents locate a bank within the searched area; wider uncertain-area investigation needs further integration.
- Food stock survives streaming and clean saves. Up to the checkpoint interval of recent bites can be lost on a crash,
  matching the existing animal checkpoint policy. Migration to an absolute saved game clock remains pending.
- The initial food values do not establish population carrying capacity. Long-duration planet balance runs remain necessary.
