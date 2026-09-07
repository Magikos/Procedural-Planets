# Wildlife landing targets

Status: Authored scene sites, a snapshot registry, exclusive claims, and resident bird support are implemented.
Scatter socket publication and pollinator migration remain proposed. For tested bird behavior,
see [bird landing](2026-09-05-bird-landing.md).

## Current behavior

`AmbientSwarms.ScanFlowers` reads live scatter draw buckets once per second.
It selects prototypes classified as flowers and retains the nearest 64 instances within 30 metres of the observer.
Each target is the world-space top center of the combined render mesh bounds.
An insect selects an unreserved flower and retains the scatter instance ID while traveling or resting.
Missing targets cause retargeting or retirement. Butterflies can choose ground; bees require flowers.
The free camera selects the residency area but does not become an entity threat.

`WildlifeLandingSite` now decorates an authored scene contact point beneath a `Planet`.
`WildlifeLandingTargets` holds scene snapshots and exclusive bird claims.
Flower selection and reservation still live inside `AmbientSwarms.Pollinators.cs`; insect migration remains follow-up work.

Resident birds use `CreatureBrain.Perch` and `FlightGrounding`.
They now select dry, gentle terrain ahead and approach it before resting twelve seconds after actual ground contact.
They also prefer suitable authored branch or rail sites. Regression and controller probes cover landing and takeoff.
Ambient animated model groups remain a separate distant population and do not land.

## Shared target contract

Share target discovery, suitability checks, reservation, and invalidation between species.
Keep flight, steering, body clearance, animation, and takeoff behavior in the species controllers.

Each target needs:

- An owner identity plus a socket index. One branch or flower cluster can expose multiple distinct positions.
- A local position, surface normal, and preferred facing direction, transformed by its owner.
- Supported uses: nectar feeding, insect resting, or bird perching. A nectar flower can also support insect resting.
- Physical clearance for the visitor. A branch suitable for a small bird need not support a large bird.
- One reservation owner. Begin with one visitor per socket; expose more sockets for larger surfaces.

Do not identify targets by world position or a draw-bucket index. Both change during movement and streaming.
Do not merge scatter IDs and entity IDs into one untyped integer namespace.
Represent the owner domain explicitly when forming target and visitor identities.

## Authoring more targets

For instanced vegetation, add local landing sockets to the existing scatter prototype authoring data.
Copy those sockets into the prototype's immutable runtime snapshot, following the existing settings pattern.
Transform sockets using each scatter instance transform. This does not require a GameObject for every plant.

For regular scene objects, a small landing-site component can expose equivalent sockets.
The world owner registers and unregisters those sockets through the existing lifecycle.
Do not add a second global initialization or service discovery path.

| Owner | Authored target | Typical visitor |
| --- | --- | --- |
| Flower or flowering shrub | Nectar point above an actual blossom | Bee or butterfly |
| Leaf, rock, fence, or prop | Insect resting point | Butterfly |
| Branch, fence rail, or rock ledge | Bird perch with approach clearance | Resident bird |
| Terrain | Sampled ground point with slope and water rejection | Butterfly or ground-landing bird |

Keep the current flower-bounds heuristic as a fallback for unannotated flowers.
Do not infer branch perches from a tree's bounding box: its top center can be empty space inside foliage.
Sample terrain targets on demand rather than registering an unbounded collection of ground points.

## Registry and lifecycle

The planet/world owner injects one landing-target registry into pollinator and resident-creature controllers.
Providers publish targets from live world instances. The registry queries by range, supported use, and clearance.
For current pollinators, the existing scatter cache is the available source.
For shared resident birds, target availability must follow world residency rather than camera visibility alone.
Audit the scatter publication boundary before moving resident birds onto these targets.

A visitor reserves its destination before departure and releases its previous destination.
It releases the reservation when it leaves, retires, dies, or unloads.
Harvest, destruction, streaming eviction, and world teardown invalidate the owner's targets and reservations.
Invalidation triggers a new destination, safe takeoff, or retirement; it must not leave a visitor suspended over a removed branch.
The current bird implementation invalidates a moved scene socket and takes off. Following moving supports remains future work.

Use a bounded local query first. Add a spatial index only if measured resident populations require it.
Avoid arbitrary reservation expiry while a valid living visitor still owns the target.

## Birds use their own movement

`FlightGrounding` now supports an elevated authored contact plane alongside terrain support.
A branch landing must approach a reserved position, check clearance, then use that target as its support.
The rest clock starts after actual arrival. Threat response releases the perch and returns to flight.
Keep bird intent and authority in `CreatureBrain` and `CreatureResidencyService`; do not route resident birds through ambient insect motion.

Ambient animated bird groups remain a distant visual population. Their controller does not reserve landing targets.
Any later conversion between distant particles and resident birds must avoid duplicate populations and visible replacement jumps.

## Later player interactions

A still player can expose a temporary insect-rest socket using the real player entity identity.
Movement withdraws that socket and prompts takeoff. The free camera never publishes one.
The same owner-transform contract supports a hand or shoulder without a special reservation system.
Player landing remains deferred, as Bryan requested earlier.

## Implementation order and checks

1. Extract existing flower discovery and reservations into the shared target contract while preserving current behavior.
2. Add authored scatter and scene sockets, with diagnostics showing owner, target use, reservation, and world position.
3. Add explicit bird approach and target support through resident bird movement.
4. Add player sockets when player interactions enter scope.

Regression checks must cover competing visitors, multiple sockets on one owner, target movement, harvest, unloading, and teardown.
Check species filtering, clearance, translated planets, and reservation release during interrupted flight.
Unity visual checks must show actual contact on blossoms and branches, safe approach and takeoff, and no camera-driven threats.
