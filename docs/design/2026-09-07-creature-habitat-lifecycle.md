# Creature habitat and lifecycle

Bryan approved a staged program: shared runtime integration, a planet habitat, renewable resources, aging, reproduction, and population validation. Existing animal behavior must remain available. This document records the implemented foundation and the unfinished integration work separately.

## Implemented foundation

- `ActorResourceSource` now supports capacity-limited renewal. Existing constructors retain finite, non-renewing stock. Carcasses do not regrow meat.
- `ActorLifeCycle` holds age, individual death age, sex, parent identities, mate identity, gestation, and reproduction cooldown. Its host supplies elapsed game days.
- `ActorLifeProfile` supplies shared defaults. `CreatureLifeSettings` exposes species-specific authoring values and creates an immutable profile snapshot.
- Juveniles grow from 55% to full size. Juveniles and elders have reduced movement and combat strength. Visuals currently scale the existing adult models; dedicated juvenile meshes and antler development are not implemented.
- Reproduction requires an adult female, a detected nearby adult male of the same test species and group, adequate health and reserves, and no immediate threat or hunt. Direct parent/offspring and shared-parent pairings are excluded.
- `ActorHabitatCapacity` calculates a population budget from daily usable nutrition and freshwater, retaining a 25% reserve. The test host uses nearby route-reachable renewable sources for birth eligibility. Predator birth budgets use half the potential recruitment yield of adult female prey. This is an estimate, not proof of sustainable predation.
- Pregnancy reserves population capacity. Existing pregnancies can complete after shortages begin. New pregnancies require enabled food, water, and renewal. The 64-actor limit gates conception; it never deletes existing animals or pending offspring.
- Young animals use remembered mother positions with the existing home-return objective. This is basic following, not nursing, food sharing, adoption, or complete parental care.
- Cause-of-death counters distinguish attack, bleeding, hunger, thirst, and old age. The HUD reports births, consumption, critical needs, and missing source knowledge.
- `ActorKnowledge` still holds at most 32 entries. Under pressure, 24 places remain available for moving contacts and eight for sources. Unused space can serve either category. Long resource expiry no longer excludes a fresh predator observation.

## Test habitat

`Assets/Scenes/Tests/CreatureHabitat.unity` uses the existing encounter host and shared FSM. Its initial population is explicitly configured: 12 deer, three pack wolves, and one solo wolf. Initial placement is not yet derived from a world habitat survey.

The original encounter remains available. The `Renewable habitat` button enables this preset. The preset uses 320 simulation seconds for full hunger growth, 240 for thirst, and 120 seconds per game day. These are test values, not biological claims or final balance.

Sources cover retreat destinations across the existing navigation course. Stock renews from the same game-day clock used by lifecycle and decay. Source quality still distinguishes freshwater from saltwater.

`AnimateActors` can disable animation evaluation during accelerated checks. Movement, brains, perception, combat, needs, and decay continue. This does not make the host headless: it still owns scene transforms and navigation.

## Evidence and limits

The initial central-source layout produced critical thirst despite ample total stock. Distributed sources address that spatial coverage issue. A preliminary distributed run reached 2.33 days with ten natural offspring and 24 living animals after two combat deaths. At three days, three combat-related deaths had occurred. Some survivors had critical needs. This was before the memory-capacity correction and is not a final balance result.

Evidence in `local-only/ecosystem-prep/perception`:

- `habitat-2days.txt`, `habitat-2_8days.txt`: preliminary population snapshots.
- `habitat-offspring.png`: scaled offspring in the test habitat.
- `habitat-boundaries.txt`: dry habitat blocks conception, disabled food blocks predator conception, and old-age death creates a carcass.
- `habitat-core-build.log`, `habitat-planet-build.log`: successful builds with existing warnings.

The first full EditMode run passed 528 tests before the memory regression test was added. Test job: `0f97a6c94f8f4ed6a12ef1dfbf920df2`. Tests cover reproduction conditions, gestation, cooldown, state reconstruction, age death, renewal limits, capacity limits, and a 100-day resource-stock calculation. That calculation does not simulate navigation or predation.

After the memory regression correction, all 529 EditMode tests passed (job `8b1da18a467540c2961fce9fa5a88b5a`). The final build passed with 18 existing warnings and no errors. Newborn visual, combat, perception, and lifecycle profiles now come from the restart snapshot rather than re-reading authoring assets during births.

The replay also exposed persistent selection of distant previous resource targets. Source selection now scores remembered distance, uncertainty, and nearby remembered danger. A 20% preference for the previous source limits switching. The runnable source-selection probe verifies closer-source replacement, danger avoidance, and retention when an alternative is only 10% closer.

Final runtime replay, after both survival corrections: 2.17 game days, six natural births, two attack deaths, 20 living animals, and no hunger or thirst deaths. Food consumption reached 7.0 nutrition units and water consumption reached 12.8. Two survivors had critical hunger and one had critical thirst; all three knew a relevant source. Animation evaluation was disabled during the accelerated replay, then restored. No runtime exceptions were reported. This remains a short run and does not establish long-term balance.

Final evidence is in `local-only/ecosystem-prep/habitat`: `final-2days.txt`, `source-selection.txt`, `source-selection-probe.cs`, and `build.log`.

## Remaining approved work

1. Reconcile the world clock. Planet residency and existing corpse records use Unix timestamps; this fixture uses elapsed game days. Add a versioned migration path without reinterpreting existing timestamps. Preserve existing respawn behavior as an explicit mode while introducing natural reproduction.
2. Move the remaining encounter-host simulation into reusable authority code. Keep scene controls, animation, and debug drawing in presentation. Do not create another FSM or remove bird landing, disguises, persistence, combat, or current navigation behavior.
3. Integrate the shared perception and survival path into `CreatureResidencyService`. Connect local daylight through the celestial service. Keep the existing terrain-screening contract and separate observers from threat identities.
4. Connect world food and freshwater queries. Survey usable, reachable supply before choosing initial populations. Distinguish unavailable resources from failed detection, blocked routes, and unsafe access.
5. Persist newborn identities, lineage, age, gestation, cooldown, injuries, endurance, and consumed resource stock through the existing world delta system. Constructor reconstruction tests are not a save/load implementation. Fixture identities currently last only until restart.
6. Run longer population tests across multiple starting conditions, then test unloading and save reload. Measure frame cost as population increases. Short survival runs cannot establish long-term carrying capacity.

The real-planet migration and full lifecycle persistence are not complete in this slice.
