# River-view frame stall: 2026-09-10

The severe river-view stall came from CPU fish simulation. High draw counts were a separate cost.

## Reproduction and evidence

Unity 6000.7.0a5, seed `1691104419`, saved camera `RiverChannelFinal`, position `(59.48, -1134.49, -4971.95)`.
The Editor ran at 2160 × 771 with VSync disabled. These are Editor measurements, not standalone Player benchmarks.

Before changes, six actual frames averaged 1,676 ms. A seven-frame CPU profile attributed 1,484 ms to `Planet.Update` self time.
An isolated fish update took 1,448 ms. River presentation took 0.35 ms. Disabling GPU river queries did not remove the stall.

The population contained 52 fish. Unity capped delta time at 0.333 seconds, causing ten fish substeps per frame.
Each fish repeated current habitat checks within each substep. A water query cost about 0.194 ms, including river sampling and ground carving.
Long frames therefore multiplied water queries and prolonged subsequent frames.

## Changes

- `FishSchool` retains the current habitat sample within each synchronous substep. Destination and shoreline checks remain active.
- `FishMovement` shares habitat validation and permits the school to reuse its validated starting position.
- `FishPopulation` caps runtime catch-up at 0.1 seconds. Explicit offline `FishSchool.Tick` calls retain full elapsed-time behavior.
- The Rivers task added Burst direct calls for `RiverFieldData.Sample` and `Carve`. Both use the existing algorithms.
- Frame counters now expose fish simulation, fish presentation, river presentation, and ambient wildlife.
- Frame counters retain valid frames longer than one second. The old filter concealed the severe stall.

No fish population limit or rendering quality setting was reduced. Runtime fish movement slows during sustained overload because catch-up is bounded.

## Validation

All 35 selected EditMode tests passed: `FishMovementTests`, `FishPopulationTests`, `FrameTimingSectionTests`, and `RiverTests`.
The tests include habitat safety, repeated-step sampling, long-frame counters, and Burst/reference parity across banks and cube seams.
Core and Planet assemblies built with zero errors. Existing warnings remain in build logs.

The final 240-frame capture had seven groups and 44 fish. Population movement makes this a scene comparison, not an equal-count benchmark.

| Measurement | After |
|---|---:|
| Actual frame interval average | 25.35 ms |
| Actual frame interval maximum | 50.47 ms |
| CPU frame average / p95, last 120 samples | 24.74 / 35.56 ms |
| GPU frame average, 94 valid samples | 19.17 ms |
| Fish CPU average / p95, last 120 samples | 5.88 / 11.44 ms |

At the same query direction, 1,000 river samples took 5.985 ms through Burst and 101.314 ms through the managed reference.
Burst reported enabled. GPU river queries remained enabled in the final capture.

## Remaining performance work

The severe stall is fixed, but this view does not sustain 60 FPS. Measure remaining CPU and GPU costs separately before choosing another implementation.
Orbital isolation removed about 1,170 draw calls when scatter rendering was disabled. It saved about 2 ms in that capture.
Scatter batching and material passes therefore remain useful candidates. They do not explain the former 1.4-second fish update.
The GPU near-river average also exceeds the 16.67 ms budget. Evaluate shader and shadow costs with a GPU capture.
Consider fish query jobs only if further measurements justify their scheduling and snapshot costs. Burst already removes the dominant managed arithmetic cost.

Local evidence is under `local-only/performance-2026-09-10/`: `near-cpu.json`, `river-query-isolation.json`, `isolation.json`, `near-after.json`, and build logs.
The initial screenshot overlapped GPU query isolation. Its query state is uncertain, so it must not serve as a visual parity reference.
Unity was returned to the Rivers task at the saved river view with `_RiverActive=1` for separate visual validation.
