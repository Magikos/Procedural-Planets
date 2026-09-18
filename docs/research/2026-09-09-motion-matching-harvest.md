# Motion matching harvest research

Date: 2026-09-09. Source inspection of the current dirty working tree and installed scratch package.

Package root: `D:/Unity/Explore Assets/Assets/QuanticBrains/MotionMatching`.
`Scripts/Editor/WelcomeWindow.cs:10` identifies installed version `1.3.4`.
The package includes `Documentation/MMSystem - Documentation.pdf`. Findings below come from inspected source, not a runtime benchmark.
No package source was copied. No Unity control or runtime measurement occurred during this research.

## Recommended order

### 1. Inertialization for pose and velocity continuity

Source: `Scripts/Components/Inertialization.cs`.

The implementation records per-bone position, rotation, linear velocity, angular velocity, and optional scale data.
New transitions calculate differences between the current motion and the target animation motion.
These offsets provide information that weight crossfades alone do not preserve.

Integration point: the shared animation pose pipeline between base sampling and procedural contact correction.
Keep `ActorAnimationGraph` as the playback owner. Keep the shared motor as the authority for root movement.
Capture base animation motion separately from IK, leaning, look, spine, and secondary motion.
Otherwise a new transition can capture the previous procedural correction and apply it again.

The current `ActorPerformancePlayback` uses bounded duplicate phase slots to prevent visible sample rewinds.
Inertialization is a candidate improvement for pose velocity continuity, not proof that this storage can immediately be removed.
Test interrupted transitions, skipped pose evaluations, changing gravity frames, and ragdoll handoffs before replacing existing behavior.
Exclude locked contacts from unrestricted smoothing. Apply contact constraints after inertialization.

### 2. Baked feature data for candidate selection

Sources:

- `Scripts/Components/Queries/Models/FeaturesComputedNative.cs`
- `Scripts/Components/Queries/QueryComputed.cs`
- `Scripts/Components/Queries/Implementations/ActionQueryComputed.cs`

The package stores pose positions and velocities, past and future trajectories, normalization statistics, and animation-frame identifiers.
`GetQueryComputedNative` creates persistent native arrays once and returns the existing arrays after creation.
Feature vectors are flattened into contiguous arrays. Query ranges also use persistent native storage.
This caches recorded feature data. It does not cache the final actor pose after terrain or interaction IK.

Integration point: an optional candidate selector inside an eligible `ActorAnimationPerformanceLibraryData` group.
Filter action, rig family, proficiency, condition, equipment, and contact compatibility before calculating similarity.
Do not let a low feature distance select an expert vault for a novice or a healthy gait for an injured actor.
Keep physical phase transitions and gameplay outcomes outside nearest-pose selection.

Our graph already retains playback instances. Adding another playback cache would not reproduce the feature database benefit.
Share immutable baked features where possible. Keep current trajectories, contact targets, event cursors, and transition offsets actor-specific.
Define native-data lifetime and invalidation before sharing datasets among actors.

### 3. Search cadence and diagnostics

Sources:

- `Scripts/Components/PoseFinders/Implementations/MotionPoseFinder.cs`
- `Scripts/Editor/Analysis/TransitionCost.cs`
- `Scripts/Editor/Analysis/PoseUsage.cs`
- `Scripts/Editor/Analysis/PostProcessingMetrics.cs`

The pose finder continues the next pose between searches according to its search counter.
Search jobs partition work by query range. Each range performs a linear candidate scan.
The implementation uses Burst, but several scheduled jobs immediately call `Complete()`.
This inspection did not establish an accelerated search tree or a measured whole-system performance gain.

Integration point: existing actor pose cadence and profiling facilities.
Measure search frequency, candidate count, feature count, dataset memory, job synchronization, and total actor cost.
Keep search updates distinct from visual pose updates and authority simulation.
Transition-cost and pose-usage displays are useful diagnostics for deciding whether more animation data improves results.

## Creature, skill, and contact constraints

`Scripts/Containers/CustomAvatars/GenericAvatar.cs` resolves an authored list of bone names.
The package therefore has generic skeleton support in addition to humanoid support.
This does not establish automatic transfer between unrelated anatomies.
Each body family needs suitable motion data, feature bones, contact metadata, and procedural solvers.

Action query ranges provide a useful restriction mechanism. They are not a complete skill or injury model.
A limp changes support timing and balance. Mouth pickup needs a compatible effector and object contact.
Neither feature follows automatically from nearest-pose search.

## Conflicts with our runtime

Source: `Scripts/MotionMatching.cs`.

The package runtime advances through `FixedUpdate`, instantiates its character controller, and updates root and bone transforms.
Its `SynchronizeTransforms` coroutine explicitly coordinates with Final IK.
These responsibilities conflict with our authority-owned motor and manually ordered procedural pose pipeline.

Harvest bounded algorithms and data concepts. Do not import the runtime coordinator wholesale.
Retain `SurfaceCharacterController`, `ActorTraversal`, `ActorAnimationGraph`, `ActorPerformancePlayback`, and the shared procedural systems.
Record the source version, original path, applicable license, and changes if a later implementation copies vendor source.

## Evidence limits

This research verifies source mechanisms and integration risks.
It does not establish visual quality, animal readiness, supported actor counts, or runtime speed in our project.
The first useful experiment is inertialization on an interrupted humanoid transition with contact correction enabled.
Compare pose continuity and runtime cost before expanding into a searchable locomotion database.
