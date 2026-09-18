# Creature stamina and fatigue

Status: Implemented in the shared actor model and encounter fixture. Simulation checks and 420/420 final regression tests passed.
Current next action: Review encounter balance, then integrate persistent world actor reserves alongside the existing survival migration.
Baseline: Dirty `harvest-vertical-slice` at `d1e0f62`, 2026-09-07. Preserve concurrent weather and wildlife changes.

## Rules

Bryan approved stamina, long-term fatigue, sleep recovery, and bounded deprivation penalties. Ordinary hunger must not prevent hunting.
The implementation extends the existing creature brain and utility selection. It adds no FSM, networking package, or vendor runtime.

`Assets/Scripts/Game/Actors/ActorEndurance.cs` owns stamina, capacity, fatigue, and recovery thresholds without Unity dependencies.
The host supplies elapsed simulation seconds, needs, exertion, and an immutable duration profile.
Construction supports restoring all five state values. Presentation only reads them.

| Setting | Initial prototype value |
|---|---|
| Deer full-reserve sprint budget | Eight seconds |
| Wolf full-reserve sprint budget | Twenty-two seconds |
| Awake fatigue accumulation | Full fatigue after 360 seconds; running doubles accumulation |
| Sleep fatigue recovery | Full fatigue removed over sixty seconds |
| Stamina recovery | Sixteen seconds resting; fifty seconds walking, before mild condition penalties |
| Exhaustion entry / recovery exit | 10% / 65% of effective capacity |
| Required sleep entry / exit | 80% / 25% fatigue |
| Optional sleep continuation | Enter when resting at 35% fatigue; continue toward 15% unless another need interrupts |

Capacity loses up to 15 percentage points from hunger above 85%, 20 from thirst above 80%, and 25 from fatigue.
Combined penalties stop at 55% capacity. Capacity changes by at most eight percentage points per simulation second.
Food does not grant stamina. Rest and sleep refill available capacity; food changes the future capacity target.
Even severe hunger permits positive resting recovery. Deprivation can still kill when food remains unavailable.

Run speed falls smoothly below 40% remaining reserves, reaching 40% normal run speed at 10% reserves.
Immediate danger still permits flight when exhausted. Predators cannot initiate another hunt while recovery or required sleep remains active.
Nearby food and water can outrank rest. The host measures actual movement before assigning exertion, so stationary attack recovery does not drain sprint reserves.

## Decision correction and source memory

The previous maximum-hunger scores were 1.05 for food and 1.00 for hunting. The 0.12 switching margin could retain an active hunt.
Available food now scores `1 + hunger * 0.5 - travelCost`. Water uses `1 + thirst * 0.55 - travelCost`.
Travel cost is distance times 0.012, capped at 0.9. This permits switching to nearby carrion without favoring a distant remembered corpse over close prey.
The existing commitment interval, switching margin, failure memory, and threat override remain intact.

The fixture now retains its selected known source after leaving the 25-metre discovery radius. It forgets sources when stock or eligibility disappears.
This fixes animals forgetting water during a chase. It is not a full perception memory or navigation implementation.
The display reports stamina/capacity, fatigue, recovery, required sleep, and the wolf's known food ID, stock, and distance.

## Observed simulations

These are accelerated fixture observations, not wildlife measurements or proof of long-term ecosystem balance.

- Default expanded scene: a full-speed deer exhausted its reserves. By sixty seconds, the wolf had landed two hits and was feeding.
- At sixty seconds, corpse meat fell to about 0.61 from 1.5. Wolf hunger fell to 20%, with stamina about 90%.
- A four-minute run retained four living actors: three deer and the wolf. Plant stock eventually reached zero; water remained available.
- Stress case: wolf began at 100% hunger, 5% stamina, and 90% fatigue. It rested, slept, then resumed stalking at forty-seven seconds.
- The stress wolf remained alive through two minutes and attempted multiple pursuits without a guaranteed kill.
- Carrion case: a wolf already stalking received a nearby corpse while at maximum hunger. It switched to Feed within three seconds.
- After fifteen more seconds, corpse meat fell from 1.5 to 0.44 and wolf hunger reached 10%. No further hunt was required.

Evidence lives under `local-only/ecosystem-prep/`: `endurance-final-240s.txt`, `endurance-stress-transitions.json`, and `endurance-carrion-switch.json`.
An initial run exposed forgotten water sources. Its earlier results remain in `endurance-default-180s.txt` for comparison.
The first regression run passed 420/420 EditMode tests, including seven endurance tests, job `2609148079bf48c8af4a2d47486846e5`.
The final run passed 420/420, job `0065b8648ec847df934098867ffecf35`, after source-memory and distant-resource scoring changes.
The final four-minute replay still produced one hunted deer and four living actors. The wolf resumed stalking with 67% stamina capacity.
The Planet build passed with zero errors and eighteen existing warnings. Graphify updated successfully.
No runtime exceptions appeared in the final checks. Unity is paused one second into the reset expanded scene.
The final display capture is `local-only/ecosystem-prep/captures/endurance-ready.png`.

## Limits

World persistence does not yet save these reserves. The fixture resets them on restart, as it resets its existing needs and corpses.
Production hosts must supply recovery/sleep observations and apply the endurance speed limit when adopting this model.
These changes do not add pack hunting, scent propagation, obstacle reachability, plant regrowth, or a guarantee that predators survive.
Known-source memory currently retains one selected food source and one water source per actor. Shared stock remains authoritative in the fixture.
Values are initial gameplay settings. Repeated tests across terrain, populations, and food supply remain necessary before final balancing.
