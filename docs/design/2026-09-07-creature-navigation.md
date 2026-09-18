# Creature navigation prototype — 2026-09-07

Status: imported and exercised in Unity after Bryan released editor control.
The initial preparation phase left Unity untouched. Validation results follow below.

## Implemented scope

- Reuse Unity's installed native NavMesh builder and queries. No package installation.
- CreatureNavigation owns per-actor route state, half-second replans, complete-path checks,
  strict height matching, and a four-second movement-progress deadline.
- The fixture routes hunting, food, water, escape, and wandering movement through navigation.
  Movement cannot cross a NavMesh boundary. Attack contact also requires a clear route segment.
- The existing utility decision system rejects failed objectives for 15 seconds.
  The fixture skips that target during the rejection period and can select another source or prey.
- Unreachable threats disable both contested hunting and counterattacks. Escape remains an emergency.
- Escape tries forward and side routes before reporting a blocked retreat.
- The obstacle course contains a barrier, a passage, and a three-metre elevated refuge.
  Its navigation surface is bounded to 80 by 80 metres and samples the existing prototype ground.
  It owns and removes only its own NavMeshData instance.
- HUD controls toggle obstacles, place the first deer on the refuge, and simulate an attack from there.
  The refuge attack control places the wolf nearby and applies five damage with the deer as attacker.
  These are explicit diagnostics, not new deer climbing or ranged-attack abilities.
- Actor status includes the route result. Existing no-obstacle behavior remains available.

This is a local obstacle prototype. Production planet navigation, streamed navigation patches,
per-species clearance, real perception, and pack coordination remain separate work.
The brain consumes reachability values; it does not call NavMesh or depend on a GameObject.
The fixture still owns movement and damage authority. No client simulation or network transport was added.

## Compile evidence

Core and Planet initially compiled. The final full dependency build encountered another agent's
new console type missing from the generated project:

`error CS0246: The type or namespace name 'ConsoleReleasePolicy' could not be found (are you missing a using directive or an assembly reference?)`

The isolated Planet build passed with zero errors and 18 existing warnings against built dependencies.
A local-only MSBuild target includes new navigation files without asking Unity to regenerate projects.
No Unity test results are claimed for this change.

## Queued validation — run after Bryan releases Unity

1. Refresh/import scripts and regenerate projects. Build Core, then Planet serially.
2. Run the complete EditMode assembly, including CreatureNavigationTests.
3. Open WolfDeerEncounter. Restart with obstacles enabled; verify paths around the barrier,
   passage clearance, grounded feet, successful feeding/drinking, and no wall-crossing attacks.
4. Run the ecosystem for 240 seconds. Record routes, hits, living counts, health, and minimum body gap.
5. Use Deer on refuge. Verify the wolf cannot reach the top and abandons the target.
6. Use Refuge attack test. Verify retreat instead of repeated hunting, defending, or running into the rock.
7. Verify alternative escape routes and the blocked-retreat response. Check the four-second no-progress deadline.
8. Toggle obstacles off and restart. Verify the original encounter, carcass consumption, healing, and camera controls.
9. Check narrow-window HUD layout and capture overview, barrier routing, and refuge retreat.
10. Check runtime errors and repeated Restart teardown. Leave the normal ecosystem ready for Bryan.

The native route test builds isolated navigation at (200, 0, 200), checks obstacle routing,
blocked contact, boundary clipping, elevated-target rejection, and stalled-route failure.
Decision tests cover unreachable attackers and resource-specific rejection.


## Unity validation results

- EditMode: 465/465 passed, job `4abfaafd0c334cffa0d68114eeada0f7`, including the three navigation tests.
- Full Core and Planet builds now succeed. The temporary console compilation issue is resolved.
  Final Planet compile: zero errors, 18 existing warnings.
- Runtime inspection found navigation overwrote defensive facing. The fixture now preserves combat turns
  and committed attack steering while still clipping displacement at navigation boundaries.
- The cached HUD style lost its white text after reload. The HUD reapplies text colors each draw.
  The final refuge capture shows readable text again.
- With obstacles: the cornered-pair check recorded seven hits by 30 seconds. Both actors remained alive;
  deer health was 6.6/100 and wolf health was 51.2/90. Both sides dealt damage.
- Final ecosystem run: 240 seconds in 0.05-second steps, all five actors alive, plants consumed,
  water reduced to 11.69 units, minimum living center gap 1.196687 metres. No attacks landed in this run.
  Wolf health fell to 70/90 from deprivation. This is not evidence of balanced hunting success.
- Refuge attack: wolf moved from (4, 0.40, 9) to about (14.97, 0.60, 10.98) over 15 seconds.
  It exited combat with zero attack hits. The deer remained on the refuge.
- Obstacles disabled: eight hits, a carcass with 61% meat left, and a living/resting wolf at 53.3/90
  after 40 seconds. Existing feeding and recovery paths remain active.
- Three repeated resets completed without runtime console errors.
- Native route tests verify barrier detours, wall contact rejection, boundary clipping, elevated-target
  rejection, and stalled-route failure. Full authored-course escape enclosure testing remains unverified.
- Visual inspection used the current wide Game view. Narrow-window layout and detailed foot motion
  still need user review. No production spherical/streamed navigation validation is claimed.

Evidence: `local-only/ecosystem-prep/navigation-final-240s.txt`, `navigation-combat.txt`,
`navigation-refuge.txt`, `navigation-disabled-regression.txt`, and captures under `captures/`.
The scene is reset to the normal ecosystem with obstacles enabled and paused for Bryan.
