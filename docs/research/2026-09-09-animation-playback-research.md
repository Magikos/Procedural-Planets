# Animation playback research

Date: 2026-09-09. Read-only source research against the dirty working tree above `d1e0f62`.
Reference package: `D:/Unity/Explore Assets/Packages/com.kybernetik.animancer`, version `8.4.0`.
No Unity editor control, source transfer, runtime benchmark, or visual test occurred in this research.

## Recommendation

Extend `Assets/Scripts/Game/Animation/ActorAnimationGraph.cs`. Adopt focused playback concepts from Animancer rather than its full object hierarchy.
The graph already creates clip playables once per actor. State caching therefore does not provide an automatic steady-state improvement.
Phase/contact alignment and explicit transition ownership offer the strongest visual payoff.

## Ranked findings

### P1. High payoff: explicit interrupted fades

`Runtime/Core/Fades/FadeGroup.cs:149` captures the incoming and outgoing nodes with their starting weights.
`ApplyWeights` at line 349 interpolates the incoming weight and scales each outgoing starting weight by remaining progress.
`Finish` sets exact final weights and stops or disconnects outgoing states. `Cancel` releases the fade without completing it.
`FadeGroup.Pool` at line 686 reuses fade objects. This avoids repeated managed allocations; it does not cache evaluated animation poses.

Integration point: `ActorAnimationGraph.BlendBaseWeights` currently bounds the largest weight change using a global rate.
Replace implicit transition timing with authored duration/easing and a current-weight snapshot when a new action interrupts a fade.
Keep live locomotion parameter smoothing separate from action transition progress. Restarting a fade every frame would prevent completion.
Preserve outgoing clip time until its contribution reaches zero. Do not reset a visible source clip when replaying the same action.
Support a second playback instance only where a same-clip restart needs an outgoing and incoming pose simultaneously.

Required checks: A-to-B-to-C interruptions; same-clip restart; zero-duration explicit snap; finite normalized weights; exact completion; skipped-pose updates.
Weight continuity alone does not guarantee velocity continuity. If visible acceleration jumps remain, evaluate pose inertialization separately.

### P2. High payoff: compatible phase synchronization

`Runtime/Mixer States/ManualMixerState.cs:793` synchronizes selected children through weighted normalized time and normalized speed.
The implementation adjusts internal playable speed so children reach the shared normalized time on the next evaluation.
It excludes zero-length clips and handles tiny group weights separately.

Our graph advances clip time explicitly and evaluates with zero delta. Copying its playable-speed correction unchanged would not fit our schedule.
Use a shared phase clock to map compatible gait phases to each clip's sample time instead.
Keep idle outside gait synchronization. Keep irregular limp cycles and different contact orders out of naive normalized-time blending.

Animancer documents that it does not implement foot-phase synchronization. It requires extra clip analysis and runtime mapping.
That missing capability is central to our injury requirements. [Mixer synchronization](https://kybernetik.com.au/animancer/docs/manual/blending/mixers/synchronization/)

Integration points: `ActorAnimationGraph.Advance`, humanoid directional clip sampling, and authored performance contact markers.
Validate monotonic phase maps, cycle wrap, multiple cycles, left/right phase offsets, tiny weights, and zero delta.

### P3. Medium payoff: bounded state reuse and active graph branches

`Runtime/Core/AnimancerStateDictionary.cs:259` resolves existing graph-local states by key before creating new clip or transition states.
Registration rejects states owned by another graph. This is runtime state reuse, not sharing mutable states across actors.
`FadeGroup.Finish` can stop and disconnect faded-out nodes while retaining their state for reuse.

Integration point: `ActorAnimationGraph.AddBaseClip` already creates fixed clip slots once. Retain that benefit.
When skill and interaction catalogs grow, add lazy per-actor state creation with explicit lifetime and bounded retention.
Share immutable clip metadata across actors. Never share mutable time, weights, event cursors, contact anchors, or rig bindings across actors.
Measure active branch evaluation before adding disconnection. A zero-weight state and a disconnected state may have different engine costs.
Preserve current gameplay time advancement when pose evaluation is skipped.

Required measures: cold creation time, warm transition time, managed allocation, retained state count, and pose evaluation time across actor counts.
Do not claim a cache speedup without measurements on our content.

### P4. Useful reference: pooling and metadata caches

`Runtime/Data Types/Object Pooling/ObjectPool.cs` and collection pools support object reuse.
`Runtime/Utilities/Custom Fade/Easing.cs` caches delegates.
Many other package caches are editor reflection, object names, or sprite resources. They do not accelerate our humanoid pose evaluation.

Use existing project pools or native platform facilities before importing another general pool implementation.
Bake clip length, phase/contact markers, compatibility identifiers, and rig-independent trajectory data once.
Invalidate baked data when the source clip, import settings, or metadata schema changes.
Live hand targets, ground results, and wounded poses remain actor-specific and time-dependent.

## Source reuse boundaries

The installed `License.txt` states that Animancer uses the Unity Asset Store EULA. Sample art has separate terms.
The current EULA permits specified embedded product use and modification, while retaining redistribution and seat restrictions.
Do not treat adapted proprietary source as independently owned or publicly redistributable utility code.
Record source version, original path, license, and local changes for any direct harvest.
These findings do not verify purchase scope or authorize distributing a standalone animation framework.
[Unity Asset Store EULA](https://unity.com/legal/as-terms)

No direct source copy is needed for the first phase-clock and transition implementation. Standard interpolation and graph APIs can express our requirements.
Keep direct harvesting selective when a tested implementation gives a concrete advantage over existing project code.

## Limits

This research establishes implementation behavior from source. It does not establish runtime performance or visual quality.
Animancer supplies no automatic solution for skill technique selection, outcome authority, body-specific contacts, or whole-body injury compensation.
Those features belong in our performance and procedural pose design.
