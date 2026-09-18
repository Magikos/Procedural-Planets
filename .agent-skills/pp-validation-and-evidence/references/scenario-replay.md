# Record and replay a validation scenario

Use the [scenario template](../templates/scenario-record.md) inside the existing plan or evidence record.
Keep large captures and logs in the established local evidence folders; link exact files from the record.
For cross-machine handoffs, state which local files the receiving agent cannot access.

## Before the first run

1. Name the claim and define pass conditions before measuring it.
2. Record the scene or fixture, code revision, relevant dirty changes, and Unity version.
3. Record the initial state and how to restore it. Identify persisted state that a fixture reset retains.
4. Record the world and planet seeds when relevant. Include seed values for other random sources if the fixture exposes them.
5. Record the subject location and camera pose separately. Include coordinate space and planet identity for spherical scenes.
6. Record time of day, weather, simulation time, pause state, and time scale when they affect the claim.
7. Record quality tier and effective overrides. Record changed asset paths and values, not only a preset name.
8. Write ordered actions using verified commands, fixture controls, or inspector operations.

A restart, regeneration, fixture reset, and save reload restore different state.
State which one the procedure requires and which data it preserves.
Do not delete saves or reset unrelated user state to make a comparison easier.
Use copied test data when persistence migration needs destructive recovery tests.

Do not invent a freeze, teleport, or seed command. Check current console help or the fixture implementation.
If a required control does not exist, record that limitation and narrow the claim or choose another available fixture.

## Replay and compare

1. Restore the recorded initial state. Verify effective values before running actions.
2. Use an observable readiness condition, such as completed generation or a filled timing window.
3. Execute the actions in order. Record any deviation before interpreting the result.
4. Capture results at the recorded event or simulation time, with a stated tolerance when needed.
5. Archive the baseline and after evidence using the parent skill's capture protocol.
6. Compare the expected invariants and allowed differences. Mark unmet setup conditions as an invalid comparison.

The same seed alone does not reproduce weather, persistent creature state, inputs, or asynchronous timing.
Exact equality is appropriate only when the scenario controls all relevant deterministic inputs.
For noisy measurements, define repeat count and tolerance before running. Preserve individual results, including failures.
Record warmup, cache state, resolution, hardware, and sample window when making performance claims.
Separate cold-start timing from warm or reloaded runs.

For long actions, record the observation duration and whether it uses wall time or simulation time.
A screenshot proves the captured instant; it does not prove the entire interval.
Do not infer an unlimited behavior claim from one short controlled run.

## Result and maintenance

Use separate statuses for setup validity, measured result, and any required visual approval.
Record exact errors and evidence locations. A missing result is unverified, not a pass.
Preserve the original baseline recipe when setup changes. Create a new revision and state why results are no longer directly comparable.
Keep reusable setup separate from each run's observations so another agent can repeat it without inheriting a verdict.

## Provenance and maintenance

Added 2026-09-09. Sources are the parent evidence ladder, capture protocol, and the agent-systems survival scenario.
Recheck `docs/design/2026-09-07-agent-systems.md` for reset semantics and observation limits before reusing that fixture.
Read current command help through the existing console workflow; this reference defines no new commands.
