# Scenario: <stable name>

Use this block in the owning plan or evidence record. Replace placeholders before execution.
Omit irrelevant fields, but explain missing inputs that limit reproducibility.

## Recipe

| Field | Recorded value |
|---|---|
| Recipe revision and date | <revision; date> |
| Claim and scope | <behavior or measurement; observation limits> |
| Pass conditions | <expected invariants; thresholds; allowed differences> |
| Project state | <commit; relevant dirty files or patch location; Unity version> |
| Scene or fixture | <asset path; subject identity> |
| Reset and persistence | <fresh play, fixture reset, regeneration, or save reload; retained state; test-data path> |
| Seeds | <world; planet; other controlled random inputs> |
| Location and camera | <subject position; camera position and orientation; coordinate space; planet> |
| Time and environment | <time of day; weather; simulation time; pause and time scale> |
| Effective settings | <quality tier; overrides; asset paths and relevant values> |
| Readiness condition | <observable condition before actions or measurement> |
| Measurement conditions | <hardware; resolution; warmup and cache state; sample window; repeats and tolerance, if relevant> |
| Capture point | <event or elapsed simulation/wall time; observation duration> |
| Known limits | <uncontrolled inputs; inaccessible evidence; untested behavior> |

## Ordered actions

1. <Exact verified reset action and expected initial state.>
2. <Exact verified commands or fixture operations in execution order.>
3. <Observation and capture actions at the defined point.>

## Runs

Add one row per run. Keep unsuccessful runs.

| Run and timestamp | Code/asset state | Setup valid? Deviations | Observed values and behavior | Verdict against thresholds | Evidence paths |
|---|---|---|---|---|---|
| <baseline/after/repeat> | <revision or patch> | <yes/no; differences> | <measurements; exact errors> | <pass/fail/unverified/invalid comparison> | <capture; sidecar; log; test result> |

## Review

- Comparison: <differences and supported conclusion; remaining limits>.
- Required visual approval: <not applicable, pending, or Bryan's recorded verdict and date>.
- Replay notes: <changed controls or unavailable artifacts; next verification step>.
