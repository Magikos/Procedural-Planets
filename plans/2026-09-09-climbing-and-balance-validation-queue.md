# Climbing and balance validation queue

**Status:** Written backlog only. No scheduled execution or automatic Unity access.
**Baseline:** `harvest-vertical-slice`, `d1e0f62` plus uncommitted actor work, 2026-09-09.
**Design:** [Climbing and balance traversal](../docs/design/2026-09-09-climbing-and-balance-traversal.md).

Another agent owns Unity. No scripts, assets, imports, builds, tests, or captures changed during this design pass.
The tests below are proposed work. They do not exist merely because this queue names them.

## Handoff and execution order

1. Confirm the current Unity owner has released the Editor before any Editor action.
2. Read the design and recheck current actor code, scene assignments, and dirty files.
3. Record the new baseline and coordinate ownership of shared source files.
4. Implement only the next traversal slice and its meaningful tests.
5. Build Core, then Planet, using the current project build instructions.
6. Refresh Unity and resolve compilation errors before running Editor tests.
7. Run targeted fixtures serially and record test job IDs with full failures.
8. Capture the new movement and existing traversal regressions.
9. Record Bryan's visual review separately from automated results.

Do not run stale compiled assemblies after a failed import or compile.
Do not import scratch assets until the handoff. Importing can interrupt the other agent's Editor work.
No console command changes are required for the initial slice.

## Queued checks

| ID | Slice / check | Required observation |
|---|---|---|
| C01 | Left/right ledge transfers | Leading hand acquires before trailing release; root follows a swept continuous path |
| C02 | Ledge end and unreachable grip | Actor retains the hold; no drift, jump, or snap |
| C03 | Up/down wall holds | Independent hands and feet obey the selected contact requirements |
| C04 | Missing lower hold | Down input keeps the current hold |
| C05 | Reach and anatomy limits | Reject impossible limb targets without stretching bones |
| C06 | Inside/outside corners, both directions | Body and limb clearance remain valid throughout rotation |
| C07 | Adjacent ledge gap | Held directional input cannot trigger an unrequested leap |
| C08 | Wall collision during a jump | A newly reachable ledge can be caught during descent |
| C09 | Top/bottom transitions | Pull-up and Ctrl+Space descent preserve existing behavior |
| C10 | Camera free look | RMB changes the view without moving loaded contacts or reversing climbing controls |
| C11 | Occupied or wounded hand | Technique eligibility changes; animation cannot mutate inventory |
| C12 | Proficiency change during transfer | Selected technique stays latched until a valid boundary |
| C13 | Replayed skill attempt | Same attempt state produces the same outcome; no per-frame random rolls |
| C14 | Moved, destroyed, or edited support | Revalidation updates supported motion or enters controlled recovery/fall |
| C15 | Death, ragdoll, water, reset | Contacts clear once; no stale hold or immediate unintended recatch |
| C16 | Gravity direction and rig scale | Direction, reach, and collision checks remain consistent |
| C17 | 30/60/120 Hz and a long tick | No skipped contact acquisition or tunneling; measure equivalent outcomes |
| C18 | Skipped presentation frames | Authority and contact decisions remain unchanged |
| B01 | Beam entry and exit | Actor reaches support through movement; no centerline snap |
| B02 | Forward/backward, stop, reverse | Planted feet hold; steps remain on valid support |
| B03 | Beam end, obstacle, occupancy | Stop or use an explicit valid exit; do not pass through actors |
| B04 | Balance recovery and failure | Pose shows measured balance state; gameplay owns recovery/fall |
| B05 | Moving beam | Support-local contacts follow validated motion without world-anchor jumps |
| B06 | Flexible tightrope, later slice | Rope contact, collision, sag, and rendering agree at the same simulation time |

Add geometry and phase tests beside existing `ActorTraversalTests` and `ActorLedgeCatchTests`.
Add contact continuity tests beside `ProceduralPoseTests` and `FootStepContinuityTests`.
Use separate climbing or balance fixtures when their scenarios no longer fit the existing fixtures.
Confirm exact test paths with `rg --files` before editing; do not duplicate an existing helper.

## Clip sampling queue

Sample the selected clips on the review actor before tuning IK.
Record duration, loop closure, root displacement, contact intervals, and hand/foot reach.
Check corner clips as one-shot actions despite current source loop settings.
Verify both directions independently. Mirroring is not proof of valid contact geometry.

Keep existing gameplay root authority. Apply clip displacement exactly once through the approved traversal motion path.
Use IK for contact correction within limb limits. Replace an unsuitable clip instead of forcing large corrections.

## Regression queue

The preceding stair pass reported 96 passing targeted tests.
That result predates this feature and does not validate climbing or balance.
Recheck fixture contents before treating that count as the current baseline.

Run the applicable existing fixtures after each shared motor or contact change:

- `ActorTraversalTests`
- `ActorLedgeCatchTests`
- `HumanoidAnimationTests`
- `HumanoidStairAnimationTests`
- `FootStepContinuityTests`
- `ProceduralPoseTests`

Include performance selection/playback tests when those owners change.
Capture walk/strafe, stairs, crawl transitions, vault approach, catch/pull-up/drop, and swim entry after the combined slice.
Preserve movement buffering and the rule against increasing airborne speed with Shift.

## Evidence and acceptance

Store captures and numeric traces under `local-only/actor-performance/<execution-date>/climbing-balance/`.
Record the actual branch, commit, dirty source scope, Unity version, actor rig, clips, and simulation timestep.
Capture front and side views for each direction, corner, and balance entry/exit.
Include at least one interrupted transfer and one rejected reach.

Measure planted-contact error, bone length, root displacement per tick, penetration, query count, and allocation cost.
Initial acceptance targets are settled contact error below 2 cm and no unaccounted root displacement.
These are proposed thresholds, not measured results. Validate their suitability against rig scale before locking them.
Compare root movement against authorized speed and timestep, rather than a fixed per-frame distance limit.
Record baseline and changed CPU costs under the same actor count and capture conditions.

A passing test run does not establish visual quality.
Leave visual acceptance pending until Bryan reviews the captures or plays the scene.
Update this queue with actual evidence after execution. Never mark an unrun row as passed.
