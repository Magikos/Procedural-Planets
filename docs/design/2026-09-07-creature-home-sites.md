# Creature home sites

## Scope and acceptance

Use the existing utility decisions and FSM for return and gathering. Home sites own capacity,
resting positions, group or individual ownership, shelter metadata, and expiring local knowledge.
The fixture provides a pack den and an independent resting site; deer keep their existing home-range behavior.

Checks before acceptance:
- Ownership excludes rival groups and unrelated independent actors; reservations are bounded and stable.
- A fed actor returns and rests. Danger interrupts gathering. Exhaustion and inaccessible homes allow local rest.
- Gathering releases after its deadline even when members cannot arrive; release has a cooldown.
- Knowledge sharing does not extend expiry. Sources remain finite and authoritative.
- Runtime visits Rest, Gather, Hunt, Feed, ReturnHome, then Rest again with the full-speed deer.
- Verify the solo site stays independent and default population remains 8 deer, 3 pack wolves, 1 solo wolf.

## Implementation

ActorHomeSite and ActorKnowledge are authority-owned plain classes. Rest positions use an injected ground
normal; no wolf-specific checks live in either class. The prototype's Homes partial owns scene placement,
observation gathering, sharing range, site safety, and setting the existing CreatureSenses value.
ReturnHome and Gather append stable behavior IDs. Existing behavior IDs are preserved.

A resident returns after feeding, when tired, or when separated, subject to higher-priority food, water,
and danger. Exhaustion allows immediate local rest. A failed return route blocks that home for 15 seconds.
An available nearby pack can gather for up to eight seconds, with a two-second minimum when everyone is
present. After release it does not gather again for 30 seconds. Urgent needs can override gathering.

Resource observations expire after 90 simulation seconds. Threat observations expire after 12 seconds.
Nearby allies share observations, and residents exchange knowledge at their home site. Sharing preserves
expiry. Knowledge is capped at 32 observations. Fixture perception remains radius-based.

Pack den and solo resting site have camera buttons. Den rocks are prototype markers, not a finished cave asset.
Shelter currently influences return preference; weather protection, breeding, territory borders, persistent
home records and network replication remain future integrations.

## Verification

- 512/512 EditMode tests passed (55a70e151e1140158b6cef0127ca9f77).
- After preserving authored home elevation and extending an active return through moderate hunger,
  all 16 home, pack, and navigation tests passed (338e327e8c8648b49f35db4900be4105).
- Core and Planet builds succeeded. Final Planet build: 18 existing warnings, zero errors.
- Final 400-second full-population run: all three pack wolves visited Rest -> Gather -> Hunt -> Feed ->
  ReturnHome -> Rest at home. Completion times were 90.2, 272.05, and 101.95 seconds. Nine actors survived.
  Water, danger, and other needs can interrupt the sequence; these are not uninterrupted scripted tours.
- A home on the elevated refuge failed navigation. The wolf switched to Rest locally and rejected that
  home until simulation time 15.05, rather than flattening the elevated target onto reachable ground.
- No console errors in the final runtime check. Graphify update completed.
- Unity is paused at the pack den with eight deer, three pack wolves, and one independent wolf.

Evidence: local-only/ecosystem-prep/homes/cycle-final-400s.txt, unreachable-home.txt,
den-start.png, and build logs. The earlier cycle-400s.txt records the initial run before the return
commitment correction. Initial compilation exposed an EntityId assembly-boundary mismatch; the shared
classes now use full-width IDs as values, matching ActorGroup, with EntityId conversion in the fixture.
An old-assembly test run before that correction is not evidence for the home system.

Returning remains eligible through moderate hunger while food, water, and emergency decisions retain
priority. This avoids abandoning a return solely because hunger crosses the initial 50% threshold.
The final den appearance is still a marked prototype area; visual acceptance belongs to Bryan.

## Predator awareness correction

Prey detection no longer requires the predator to be hunting. The fixture detects nearby predators within 8 metres and retains awareness within 12 metres. Close threats within 3 metres bypass the short assessment delay. Existing disposition and decision logic chooses the response. Detection remains a distance approximation; separate sight, smell, and hearing are not implemented by this correction.

Unity runtime checks: deer fled from a drinking wolf at 2 and 5 metres; at 16 metres the deer continued drinking. Evidence: local-only/ecosystem-prep/awareness-water.txt. Core and Planet builds passed with 18 existing warnings and zero errors. Graph update completed.

