# Asset Adoption Map — what to harvest, per subsystem

_Companion to [2026-08-10-external-asset-catalog.md](2026-08-10-external-asset-catalog.md). The catalog is organised **by pack**; this is organised **by subsystem**, and every entry names a **part**, not a product._

> **Status: COMPLETE through round 3** — 2026-08-11. Round 1–2 surveyed the scratch folder and the 885-asset purchase library; round 3 inspected the **86 newly downloaded packages** plus the two standalone projects (Survival Engine, RPG Farming Kit). Twelve inspection agents total. **Nothing has been imported; no file under `Assets/` was touched.** Every package was read via scratch-folder reconstruction, never by importing.
>
> **The pattern that kept repeating: the small package usually won.** Fluid Seamless Portals (1 MB) beat Dynamic Portals (158 MB). Conversa (2.7k LOC) beat Dialogue System for Unity (90k LOC). Ultimate Crafting System's 531-line `Placement/` folder beat Survival Engine's building system outright. Love/Hate's ~800-line model was the highest signal-to-noise thing inspected. Meanwhile the two nominal "priority" packages in the AI round — Blaze AI and DSU — were the two biggest write-offs.
>
> **And check the cache before downloading:** three planned Tier-1 downloads were demoted because a cached package already solved the problem better, and the entire animation category resolved without a single download.

---

## 0. How to read this

### The rule

**Harvest-only. No third-party runtime C# ever ships in this repo.**

We download the real code into the scratch project, read it, lift the parts that earn their place, and refit them into our architecture — Awaitable-only, DTO settings, dependency-ordered init, `ShaderGlobalIds` for globals. Their code, our structure.

| Category | Allowed in `Assets/`? |
|---|---|
| Meshes, prefabs, textures, materials, animation clips, audio | **Yes** — import directly |
| Shaders / Shader Graphs | **Yes** — but re-check globals against `ShaderGlobalIds` |
| Runtime C# | **Never.** Read it in the scratch project; re-implement here |
| Editor-only tools (bakeries, generators) | **Never in-repo.** Run in the scratch project; import only their output |

### Confidence tags

| Tag | Meaning |
|---|---|
| **✅ inspected** | I extracted and read the actual files. Claims name real classes and line numbers. |
| **📦 cached** | The `.unitypackage` is on disk and inspectable, but I haven't read it yet at this depth. |
| **🔎 expected** | Owned but not downloaded. Reasoned from the product's documented behaviour and reputation. **Treat as a hypothesis**, not a finding. |

### Take sizes

Borrowing your phrasing: **"a lot from X, one thing from Y."**

| Take | Meaning |
|---|---|
| **CORE** | This is the primary reference for the subsystem. Read it before designing. |
| **a lot** | Several distinct ideas or files worth lifting. |
| **1 thing** | One specific algorithm, file, or trick. |
| **art** | Content only — no ideas, just assets. |
| **maybe** | Unsure; parked in §Maybe at the end. |
| **skip** | Named only so you know I looked and rejected it. |

---

## 0.5 Read this first — the three findings that outrank everything else

### ① `NativeMovementPlane` — build this before anything else

`A* Pathfinding Project Pro / Graphs/Utilities/GraphTransform.cs` (already in your download cache).

A **quaternion-valued, per-agent tangent plane** with `ToPlane(float3) → float2` and `ToWorld(float2, elevation) → float3`. You do all movement maths in the local tangent plane, then lift back to world.

This is ~150 lines and it is the correct primitive for **every** spherical system in the game — AI navigation, the character controller, mounts, carts, buoyancy, flight. Right now each of those would invent its own ad-hoc "project onto the sphere" maths. A* Pro's authors hit the same wall and this was their answer.

**Recommendation: re-implement this as a shared utility before starting any of the subsystems below.** It is the cheapest structural decision available and it compounds.

### ② Kinematic Character Controller is the locomotion answer, and it's already on disk

Arbitrary-up is **native, not bolted on** (`_characterUp = _transientRotation * _cachedWorldUp`). Its `ICharacterController` seam is nearly the shape our `CharacterMotor` already has. `Core/` is **0 coroutines, 0 `async void`, 0 `Task.Run`, 0 `RuntimeInitializeOnLoadMethod`**. It ships a planet gravity example and walkthroughs for arbitrary-up, swimming, ladders, moving platforms and double/wall jumps — effectively a design manual for the exact states we need.

This supersedes SuperCharacterController, which the catalog recommended yesterday on the basis that it was the only thing available.

### ③ Sphere navigation is a solved, shipped problem — by A* Pro

Not a hack, not a community fork: *"Added support for pathfinding, local avoidance and movement on spherical worlds"* is in their changelog, with a shipped spherical example scene. Only `NavMeshGraph` (fed from a mesh we supply) works — `RecastGraph` cannot wrap a sphere.

Two of their decisions are the blueprint for planet-scale AI: **the movement plane is a runtime quaternion**, and **the world is tiled with per-tile navmeshes stitched by shared edges** using a chessboard colouring so threads never conflict. That second one maps straight onto our cube-sphere chunk streaming.

> All three are **harvest-only reads**. None of these packages ship in the repo — A* Pro alone is 393 files with real OS threads and 23 global-namespace types.

---

## 1. Character locomotion & camera

**Current state.** `CharacterMotor` (pure class, computes its own velocity) + `IGravityProvider` / `IGroundingProvider` seams. Radial gravity, analytic collider-less grounding. 78 EditMode tests green. Play-tested through round 2; every constant is still an admitted guess (`WalkSpeed=5`, `CamDistance=5.5`, `JumpHeight=1.6`, `LookSensitivity=0.12`).

**The finding that reorders everything here:** you own **Kinematic Character Controller**, it is **already in the download cache**, and it is a far better reference than anything in the scratch folder.

### Kinematic Character Controller — **CORE** ✅ inspected
`Philippe St-Amand / ScriptingPhysics / Kinematic Character Controller.unitypackage` (27 MB, 60 `.cs`)

| Take | Part | Why |
|---|---|---|
| **CORE** | `Core/KinematicCharacterMotor.cs` (2,689 lines) | The whole motor. Arbitrary-up is **native, not bolted on**: `_characterUp = _transientRotation * _cachedWorldUp` (`:494`). Every ground/velocity decision flows from `CharacterUp`. |
| **a lot** | `Core/ICharacterController.cs` | The seam — `UpdateRotation(ref Quaternion, float)` / `UpdateVelocity(ref Vector3, float)` plus `BeforeCharacterUpdate`, `PostGroundingUpdate`, `AfterCharacterUpdate`, `OnGroundHit`, `OnMovementHit`, `ProcessHitStabilityReport`. **This is nearly the shape our `CharacterMotor` already has** — the ref-parameter callback model is worth adopting wholesale. |
| **a lot** | Ground probing + `CharacterGroundingReport` / `CharacterTransientGroundingReport` (`:329`, `:334`), `ProbeGround` (`:914`) | A grounding *report* rather than a bool. Distinguishes "on ground" from "stably on ground", tracks ground normal, collider and attached rigidbody. Our `IGroundingProvider` returns far less. |
| **a lot** | `GetDirectionTangentToSurface` + `Vector3.ProjectOnPlane(BaseVelocity, CharacterUp)` (`:919-920`) | Velocity reprojection on landing — how to preserve speed when the surface normal changes. On a sphere the normal changes *constantly*, so this matters more for us than for them. |
| **1 thing** | `Examples/Scripts/PlanetManager.cs` (66 lines, whole file read) | Radial gravity in one line: `cc.Gravity = (PlanetMover.transform.position - cc.transform.position).normalized * GravityStrength`. Confirms our approach; nothing to lift beyond the confirmation. |
| **1 thing** | `Walkthrough/7- Orienting towards arbitrary up direction/` | The orientation trick: `currentRotation = Quaternion.FromToRotation(currentRotation * Vector3.up, -Gravity) * currentRotation;` with an `OrientTowardsGravity` toggle. Also shows camera-basis projection onto `CharacterUp` (`:76-81`) — directly relevant to our third-person camera. |
| **1 thing** | `Walkthrough/13- Swimming state/` (24.5 KB) | A worked swimming state on the same motor. Roadmap #20 is ocean traversal; this is the reference. |
| **1 thing** | `Walkthrough/14- Climbing Ladders/` (27.8 KB) | Ladder climbing — relevant once building exists. |
| **1 thing** | `Core/PhysicsMover.cs` + `IMoverController` | Moving-platform attachment. Relevant to **boats and carts**: standing on a moving deck is the same problem. |
| **skip** | `KinematicCharacterSystem.cs` | Its own update-loop singleton with the one `[DefaultExecutionOrder]`. We already have `LoadingManager` + orchestrator forwarding; don't import a competing loop. |

**Rule compliance of `Core/`** — measured, not assumed: **0** `StartCoroutine`, **0** `IEnumerator`, **0** `yield return`, **0** `async void`, **0** `Task.Run`, **0** `RuntimeInitializeOnLoadMethod`, **1** `[DefaultExecutionOrder]`, **6** `Vector3.up` (all either `_cachedWorldUp` initialisation or capsule-local axes, which are correct).

**What this changes:** SuperCharacterController drops from "read this first" to a second opinion. KCC is better documented, better structured, has a real seam, ships a planet example, and its 16-part walkthrough is effectively a design manual for exactly the states we need (jump, double-jump, wall-jump, crouch, swim, ladder, moving platform, root motion, arbitrary up).

### Second opinions — read only if KCC leaves a gap

| Asset | Take | What | Tag |
|---|---|---|---|
| SuperCharacterController | **a lot** | 5-probe grounding solver (`SuperGround.cs`) and its ledge/step/flush taxonomy; `BSPTree.cs` nearest-point-on-mesh → belongs to **Collision** (§11). | ✅ inspected |
| Malbers Animal Controller | **1 thing** | `IGravity` + `GravityChanger` — confirms our seam. Discard the rest (113 coroutines, 21 `[DefaultExecutionOrder]`, and it's a modified fork). | ✅ inspected |
| Character Controller Pro | **maybe** | Lightbug. Documented arbitrary-up support; a third data point on grounding. | 🔎 expected |
| Character Movement Fundamentals | **maybe** | Same category. | 🔎 expected |
| Easy Character Movement 2 | **maybe** | Custom gravity direction. | 🔎 expected |
| Physics Character Controller | **skip** | Rigidbody-based; wrong model for us. | 🔎 expected |
| PerfectLookAt | **1 thing** | Multi-bone look-at chain — **but fix its two world-Y sign bugs** (`PerfectLookAt.cs:367`, `PerfectLookAtLegStabilizer.cs:43,46`) which break on the far hemisphere. | ✅ inspected |
| Starter Assets — ThirdPerson URP | **1 thing** | Unity's own third-person camera rig + Cinemachine wiring. Useful only as a sanity check on camera constants. | 📦 cached |

**Download gate for this subsystem:** none. Everything above is on disk.

---

## 2. Collision & physics

**Current state.** Zero physics code — no `MeshCollider`, no `BakeMesh`, no `Rigidbody` anywhere in `Assets/Scripts`. The streamed per-chunk MeshCollider "physics bubble" is fully designed in [2026-08-09-collision-strategy.md](../design/2026-08-09-collision-strategy.md) and unbuilt. This retro-blocks every dynamic verb.

| Asset | Take | What | Tag |
|---|---|---|---|
| SuperCharacterController | **CORE** | `BSPTree.cs` (332 lines) — builds a triangle BSP over a mesh collider and answers `ClosestPointOn(point, radius)`. A worked nearest-point-on-mesh solver that doesn't rely on `Physics.ComputePenetration`. ⚠️ builds the full BSP in `Awake()` and caches local-space verts — **not streaming-friendly as written**, which is exactly the part we'd redesign. | ✅ inspected |
| SuperCharacterController | **a lot** | `SuperCollider.ClosestPointOnSurface` — analytic closest-point per collider type (box/sphere/capsule/terrain/mesh). The dispatch pattern is the useful bit. | ✅ inspected |
| Kinematic Character Controller | **a lot** | How the motor consumes colliders: `IsColliderValidForCollisions`, `ProcessHitStabilityReport`, and the capsule-cast sweep. Tells us what the bubble must provide. | ✅ inspected |
| Synty packs (all) | **art** | Thousands of baked convex collision meshes at `Models/Collision/*_Convex.asset` (FantasyKingdom 2,061, Dungeon 653, Adventure 598, ElvenRealm 427, Knights 374, NatureBiomes 282). Props arrive with hulls already cooked — free input for prop physics. | ✅ inspected |
| Non-Convex Mesh Collider generator | **maybe** | Automatic convex decomposition. Relevant if props need accurate concave collision. | 🔎 expected |
| Obi Rope / Filo — The Cable Simulator | **maybe** | Rope simulation → cart tethers, bridges, ziplines. Deferred until carts exist. | 🔎 expected |
| DestroyIt / RayFire / Mesh Slicer / Impact Deformable | **maybe** | Destruction. Relevant to **chopping trees** and **mining ore** — but a felled tree is probably a swap-to-prefab, not a real fracture. See §7. | 🔎 expected |

**Download gate:** Non-Convex Mesh Collider, Obi Rope, Filo, DestroyIt, Mesh Slicer.

---

## 3. Animation ⏳

**Current state.** No animation assets at all. Player model will be Synty PolygonFantasyHeroCharacters (Humanoid, **zero clips**). Base locomotion set chosen: **Kevin Iglesias Human Mega Animations** (1,373 Humanoid clips, zero rig-import errors, in-place by default with `[RM]` twins).

Detailed clip-level analysis is in catalog §6. This section covers the **systems**, which is what harvest-only cares about.

_Agent inspection of Animancer Pro v8, IK Helper Tool, Procedural Walk Animation, iStep and the 11 Polygonmaker packs is in flight._

### 3.0 Animancer Pro v8 — **CORE**: harvest ~150 lines of Playables, skip the runtime ✅ inspected

Full C# source (not a DLL), 394 `.cs`, 4 asmdefs. ⚠️ **`AnimancerPlayable` doesn't exist in v8** — the graph root is **`AnimancerGraph`**, and the 2D mixers are `Vector2MixerState` → `CartesianMixerState` / `DirectionalMixerState`.

**Why it matters:** `LinearMixerState.Parameter = motor.PlanarSpeed` is a direct assignment from a value our motor already computes — no `Animator.SetFloat` round-trip, no `.controller` asset, no state machine duplicating logic our 78 EditMode tests already cover. Animancer's own FSM is a *separate asmdef* from its graph, which validates the split we already have: **our logic decides, the graph only renders.**

**But the load-bearing technique is small.** The verified topology:

```
AnimationPlayableOutput ──bound to──> Animator
  └─ ScriptPlayable<UpdatableListPlayable>          ← the update pump
       ├─ in 0: AnimationLayerMixerPlayable
       │          ├─ in 0: AnimationMixerPlayable   = layer 0 (base)
       │          │           ├─ AnimationClipPlayable      (clip)
       │          │           └─ AnimationMixerPlayable     (nested blend)
       │          └─ in 1..n: further layers
       └─ in 1: ScriptPlayable<UpdatableListPlayable>  (post-update)
```

Three ideas carry the whole design:

1. **The root is a `ScriptPlayable`, not the mixer.** `PrepareFrame` is the update pump, called **inside graph evaluation** — so time and fade bookkeeping run in lockstep with animation sampling instead of in `Update()`. **Given our no-`[DefaultExecutionOrder]` rule, this is a clean substitute for ordering hacks: the graph itself defines the order.**
2. **Blending is nothing but `SetInputWeight`.** One choke point: `graph.Connect(child, 0, parent, i); parent.SetInputWeight(i, w);`. There is no other mechanism.
3. **Fading is a normalised scalar lerp over those weights** — cross-fade is N clips alive at once with weights summing to 1.

| Take | Part | Why |
|---|---|---|
| **CORE** | `AnimationLayerMixerPlayable` + `SetLayerAdditive(i, true)` + `SetLayerMaskFromAvatarMask(i, mask)` | **This is the spellcasting answer.** Base-layer locomotion driven by the motor, additive/masked upper-body layer for cast / channel / aim. No state machine. |
| **CORE** | `AnimationScriptPlayable.Create(graph, job, n)` with a Burst `IAnimationJob` | The escape hatch for **spherical-world bone fixups** — foot IK and torso lean along the surface normal, applied post-blend, on the animation thread, with a `Vector3 up` we supply. Exactly what a curved world needs. |
| **a lot** | `LinearMixerState` threshold math | Same as a Blend Tree, but thresholds live in a plain C# array settable at runtime. |
| **1 thing** | `SkipFirstFade = animator.isHuman \|\| controller == null` | Skip the fade-in on the very first clip or the character **T-poses for the fade duration**. A bug we'd otherwise ship. |
| **1 thing** | `KeepChildrenConnected` | Disconnect-vs-zero-weight is a real perf lever. |
| **1 thing** | `clipPlayable.SetApplyFootIK(false)` | For in-place clips where the motor owns root motion. |

⚠️ Their runtime violates our rules anyway (7 `[DefaultExecutionOrder]`, 1 coroutine, 1 `RuntimeInitializeOnLoadMethod`) — but all avoidable in a re-implementation, since the `ScriptPlayable` pump removes the need for MonoBehaviour ordering.

### 3.0b Foot IK — iStep is the harvest, Procedural Walk is not ✅ inspected

| Asset | Verdict |
|---|---|
| **iStep** (7 `.cs`, 2,927 lines) | ✅ **The math is sphere-ready as written.** Every cast is parameterised: `isValidAndGrounded(Vector3 feetZeroPos, Vector3 upVec, int layer)`, `findNewIKPos(…, Vector3 rayDirection, …)`, and the slope addon uses `Quaternion.FromToRotation(m_transform.up, groundedNormal)`. **The only `Vector3.up` outside `Debug.DrawLine` is two inert zero-weight struct initialisers.** It even has a skeleton-derived fallback: `upVec = Head.position - Hips.position`. ⚠️ **Needs colliders** (BoxCast + SphereCast) — but both entry points are `protected virtual` and take the up-axis as an argument, so **swap the two `Physics.*Cast` calls for our analytic surface query and keep the rest of the math verbatim.** |
| **Procedural Walk Animation** (14 `.cs`) | ❌ **Unusable.** `Vector3.up` hard-coded in **all 8 casts**, no up-vector field, no gravity abstraction, and it needs colliders. Breaks the moment the character leaves the north pole. Harvest the *concept* only (step-when-drifted-past-threshold → arc → body-average height/tilt). |

**A simplification our architecture buys us:** iStep runs a two-pass BoxCast→SphereCast correction because a box cast reports the wrong plane normal at slope breaks. **On an analytic surface we get the exact normal for free — that entire second pass disappears.**

### 3.0c The 11 Polygonmaker clip packs — one rig, 561 clips ✅ inspected

**Confirmed: all 11 share one rig family** (`PolygonmakerRig_1-2`, two avatar variants, byte-identical GUIDs across every package). Import `_Source 1.2` once and expect ~10 duplicate-GUID warnings — that's the shared folder, not a problem. **All `animationType: 3` (Humanoid)** → retargets onto the Synty hero.

Convention is a root-motion base clip plus an explicit in-place twin — ⚠️ **with two separator spellings you must handle: `_inplace` and `-inplace`.**

| Pack | Clips | Verdict |
|---|---:|---|
| **Locomotion** | 145 (**65 in-place twins**) | **Import.** Full 8-direction × 5-gait matrix — `walk/walkFast/walkRelaxed/walkStealth/jog/run/runFast` × `{L45,L90,L135,R45,R90,R135,back}`, crouch set, `turn{L,R}{45,90,135}`, `turn180`, slide, rolling, `jumpObstacle/jumpWall`. Better-structured as *blend-tree-shaped* data than Kevin Iglesias: consistent naming, consistent angles, guaranteed twins. Feeds a `Vector2MixerState` straight from `CharacterMotor.Velocity`. |
| **Interactions** | 39 | **Import — this is the Valheim layer.** `chest_{open,close,search}`, `pickup_{ground,shelf,table}`, `lever_large`, full ladder set, `climb_up`, `jump_fence`. Serves harvesting, looting, building and containers directly. |
| **Floating** | 34 | **Import — the sleeper pick for a wizard game.** `move`/`run` with **no foot contact**, `cast1/2`, `power`, `transform`, `spawn`. This is **late-game flight and hovering-caster locomotion — and it sidesteps foot IK entirely on a curved surface.** Also ideal for wisps and elementals. |
| Regular | 44 | Strong: `cast1/2`, `idle`, `idlebreak`, `talk`, `pickup`, plus town-NPC social clips. |
| Hit / Death | 60 / 44 | Directional hit reactions and deaths × 4 archetypes. Combat feedback. |
| Creature | 41 | Wildlife/monsters — note `run4legs`, quadruped on the shared humanoid rig. |
| Undead | 74 | Skeletons/zombies + `spawn`/`despawn`/`deathspawn`/`ground-standup`. Fits necromancy and dungeons. |
| Archers | 48 | `aim`/`hold`/`shot`/`recharge` map cleanly onto **charge → channel → cast**. Retarget the upper body onto a staff. |
| Warriors | 35 | `cast1/2` and `block` useful; combos are for guards and bandits. |
| Street Fight | 27 | ❌ **Skip** — modern unarmed boxing. Wrong genre, no salvage. |

⚠️ **Quality caveat:** Polygonmaker's motion is chunkier than Kevin Iglesias Human Mega. Use **Locomotion + Interactions + Floating** as the genuine adds and treat the combat/death packs as **NPC/monster-tier, not player-tier**.

### 3.0d Everything else for animation

| Asset | Take | What | Tag |
|---|---|---|---|
| IK Helper Tool | **1 thing** | 53 lines. An **authored curve inside the clip** (`iKSwitch.localPosition.y`) driving hand-IK weight. Orientation-agnostic, no casts, works on a sphere unchanged. The *pattern* is exactly how you'd pin a staff or snap a hand to a harvest target. ⚠️ `OnAnimatorIK` only fires with a controller + IK Pass — in a pure-Playables setup use an `IAnimationJob` instead. | ✅ inspected |
| Final IK | **maybe** | `GrounderIK` and `FullBodyBipedIK`. **Demoted** — iStep is cached, inspected, and already sphere-ready. Download only if iStep's foot solver falls short. | 🔎 expected |
| Bio IK / Puppet3D / Very Animation / UMotion Pro | **maybe** | Authoring tools rather than runtime. UMotion/Very Animation would let us *author* custom clips (spellcasting, cart-pushing) rather than buy them. | 🔎 expected |
| Ragdoll Animator 2 | **maybe** | Death/knockback ragdolls that blend back to animation. Wants the physics bubble first. | 🔎 expected |
| Legs Animator / Tail Animator / Spinal Animator / Look Animator / Leaning Animator | **maybe** | FImpossible Creations' procedural secondary-motion family. Tail Animator is genuinely interesting for creatures and cloaks. | 🔎 expected |
| Motion Matching System | **skip** | Needs a large mocap corpus and heavy runtime; wrong scale for this project. | 🔎 expected |
| Dynamic Bone / Magica Cloth / Cloth Dynamics | **maybe** | Wizard robes and capes are a real want for the PC. Magica Cloth is the modern job-system one. | 🔎 expected |
| Better Animation Events | **1 thing** | Editor-only, channelised animation-event editor. Per the rule it stays in the scratch project — but events are authored per-clip and travel *with* the clip, so this one may be worth an exception discussion. Parked in §Maybe. | ✅ inspected |

### 3.1 Two clean Humanoid clip wins found in the cache ✅ inspected

Both are **zero-risk imports** — Humanoid rigs, explicit in-place/root-motion pairs, no runtime C#, and (in one case) no art at all to clash with Synty.

| Asset | Size | Why it earns a place |
|---|---:|---|
| **Stylish Archer Assets Pack** (Grruzam) | 95 MB | **456 of 466 FBX are Humanoid.** Organised into `Inplace/` and `Root/` subfolders throughout, plus `ZeroHeight`/`ZeroHeight_Feet` jump variants **for when you drive vertical motion yourself — exactly our case**. Ships **zero materials and zero textures**, so there is nothing to clash. The standouts are `12__Turn_ALL` and `16__Move_To_Move` — complete `Idle_To_Jog_Turn_L90/R90/180`, `Jog_To_Idle_Turn_*`, `Jog_To_Jog_Turn_*` transition sets, which are the expensive-to-author part of any third-person locomotion graph. For a wizard: **`3_Skill` (`Skill_A`..`Skill_G`) are generic ranged-caster channel/cast poses**, and `ChargeShot` maps onto a charged spell. |
| **ARPG Samurai** (Kevin Animation) | 56 MB | **339 `.anim` clips, Humanoid**, with 79 `*_IPC` in-place and 79 matching `*_Rootmotion` pairs. A wizard doesn't need katana combos, but the **47-clip crouch set**, the **13-clip ladder-climb set** (with a `Ladder.fbx` prop), `Getup1/2`, `Guard`, `Hit1..14`, `Stun`, and especially **`Buff`/`Buff2`** are directly usable. `Buff`/`Buff2` are the closest thing in the entire library to a **spellcasting** animation. |

Together with Kevin Iglesias (base locomotion) and Grruzam Archer's aim set, the humanoid animation problem is comfortably solved from cache alone — **no downloads needed for animation.**

**Download gate:** Final IK, Ragdoll Animator 2, Legs/Tail/Look Animator, Magica Cloth, UMotion Pro.

---

## 4. Magic, spells & VFX ⏳

**Not previously in scope.** A wizard PC with many spells makes this a top-three subsystem, and the library is deep here.

### 4.1 Synty POLYGON Particle FX — **CORE**, perfect style fit ✅ inspected

**180 FX prefabs** + 19 ambient FX in PolygonGeneric. The only pack here that needs no style negotiation.

Spell-relevant roster: **fire** (`FX_Fireball_01`, `_Fireball_Shooting_01/_Straight_01`, `_FlameThrower_01/02`, `_Fire_Explosion_01`, `_Embers_01`, `_Trail_Fire_01`) · **ice** (`FX_ShardIce_Explosion_01`, `_ShardIce_Shooting_01`, `_Blizzard_Snow_01`) · **lightning** (`FX_Electricity_01/02`, `_LightningStrike_01`) · **arcane** (`FX_MagicBlast_01/02`, `_ShardMagic_Explosion_01/_Shooting_01`, `_Ritual_Circle_01`, `_Magic_Lights_01`, `_MagicBug_Trails_01`) · **nature** (`FX_ShardVine_*`, `_ShardRock_*`, `_GroundCrack_Blast_01`, `_Wind_*`) · **heal** (`FX_Heal_01/02`, `_Healing_Cirle_01/02`) · **buff** (`FX_LevelUp_01`, `_Sparkle_Orbit_01/02`, `_GlowSpot_01..03`) · **debuff** (`FX_Poison_Green_01/_Purple_01`, `_StarStunned_01/02`) · **beam** (`FX_PowerBeam_01`, `_LazerBeam_01`, `_LightRayRound_01`) · **impact** (7 surface-typed variants — dirt/stone/wood/metal/water) · **AoE ground** (`FX_Ritual_Circle_01`, `_Hexagon_01/02`, `_Cartoony_Rings_01`).

🔑 **`FX_Portal_Round_01`, `FX_Portal_Sphere_01`, `FX_Portal_Thin_01` — the only on-style portal art in the entire library.**
⚠️ **Gap: no dedicated shield effect.** `FX_Hexagon_01/02` is the nearest primitive; shields must be authored (see §4.6).

| Take | Part | Why |
|---|---|---|
| **art** | The 180 FX prefabs | Import wholesale. Zero style clash. |
| **1 thing** | `SubGraphs/DepthFade.shadersubgraph` | A 5-node soft-particle depth fade. **Copy verbatim into our subgraph library.** |
| **1 thing** | `SubGraphs/Panner` + `UVWithPan` | Scrolling UV primitives. |
| **1 thing** | `Generic_ParticlesUnlit/Lit.shadergraph` | Depth fade + vertex-colour tint + fresnel rim + remap erosion. Dual-target (`BuiltInTarget` + `UniversalTarget`), so URP-clean. |
| **1 thing** | `PolygonShader.shadergraph` | The flat-shaded master — **the natural reference for making `Planet/PropLit` visually match Synty.** |
| ⚠️ | `SubGraphs/ObjectYRotation` | **Y-axis world-space billboard — wrong on a sphere.** Re-derive against planet-up. |

⚠️ **One-time material remap needed:** of 57 particle materials, only 15 bind the Synty graph; **42 bind legacy built-in resources** (`Particles/Additive`, `Alpha Blended`, `Multiply`). Those have no `LightMode` tag so URP draws them via implicit `SRPDefaultUnlit` — **they render, they don't go magenta** — but you lose soft-particle depth fade, URP fog, and SRP Batcher compatibility. Remap onto `Generic_ParticlesUnlit`/`Lit`.

### 4.2 GAPH 100 Special Skills — second source ✅ inspected

**102 effects** (not 100), split into **Set 1 = 55 script-free** and Set 2 = 47 script-driven. Under harvest-only, **Set 1 is what you can actually take** — Set 2 needs 8 spawner/mover classes re-implemented, which our spell system should own anyway.

Best Set-1 picks for a wizard: `FlameTsunami`, `BlastFlame`, `MagmaStrike`, `FlameBreath`, `IceField`, `IceFatalWheel`, `StormTornado`, `LightningField`, `BlackHole`, `PowerOfGravity`, `TimeField`, `DeathWave`, `CurseOfSpider`, `LightInFullBloom`, `LumenJudgement`, `GloryBoundary`, `PurifierBeam`, `WindCyclone`, `PoisonExplosion`, `GuardianShield`, **`SpaceWarpPortal`**, `OrbitalStrike`.

⚠️ **Use the shipped toon patch, not the default materials** — `Patch/Toon/ToonTypeMaterial(LinearSpace).unitypackage` + `ToonTypeTextures_Part1/2`. **That patch is what makes this pack Synty-adjacent**; the stock look is semi-realistic anime and clashes.
⚠️ `Shader_DistortionEffect.shader` uses **`GrabPass` → magenta in URP**. GAPH ships the fix: `Patch/Prefebs/OriginalEffectsPrefebs(WithoutDistort).unitypackage`. Or port it by swapping `_GrabTexture` → `_CameraOpaqueTexture`.

| Take | Part | Why |
|---|---|---|
| **CORE** | `Shader_IntegratedEffect.shader` (564 lines) | **The best uber-VFX reference in the library, and a ready-made spec for our own URP uber-VFX Shader Graph.** Keyword-gated: soft particles, normal-map distortion, noise distortion, flipbook + advanced flipbook blending, scrolling UV (incl. per-particle via custom vertex stream), UV rotation, **vertex displacement by noise**, fresnel rim, **dissolve/mask erosion with animated + distorted mask**, impact ripple, trail pass, plus exposed blend modes and culling. |
| **a lot** | `Shader_Decal.shader` | **Depth-reconstruction box decal** — `Cull Front`, `ZWrite Off`, `Linear01Depth` reprojection. **This is the technique for AoE ground markers on a curved planet**: depth-driven, so it conforms to terrain automatically instead of needing a projected quad. Easier to make planet-aware than URP's decal system. |

### 4.3 kripto289 Mesh Effects — one great idea ✅ inspected

Realistic style, so the art clashes. **A genuine URP patch ships in-box** (`HDRP and URP patches/URP patch.unitypackage`) which drops GrabPass and reads `_CameraOpaqueTexture` — unpatched, the 3 distortion shaders go magenta.

| Take | Part | Why |
|---|---|---|
| **a lot** | `ME_UberParticleShader.shader` — the **glowing dissolve edge** | `_Cutout` (PerRendererData) + `_CutoutTex` + `_CutoutThreshold` + **`_CutoutColor` for a glowing burn edge**. The single most reusable idea here — spell burn-in/burn-out. |
| **1 thing** | `ME_GlowCutout(Gradient).shader` | Minimal noise dissolve with an animated border (`_BorderScale` = width XY + offset Z). Short, clean, trivial to rebuild in Shader Graph. |
| **1 thing** | `ME_ColorHelper.ChangeObjectColorByHUE` | Recolour a whole effect by HSV hue — **one prefab, per-school spell tinting.** |
| **1 thing** | `PSMeshRendererUpdater.cs` concept | Applies an effect material across a mesh's renderers and fades it — the "enchanted weapon glow" system. ⚠️ Its implementation allocates **string dictionary keys every frame in `Update()`**. Take the idea, not the code. |

⚠️ **Two raw-string shader globals — `ShaderGlobalIds` conflicts:** `ME_AmbientColor` (`ME_CustomLight.cs:17`), `ME_Reflection` (`ME_Reflection.cs:33`). ⚠️ `ME_ParticleCollisionDecal.cs:53` uses `Vector3.Angle(normal, Vector3.up)` — breaks on a sphere.

### 4.4 Hovl Procedural fire — small and clean ✅ inspected

42 entries, **zero C#**. Cleanest pack in the set for harvest-only.

`FireSphere.shadergraph` (URP + HD targets) is the whole value: **genuinely procedural** — 31 Multiply / 13 Add / 10 Fraction / 8 Sine / 6 Lerp, i.e. flipbook-free fire from stacked `frac(uv + time*speed)` layers modulated by `sin`, with only **one** texture sample. Because it barely depends on a texture, it **recolours and posterises freely — you can flatten it to Synty flat-shading without fighting a baked flipbook.** Applied to a sphere mesh it gives a volumetric-ish fireball with no particles, which is exactly right for **a projectile that must look correct from every angle while flying over a curved horizon.**

⚠️ Delete the sibling `FireSphere.shader` — it's a Built-in surface shader and will go magenta.

### 4.5 Rejected — with reasons ✅ inspected

| Pack | Verdict |
|---|---|
| **SineVFX Sci-fi Portal** | ❌ **Reject. There is no portal technique in it.** No `RenderTexture`, no second camera, no oblique frustum, no `Stencil{}`, no traversal script — it is a camera-facing textured quad. All 5 shaders are `ForwardBase` (magenta in URP) *and* carry a 2016-era `only_renderers` whitelist with **no d3d12 and no vulkan**. Worse, **91 of its 92 `.cs` files are a vendored fork of Post Processing Stack v2** that would collide with URP 17's Volume system. **One idea survives:** the `_OpenMask` + `_OpenMaskDistortionPower` **iris** — a distorted radial mask driven by a 0→1 progress float is the right way to animate a portal opening. Four nodes in Shader Graph. |
| **pelengami Sci-Fi VFX** | ❌ **Reject as art** (off-theme, and **14 of 26 shaders are `#pragma surface` — guaranteed magenta in URP** — plus 9 more using GrabPass; effectively the whole pack is dead). ✅ **But read `SFX_EnergyShield.shader`** — the best shield reference anywhere in these seven packs, combining fresnel rim, **depth-intersection glow** (the bright rim where a dome cuts terrain — exactly what you want on a curved planet), dual scrolling noise, dissolve, **axis-swept spawn-in wipe**, and a **localised hit ripple** (`_HitTexture`, `_HitWaveMaxRadius`, `_HitWaveFade`). `SFX_Area.shader` is the same vocabulary for AoE ground and its depth-fade makes circles read correctly on uneven terrain. ⚠️ `SFX_GroundAttacher.cs` is world-down ground-snapping — breaks on a sphere. |
| **MK Glow** | ❌ **Reject — dead on 6000.6.** `RecordRenderGraph` appears **nowhere in the package**; it's not an empty override, the method is never declared. The only path is compatibility-mode `Execute(ScriptableRenderContext, ref RenderingData)`, and Compatibility Mode was **removed in 6.2**. You add the feature, the inspector looks right, nothing renders, nothing logs. ⚠️ It also writes dangerously generic raw-string globals — **`_ViewMatrix`, `_MainTex`, `_SourceTex`, `_DepthBuffer`** — which would quietly stomp things. **Use URP 17's stock Bloom.** ✅ One capability URP Bloom lacks is **per-object selective glow via a separate render buffer** (`MKGlowSelectiveRender.shader`) — if only spell VFX should bloom and not the sun, reimplement that as a modern RenderGraph feature. |

### 4.6 The system we still have to build

Every pack above is a bag of particle prefabs. **The spell system is ours**: a spell as a DTO, VFX as a prefab reference, casting as a masked additive animation layer (§3.0), with the motor unaffected. Only three things from this whole category contribute *logic* — GAPH's decal reprojection, pelengami's shield feature list, and SineVFX's iris mask.

**Two authoring gaps no pack fills:** a **shield/dome effect** (build from pelengami's feature list) and **portal traversal** (art from Synty, iris from SineVFX, traversal written by us — and remember the planet wrinkle from §10: source and destination have different "up", so re-orientation must be atomic).

**Suggested harvest order:** Synty art wholesale + remap 42 materials → GAPH Set 1 with the toon patch → rebuild `Shader_IntegratedEffect`'s property set as our own URP uber-VFX graph, taking kripto's glowing dissolve edge and Synty's depth-fade subgraph → port Hovl's `FireSphere.shadergraph` for projectiles → author the shield from pelengami's spec → portals last.

### 4.7 The rest of the magic library (not yet inspected)

- **Stylised spell FX (best style match):** Epic Toon VFX 2, Stylized VFX Bundle, Cartoon FX Remaster / Cartoon FX 4 Remaster / Cartoon FX Pack 3D, Toon Effects Maker URP, StylizedVFX Buff&Debuff vol.1, Frost & Ice Stylized VFX Starter Kit, Stylized Spark Particles, Hyper Casual FX Pack
- **Spell libraries:** Magic Arsenal, Combat Magic Spells Bundle, Spells Pack, 100 Special Skills Effects Pack 📦, Anime Powers Pack, Unique AoE Magic Abilities Vol.1, Unique Projectiles Vol.1, RPG VFX Bundle, MiniMagic And SpecialFX Library, Shuriken Magic Effect Pack, Realistic Effects Pack 4, Ultimate VFX
- **Shields / auras / zones:** Magic Shield Bubbles, Zones Fields and Shields, FREE Magic Aura Construction Kit, Skill & Attack Indicators (AoE telegraphs — important for readable combat)
- **Toolkits:** All In 1 Vfx Toolkit, Mesh Effects 📦 (kripto289), VolFx, Flipbook VFX Bundle, Particle Mega Pack, PopcornFX, Hayate 3 Particle Turbulence, Dissolve FX Master Kit
- **Portals (fast-travel system):** Dynamic Portals, Fluid Seamless Portals, Fantasy Portal FX, Sci-fi Portal And Machines Pack 📦 (technique only — art is off-theme)
- **Glow/post:** MK Glow 📦, Highlight Plus, Ultimate Outlines & Highlights, Linework
- **Fire/light:** Procedural fire 📦 (Hovl), Simple Torch, Procedural Lightning

**The structural question this subsystem raises:** every one of these is a bag of particle prefabs. What we actually need is a **spell definition → VFX binding** layer of our own (spell as a DTO; VFX as an addressable prefab reference; casting as a state on the motor). The packs supply art; the system is ours. Skill & Attack Indicators and the portal assets are the only ones likely to contribute *logic*.

**Download gate:** Magic Arsenal, Combat Magic Spells Bundle, Epic Toon VFX 2, Stylized VFX Bundle, All In 1 Vfx Toolkit, Dynamic Portals, Fluid Seamless Portals, Skill & Attack Indicators, Highlight Plus.

---

## 5. Survival, crafting, building & farming 🔎

**Not previously in scope, and this is the biggest gap between the catalog and your stated game.** Valheim-style building/crafting/cooking is a whole pillar with zero code today.

### 5.1 The headline — **no single package is the answer; two of them combine into one** ✅ inspected

**Survival Engine has no snap points at all.** Grep returns four hits, three unrelated. Its `BuildableType.Grid` is a naive world-space `RoundToInt`. There is no socket, no port, no "wall connects to floor edge". **Valheim-style building is not in it.**

**Ultimate Crafting System has exactly that, in 531 lines, and it is sphere-safe as written.** That was the surprise of this inspection.

So the recommendation is a **split**: Survival Engine's *domain schema* over UCS's *plumbing and placement*.

### 5.2 Ultimate Crafting System — `Placement/` is the single best find in this category ✅ inspected

A composable `IPlacementProcessor` pipeline mutating a `PlacementInfo { InitialPosition, Position, Rotation, IsValid, data bag }`. Processors: `GridSnapper`, `SurfaceSnapper`, `AxisRotationSnapper`, **`PortSnapper`**, `TransformMatcher`, `ChildMaterialReplacer` (ghost tint).

**`Placement_PortSnapper.SnapToPort()` (`:66-78`) is pure quaternion algebra with no world-up assumption whatsoever:**

```csharp
var localOffset = placedPort.Position;
info.Rotation = existingPort.Rotation * Quaternion.Inverse(placedPort.Rotation);
info.Position = existingPort.Position - info.Rotation * localOffset;
```

Candidate selection scores `distance/PortSnapRange + Quaternion.Angle(a,b)/180` **with hysteresis toward the previous target** so the ghost doesn't flicker between equidistant sockets. Ports are child transforms with a `ConnectorPort`, typed by a `PortIdentifier` ScriptableObject carrying `List<PortIdentifier> ConnectsTo` — so *"wall-edge connects to floor-edge and roof-edge"* is authored as data. `ConnectorPortGroup` enforces "only one of these child ports may connect".

**Lift this whole folder.** Three contained fixes:
- ⚠️ `PortManager.cs:16` uses `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` to clear statics — **banned**. Replace the statics with a registered service.
- ⚠️ `EnumeratePotentialConnections()` is a **LINQ-allocating linear scan over all open ports, every frame while the ghost moves.** Dies at planet scale — needs a spatial hash keyed off the ghost position.
- ⚠️ `Placement_SurfaceSnapper.cs:12` is the one flat-world bug, and it's *half* fixed already: the origin offsets along `-Physics.gravity.normalized`, then rays along hard-coded `Vector3.down`. Replace both with radial up + our analytic query.

UCS's other liftables: **`RuntimeID`** (value-type ID struct), **`ItemStack`** (`{id, Quantity}` struct with value semantics), **`Quantity`** (struct over `int` with implicit conversions, so you can widen later without touching call sites), **`IRecipe<INGREDIENT,OUTPUT>`** — two-way generic and **multi-output** (Survival Engine can only ever produce one thing; a wizard game wants *"distil → potion + empty flask + residue"*), the **satisfier strategy objects** (`QuantityAndIDSatisfier`, `AnyPositionItemQuantitySatisfier`…) which express *"any 3 herbs of school Fire"* cleanly, and **`SlottedInventory.RemainderIfInserted(x)` / `InsertPossible(x)`** — a try-then-commit split that makes "can I afford this recipe" free. It's also the only package here with actual EditMode/PlayMode tests.

### 5.3 Survival Engine — take the schema, rewrite the geometry ✅ inspected

200 `.cs`, no asmdefs, one namespace. A *complete* Valheim-shaped feature set in ~15k lines. **Almost none of the code should ship; almost all of the schema should.**

| Take | Part | Why |
|---|---|---|
| **CORE** | **`CraftData` (`Data/CraftData.cs:24-53`)** | One base for **item / building piece / crop / tameable creature** — shared `title/icon/groups/craftable/craft_quantity/craft_duration` plus a **four-part cost**: exact `craft_items`, `GroupData[] craft_fillers` ("any wood"), `CraftData[] craft_requirements` (prerequisites), `GroupData craft_near` (must be near a forge/fire/water). |
| **CORE** | **`GroupData` (`Data/GroupData.cs:15`)** | **The cleverest thing in the package** — an *empty* ScriptableObject used purely as a tag, serving four unrelated jobs at once: crafting-station kind, tool gate, filler-ingredient category, and UI tab. One concept, zero enums. **Lift verbatim** — it maps directly onto reagent classes, focus types and spell schools. |
| **a lot** | `Gameplay/Destructible.cs` + `Regrowth.cs` | The harvestable-node model, by composition not inheritance. **Hit count is emergent** (`hp -= max(damage - armor, 1)` — a 100 hp tree and a 20-damage axe *is* a 5-hit tree). Tool gate is `GroupData required_item` checked in combat. **Drops are one polymorphic `SData[]`** where an entry may be an item, a construction, a plant (stump→sapling), a spawn, or a probability wrapper — genuinely good design. Regrowth writes a record into the save rather than holding a live timer. |
| **a lot** | `Gameplay/Buildable.cs:272-395` — the **structure** of validity | Four gates: overlap / flat-ground / valid-floor / accessible. The useful trick is **layer masking `obstacle_layer & ~floor_layer`** so the floor never counts as an obstacle. Plus a 0.5 s slow-tick re-validation that kills a piece when its floor vanishes — *"chop the support post, the roof falls"*. ⚠️ **That is the only structural rule in the package — there is no stability/stress propagation.** |
| **a lot** | The **save diff model** (`Data/PlayerData.cs:35-50`) | Two populations: authored-world objects store only a **removal key set** (`removed_objects[uid] = 1`), runtime-created objects store full records. **The save is a diff against the authored world, not the world.** Plus a **sub-UID scalar sidecar** (`GetSubUID("progress")` → `SetCustomFloat`) giving any component a schemaless property bag with zero boilerplate. **This generalises our `SurfaceEditStamp` spine cleanly.** |
| **1 thing** | `DurabilityType {UsageCount, UsageTime, Spoilage}` | Three semantics collapsed onto one float. Elegant. |
| **1 thing** | Item↔build cross-links (`plant_data`, `construction_data`, `container_data`) | How "seed → plant" and "hammer → building" work with no extra system. |
| **1 thing** | `Gameplay/Plant.cs` two-accumulator growth | `growth_progress` + `fruit_progress`, stages as separate prefabs, boost multiplier. Portable and orientation-independent. ⚠️ **Watering is not soil state** — `HasWater()` is literally `boost_timer > 0`, an unsaved transient lost on reload. Too thin for a wizard garden. |

⚠️ **`Buildable.cs` is the worst flat-world offender in the package — 16 × `Vector3.up|down`.** Every probe hard-codes world up/down; height resolution is Y-only; grid snap is world-axis `RoundToInt` on absolute Y; rotation snap quantises `euler.y` only. `Tools/PhysicsTool.cs` computes ground distance as a scalar `.y`. **Every line of the geometry must be rewritten against a surface frame** — but the 5-point probe becomes 5 *analytic* height samples on the tangent plane, which is **cheaper than what they do** and removes the dependency on the unbuilt physics bubble.

⚠️ **Two structural scaling failures, both fatal at planet scale:**
- `TheGame.UpdateDurability()` runs from `Update()` and iterates **every dropped item, inventory, construction, timed bonus and pending regrowth in the entire save, every single frame.** Replace with timestamps evaluated lazily on chunk stream-in.
- `TheGame.Start()` instantiates the whole world in five `foreach` loops. **No spatial index; records carry only a `scene` string.** Also `removed_objects` grows monotonically and is never compacted.

✅ **Good news:** **zero Unity `Terrain` references** across all 200 files. NavMesh is confined to 4 files (all NPC/pet movement — reference-only for us).

### 5.4 The other three — mostly rejected ✅ inspected

| Asset | Verdict |
|---|---|
| **RPG Farming Kit** | ⚠️ **It's 2D.** Sprite-based Stardew clone — `SpriteRenderer`, `UnityEngine.Tilemaps`, `Vector3Int` cell coords. No 3D placement code to port, and **seasons are not implemented** (single grep hit, and it's a `// TODO`). **Take exactly two things:** (1) the **`ISaveable` + per-component JSON** pattern from its bundled Lowscope save system — strictly better *serialization* than Survival Engine's `BinaryFormatter` god-blob; (2) the principle that **soil state lives in the world, not on the plant** (it's a tile on a named tilemap) — which for us means a moisture/tilled channel in `SurfaceEditStamp`. |
| **Inventory Pro** | ❌ **Disqualified at line 16: `public partial class InventoryItemBase : MonoBehaviour`.** Item *definitions* are **prefabs**, not ScriptableObjects — so they can't be pure data and can't be DTO-snapshotted. Also carries a bundled FullSerializer fork, a localisation system, an audio manager, and a 2,223-line `ItemCollectionBase`. **Take one idea:** `StatDecorator[]`-shaped extensible per-item stats, and `ItemAmountRow`'s explicit `{item, amount}` pair (better than Survival Engine's duplicate-array-entries hack). |
| **Easy Grid Builder Pro** | ❌ **Skip entirely.** Hard grid-locked — the core datastructure is a `T[,]` with `(int x, int z)` coords, levels are **absolute world-Y layers**, and `EasyGridBuilderPro.cs` is a **4,251-line single class**. Its "free object" snapper is 27 lines that add a trigger box and snap to the object's *origin* — one socket, no orientation matching. Strictly weaker than UCS's `PortSnapper` in every dimension. Its save system builds a path from `Application.dataPath` (read-only in a build) inside a static initialiser that dereferences another singleton. |
| SmartBuilder / Whiskey Structure Builder / Auto Fence & Wall Builder | **maybe** | Editor-time level-design tools, not runtime player building. Auto Fence is interesting for **NPC town generation**. Not downloaded. |
| Interactor — Interaction Handler | ❌ **rejected** | See §16.4 — detection is a sphere trigger with 7 lines of prioritisation and no view-angle scoring. |
| RPG Builder | **maybe** | Not downloaded. Almost certainly too opinionated. |

### 5.5 The recommended data model

**`ItemDefinition : CraftableDefinition : ScriptableObject`** carrying Survival Engine's field set, identified by a **hashed-string `ItemId` struct** (UCS's value-type ID discipline, but human-authorable and diffable in a stamp), with `ItemStack`/`Quantity` value types, a **multi-output** `RecipeDefinition`, `GroupData`-style `TagAsset` references, and a snapshot-DTO layer per our settings convention.

### 5.6 ⚠️ Three decisions that must be made before writing any of it

These came out of the inspection and **no package solves them** — they're ours:

1. **Deterministic identity for procedurally-scattered objects.** Survival Engine hand-authors a `UniqueID` per scene object. Ours must be **derived from `(seed, cube face, quadtree node, scatter index)`** so "this tree is chopped" is a stamp entry with no per-tree serialization. **This is *the* prerequisite for generalising `SurfaceEditStamp` to harvesting.**
2. **Every record needs a spatial key.** Add cube-face + quadtree-node to buildings, plants, drops and regrowth records **now**. Survival Engine has only a `scene` string and loads the entire world at start. This is a schema change, cheap today and expensive later.
3. **`ISurfaceQuery` before anything else.** Placement, item settling, regrowth scatter and crop siting are all four-or-five downward raycasts against colliders **we don't have**. Route them all through one analytic surface service and **the physics bubble stays optional** — needed only for object-vs-object overlap during placement, which uses *building* colliders we spawn ourselves.
| Synty POLYGON Construction / Farm packs | **art** | Building pieces and farm props. See §14. | 🔎 expected |
| Fantastic Nature Pack camping set | **art** | Campfire, tent, sleeping bag, pot, pan, bowl, fishing rod, wood pile — already extracted and URP-confirmed. | ✅ inspected |
| Human Crafting Animations (Kevin Iglesias) | **art** | Craft/gather clips on the same rig as our base locomotion set. | 📦 cached |
| Crafting Mecanim Animation Pack | **art** | 130 clips: dig, chop, fish, carry, cart-push, climb. Built-in materials, but we only take clips. | ✅ inspected |
| RamsterZ Survival / Loot / Simple Activations | **art** | The verb animations — shoveling, pickaxe, tree-chop, hammering, foraging, floor-pickup, lever/valve/button. | ✅ inspected |

**Download gate (high priority):** Survival Engine, Ultimate Crafting System, RPG Farming Kit, Easy Grid Builder Pro, Inventory Pro.

---

## 6. Resource harvesting — chopping, mining, fishing, gathering 🔎

**Current state.** Nothing. But note we already have `SurfaceEditStamp` persistence (path wear, scorch) — **a harvested tree stump is the same shape of problem**: a persistent, seed-keyed world edit rebuilt from a stamp list. That existing spine is the right foundation, not a new one.

**The design question to settle first:** is a felled tree a *fracture* or a *prefab swap*? For a Synty-flat game the swap is almost certainly right — tree prefab → falling animation → stump prefab + log items. That makes the whole destruction cluster (DestroyIt, RayFire, Mesh Slicer, Impact Deformable) **optional**, not foundational.

| Asset | Take | What | Tag |
|---|---|---|---|
| Survival Engine | **CORE** | Its harvestable-node model: node → tool requirement → hit count → drops → respawn timer. See §5. | 🔎 expected |
| Crates & Barrels — Stylized Destructible Props | **1 thing** | How a *stylised* destructible is authored (pre-broken pieces, not runtime fracture). This is the cheap answer to "chop a tree" and it matches the art target. | 🔎 expected |
| DestroyIt | **maybe** | Full destruction system. Only if pre-broken swaps prove insufficient. | 🔎 expected |
| Mesh Slicer | **maybe** | Runtime mesh cutting. Fun, expensive, probably unnecessary. | 🔎 expected |
| RamsterZ Survival Animations | **art** | `Survival_Build_Shoveling`, `_PickAxe_LowHeight/MediumHeight`, `_TreeChop_Start/Vertical_Loop/Horizontal_Loop/Exit`, `_Foraging_BerryBush`, `_PrimitiveFishing_*`, `_SpearFishing_*`, `_Skinning_Ground`. | ✅ inspected |
| Crafting Mecanim Animation Pack | **art** | `Dig-Start/Idle/Scoop/Finish`, `Fishing-Cast/Idle/Reel/Finish`, `Chop-*` (+ `-Upper` partial-body), `Gather`, `Gather-Kneeling`, plus 19 usable low-poly tool props (Hammer, PickAxe, Shovel, Sickle, Rake, FishingPole). | ✅ inspected |
| Loot Anim Set | **art** | `Loot_FloorPickUp_Kneel_HarvestItem` is literally foraging; the `_Inspect_Enter/Loop/Keep/Exit` cycle is a "hold up the item you just got" state. | ✅ inspected |
| 3D Characters — Fish | **art** | Fish models for fishing. | 🔎 expected |
| SurfaceData: Effects System | **1 thing** | Maps surface type → footstep/impact FX+SFX. Directly reusable idea for "axe hits wood vs. pick hits stone", and our biome system already knows the surface type. | 🔎 expected |

**Download gate:** Survival Engine, Crates & Barrels, SurfaceData, 3D Characters — Fish.

---

## 7. Water, swimming, sailing & fishing

**Current state.** Ocean with caustics (**hard don't-touch**), flat water surface, no waves on the sphere, no swimming, no buoyancy. Ocean biome has **zero** scatter content. Roadmap #20 is ocean traversal.

### 7.1 Poseidon — **CORE**, and it's already cached ✅ inspected

Inside `Pinwheel Studio / Low Poly Tools Bundle` (which is really four products: Polaris, **Poseidon**, TextureGraph, Jupiter). **Poseidon is the Synty-style ocean we're missing**, it's URP-correct, and it's the smallest cleanest code of the six water/vegetation packages inspected.

| Take | Part | Why |
|---|---|---|
| **CORE** | `PCustomMeshBaker.cs:31-70` — the flat-shading trick | Bakes the **other two triangle vertices** into UV0 and vertex colour, so the vertex shader reconstructs the face normal and centroid itself (`UniversalRP_Forward.cginc:62-80`). Result: **per-facet flat lighting on a fully vertex-shaded, SRP-batchable mesh with no geometry shader** — and the normal stays correct *after* wave displacement. Exactly what a Synty-look spherical ocean needs. ~35 lines to re-implement. |
| **CORE** | `IPMeshCreator` / `PCustomMeshBaker.Bake(srcMesh)` | Takes **any** source mesh. Our cube-sphere ocean chunks go straight in — no clipmap, no flat-plane assumption. |
| **a lot** | `CGIncludes/PWave.cginc` — `SampleWaveCurve(t, _WaveSteepness)` | Waves are a **piecewise cubic Bézier crest**, not Gerstner. The middle control point slides `lerp(0.5, 0.95, steepness)` so crests sharpen **without Gerstner self-intersection**. Cheap, art-directable, and it looks low-poly by construction. `ApplyWaveHQ` displaces all three baked vertices so the reconstructed facet normal stays right. |
| **a lot** | `CGIncludes/PFoam.cginc` — `CalculateFoamColorHQ` | **Shoreline foam as a hard-clipped, noise-eroded band** (two counter-scrolling noise samples multiplied, then `noise >= depthFade` against `waterDepth = sceneDepth - surfaceDepth`) rather than a soft gradient. This is the correct stylised shoreline for our art target and it directly fills the "shoreline treatment" gap. Crest foam gates on shallow water via `crestDepthFade`. |
| **1 thing** | `crestMask = saturate(p.y / _WaveHeight)` | Falls out of the wave for free and drives crest foam. |
| **art / shaders** | `CGIncludes/{PDepth, PLightAbsorption, PFresnel, PRefraction, PMeshNoise}.cginc` | Small, single-purpose, URP-correct. `PLightAbsorption` is Beer-law depth colour. **These are shaders, so importing them is allowed under the rule.** |
| ⛔ | `PCaustic.cginc` | **Leave alone** — caustics are the hard don't-touch. |
| ❌ | `PWaterEffectRendererFeature.cs` | **The exact trap again** — `AddRenderPasses` + `Execute(ScriptableRenderContext, ref RenderingData)` and **zero `RecordRenderGraph` in the entire bundle**. Silently dead on 6000.6. The water *surface* shaders are ordinary URP forward shaders and are unaffected; only the underwater/wet-lens post-FX no-op. |

### 7.2 Oceanis — harvest four algorithms, ignore 534 MB ✅ inspected

⚠️ **Its main water shader isn't really URP.** `Oceanis 2024 Water.shader` (3,658 lines) declares no `"RenderPipeline"="UniversalPipeline"`; its base pass has `LightMode` **commented out** so URP picks it up as `SRPDefaultUnlit`, while the still-active `ForwardAdd` pass never draws. ~60% commented-out code, hardcoded Y-up throughout, and a 3,696-line God-MonoBehaviour controller.

| Take | Part | Why |
|---|---|---|
| **a lot** | `WaterIncludeSM30.cginc:195,230` — `GerstnerOffset4` / `GerstnerNormal4` | 4 waves in one `float4` swizzle; `Gerstner()` stacks five calls → 20 waves. ~15 lines, and exactly what you'd re-derive on a spherical tangent frame. |
| **a lot** | `WavesGenerator.cs:465-498` — `GetWaterHeight` | **Fixed-point inversion of horizontal displacement** (iterate `d = GetWaterDisplacement(pos - d)` three times) plus an `AsyncGPUReadback` of the displacement cascade. This is how you get CPU-side wave height for **buoyancy and swimming** without stalling the GPU. |
| **1 thing** | `Oceanis 2024 Water.shader:1174-1245` | Depth-texture-driven **shore wave modulation** — amplitude/speed/steepness rewritten in shallow water so waves steepen and slow. Complements Poseidon's foam. |
| **1 thing** | `InitialSpectrum.compute` | Proper JONSWAP/Donelan-Banner directional spectrum with finite-depth dispersion. Only if we ever want true FFT ocean. |
| **1 thing** | `CustomPostProcessingPassOCEANIS.cs:34-54` | A **real, working RenderGraph unsafe-pass** reference implementation (`renderGraph.AddUnsafePass<PassData>`, `builder.UseTexture(..., AccessFlags.ReadWrite)`). Useful template given how many assets get this wrong. |
| **1 thing** | `ComplexBuoyancy.cs` | Voxelised Archimedes buoyancy. Public-domain (Alex Zhdankin), header says *"do whatever you like"*. |

**Zero Unity Terrain dependency.** Shore behaviour is a depth render texture, not splatmaps.

### 7.3 Everything else for water

| Asset | Take | Note | Tag |
|---|---|---|---|
| Kinematic Character Controller | **CORE** | `Walkthrough/13- Swimming state/` — complete swimming state on the motor we're already adopting. No integration mismatch. | ✅ inspected |
| **Dynamic Water Physics 2** | **a lot** — reimplement ~370 lines | ✅ inspected. **Real submerged-volume buoyancy**, not point samples: a decimated 64-triangle sim mesh, each triangle **clipped against the water surface** into wetted sub-triangles, then a pressure integral `ρ·g·h·A·(n̂·ŵ)` accumulated per sub-triangle. Allocation-free managed C#, `in`/`ref` throughout. 🔑 **Its `WaterDataProvider` seam is a good shape to copy** (capability flags + batch queries, 8 shipped implementations prove it works) — **but the contract is a Y-heightfield**: `points` is `Vector3[]` while `waterHeights` is `float[]`, and `PointInWater` is `GetWaterHeight(...) > worldPoint.y`. A spherical ocean can't be expressed — antipodal sea-level points would need `+R` and `−R`. **The fix is three sites, all inside the algorithm, not the interface:** `d = P.y - h` → `d = dot(P − P_surf, up(P))` at `WaterObject.cs:852`, the same at `WaterDataProvider.cs:209`, and `_gravity.y` → `_gravity.magnitude` at `:1138`. Type the provider as `Vector3[] surfacePoints`. ⚠️ 8 × `[DefaultExecutionOrder]`, some coroutines. Also ships `SailController` + `WindGenerator` — relevant to sailing. |
| **Stylized Water 2** | ❌ **skip — Poseidon stays** | ✅ inspected. 🔴 **RenderGraph: FAILS, and deliberately.** `SetupConstants.cs:39` and `DisplacementPrePass.cs:124` both contain `public override void RecordRenderGraph(...) { }` — an **empty body under a `//Silence warning spam` comment.** The author knowingly stubbed it rather than port. So on 6000.6 the caustics projection matrix, SSR toggles and the entire displacement pre-pass **silently never run** — **asset #6 in this family.** Beyond that it's flat XZ Gerstner on a plane/tile grid (`offsets.y`, `xzVtx`, distance fade on `.xz`) and **cannot displace an arbitrary mesh**; its production shader isn't even a `.shader` but a `.watershader` generated by a ScriptedImporter. **One idea worth copying:** `Runtime/Buoyancy.cs` — a static CPU function that reproduces the vertex shader's displacement *exactly* by reading amplitude/frequency/direction back off the material, plus `CanTouchWater` as an O(1) broad-phase against `GetMaxWaveHeight`. **That discipline is how you make a spherical ocean queryable by swimming and sailing without a GPU readback.** |
| **FS Swimming System** | ❌ **skip — KCC wins** | ✅ inspected. Its entire water model is **one cached `float waterSurfaceY`** from a `Vector3.down` raycast, plus a `Vector3.up` sphere check against a hardcoded `"Water"` layer name. Strictly weaker than KCC's swimming state, which is already on our motor and expresses the surface as a plane we supply. No real buoyancy (a `CharacterController` resize plus a velocity clamp for loose objects). 9 coroutines in the swim scripts alone, no asmdefs, and it **drags in a whole competing third-person controller** (`LocomotionController` 1,233 lines, a 2,633-line input asset). **Worth 30 minutes of reading for one thing only:** its VFX/audio choreography — ripple-on-contact with dedup, edge foam, submerged bubbles, a `bigSplashThreshold`, climb-out event. Design reference, not code. |
| Crest Water 4 | **skip** | **BIRP only.** | ✅ from title |
| KWS Water System | **skip** | HDRP. | ✅ from title |
| Underwater FX / Running Water VFX | **art** | Underwater post, waterfall/stream FX. | 🔎 expected |
| POLYGON Pirates | **art** | 5 hull classes, ship wheels, anchors, docks, cranes. The sailing content. | ✅ inspected |
| Toon Adventure Island | **art** | Animated fish + seagulls, `TAI_Swordfish` with `_Bite`/`_Steak` loot states, nets, rafts, underwater plants. ⚠️ toon-outline style. | ✅ inspected |
| POLYGON Vikings | **art** | Best fishing props in the library — racks, nets, hanging fish, fishing spear. | ✅ inspected |
| Corals + Polyperfect aquatic ×13 | **art** | Ocean biome's first content. | ✅ inspected |

**Download gate:** Dynamic Water Physics 2 only. (Poseidon, Oceanis, Pirates, Vikings, Adventure Island are all cached.)

### 7.4 Immediate application — the deferred lake items

The Lake biome landed overnight (`a34c843`…`57579ce`) with **lily-pads-on-water and murky water tint deferred**. Both have answers already inventoried:

- **Lily pads / floating plants** — `SM_Env_LillyPads_01..04` (Synty PNB Swamp, already-owned biome folder, un-imported), `TFF_Water_LillY_Leaf_01A` + `TFF_Lotus_Leaf_01A` + `TFF_Glowing_Lilly_01A-02C` (Toon Fantasy Nature), `SM_Plant_Lillypad_Large_01..03` (PolygonNature), `TEM_Lily_Flower_01A-03A` (**already imported** in `TEM_Vegetation`). The last one means we can prototype floating-plant placement with zero new imports.
- **Murky water tint** — Poseidon's `PLightAbsorption.cginc` is Beer-law depth colour with per-channel absorption. That *is* the murky-water knob, and it's a shader so it can be imported directly.

This is the cheapest place in the whole document to convert research into a shipped change.

---

## 8. Weather, clouds & sky 🔎

**Current state.** Mature and shipped — volumetric raymarched clouds, a spherical weather grid with condensation/storm/rain-rate, atmosphere scattering, day/night via `CelestialManager`. Clouds are "parked needing polish" and run **full-res with no downsample** (the single largest untouched GPU cost; roadmap #3 is half-res raymarch).

**So this subsystem needs comparanda, not replacements.** Five owned cloud/weather systems exist purely to answer "how did they solve what we're stuck on".

### 8.1 RenderGraph verdict — **4 of 7 are dead on 6000.6** ✅ inspected

| Package | Renderer feature? | `RecordRenderGraph`? | Verdict |
|---|---|---|---|
| **Altos** | 5 passes | **Yes, all 5** (`AddUnsafePass`) | ✅ **ALIVE** |
| **Volumetric Fog & Mist 2** | 2 features | **Yes, 3 sites** | ✅ **ALIVE** |
| UniStorm | none — PostProcessing v1 + `CameraEvent` command buffers | n/a | ❌ **DEAD** (Built-in) |
| Aura 2 | none — `OnRenderImage` | n/a | ❌ **DEAD** — *the fifth instance of this failure* |
| Weatherade | none (material shaders, all Built-in) | n/a | ❌ **DEAD** |
| Brute Force Snow / Snowify | n/a | n/a | shaders / editor tool |

Altos even guards its legacy path with `#if !UNITY_6000_4_OR_NEWER` — they've already anticipated Compatibility Mode removal.

### 8.2 Altos — **the headline: up to a 16× reduction in rays cast** ✅ inspected

Altos stacks **three independent reductions**, and resolution is chosen by literally incrementing an enum (`VolumetricCloudsRenderPass.cs:209-273`):

```csharp
var cloudScale = Scale.Full;
if (useReprojection)                  cloudScale++;
if (resolutionOptions == Half)        cloudScale++;
// CreateDescriptor: d.width >>= (int)scale;
```

With both on, **the raymarch runs at quarter resolution — 1/16 the rays.** The chain is: depth downsample → **RenderClouds at ¼ res** → reproject to ½ → upscale to full → TAA → merge.

| Take | Part | Why |
|---|---|---|
| **CORE** | The whole amortisation chain — `Reproject.shader`, `UpscaleClouds.shader`, `DitherDepth.shader`, `TextureUtils.hlsl` | Small, self-contained, **URP-native shaders → importable**. Directly answers roadmap #3 (our clouds are full-res, no downsample — the largest untouched GPU cost). |
| **CORE** | **2×2 checkerboard reprojection over 4 frames** (`Reproject.shader:70-76`) | Only one quadrant is freshly marched per frame; the rest reproject from history via motion vectors with an **8-tap neighbourhood clamp**. Honest fallbacks: `_IsFirstFrame` re-marches full screen (so screenshots are correct), off-screen history UV re-marches that pixel. |
| **a lot** | **Checkerboard min/max depth downsample** (`TextureUtils.hlsl:112`) — **steal verbatim** | Alternates `min`/`max` per texel so both near and far silhouettes survive the halving. Thin geometry doesn't disappear. Nearly free. |
| **a lot** | Two-tier step scheme with **backtrack-on-hit** (`RenderCloudsPass.hlsl:1145-1217`) | Step counts derived from actual ray/shell intersection thickness, not a fixed world distance. On first non-empty sample it *rewinds one coarse step* — avoids the classic one-step-late entry artifact. |
| **1 thing** | Mip-LOD by distance, with in-scattering deliberately sampling `mip + 2` | Cheap and physically defensible. |

🔴 **Do not copy their upsample weight — it's a genuine bug.** `UpscaleClouds.shader:24-62` computes `exp((-1.0/SIGMA2) * dot(depth, depth00))`. `depth` is a **scalar**, so `dot(a,b)` is the *product*, not the squared difference. With `-1/SIGMA2 = -1e6`: in sky (depth→0) every weight becomes `exp(0)=1` and it degenerates to an **unweighted box filter**; near geometry all weights underflow to 0 and it returns **black**. Altos ships a nominally depth-aware upsample that isn't. **Use VFM2's edge-preserving upscale with an explicit depth threshold as the correct reference**, and write the weight as a squared depth *difference* in linear eye space.

🔑 **Altos is one line from being planet-correct.** Height is radial everywhere (`distance(rayPos, _PLANET_CENTER) - (planetRadius + atmosHeight)`), shell entry/exit is real ray-sphere intersection, and there's a proper horizon falloff. But `RenderCloudsPass.hlsl:1027` re-pins `_PLANET_CENTER` **directly under the camera in XZ every frame** — so the camera is permanently at the north pole of a sphere that slides beneath it. **Flying up works** (altitude is true radial distance); flying *around* doesn't. Substitute our real planet centre and everything downstream is already correct spherical math.

⚠️ Its `CloudShaderParamHelper.cs` is a proper ID hub (182 `PropertyToID`s) — the same pattern as our `ShaderGlobalIds` — but `VolumetricCloudsRenderPass.cs` bypasses it with ~8 raw-string globals (`"_UseReprojection"`, `"_RenderScale"`, `"_PreviousFrame"`, `"_IsFirstFrame"`…). All must be registered if we re-implement.

❌ **Nothing to harvest from its weather model** — there is no weather-type state machine at all. `WeatherManager.cs` is 83 lines of precipitation-intensity sphere, and cloudiness is a hash-per-integer-hour smoothstepped scalar. **Our spherical grid is strictly more sophisticated.**

### 8.3 UniStorm — take the fan-out shape, nothing else ✅ inspected

Rendering is a write-off (PostProcessing Stack v1, `OnRenderImage`, `CameraEvent` command buffers — all inert under URP).

Its weather model is also **a generation behind ours**: one global weather state for the whole world, selected by a **single precipitation-probability dice roll** against a seasonal `AnimationCurve` keyed on day-of-year, with rejection sampling for season/temperature constraints. No spatial dimension whatsoever. ⚠️ The rejection loop is an unbounded `while` with `break` only in the `else` — a badly configured type list hangs it.

**What *is* worth taking is the fan-out architecture.** `ChangeWeather()` kicks off ~20 independent per-channel tweens toward that weather type's targets, each scaled by a shared `TransitionSpeed`:

```csharp
CloudCoroutine  = StartCoroutine(CloudFadeSequence(10 * TransitionSpeed, …));
FogCoroutine    = StartCoroutine(FogFadeSequence  ( 5 * TransitionSpeed, …));
RainShaderCoroutine = StartCoroutine(RainShaderFadeSequence(20 * TransitionSpeed, 1, false));
SnowShaderCoroutine = StartCoroutine(SnowShaderFadeSequence(20 * TransitionSpeed, 0, true));
```

Three design points worth keeping: each channel **stores its handle so a re-entrant change cancels the in-flight tween**; channels get **deliberately different durations** (fog 5×, sky 10×, **surface wetness/snow 20× — the ground response lags the sky**); and rain/snow are **mutually exclusive ramps**. **Rebuild as one `Awaitable` per channel with a per-channel `CancellationToken`.** Also worth stealing: its **precomputed 24-hour forecast queue** as a UI/gameplay affordance, and the seasonal probability curve as a *bias input* to our grid.

### 8.4 Surface response to weather — the real gap ✅ inspected

| Asset | Verdict |
|---|---|
| **Weatherade** | ✅ **Best algorithm** — and **the accumulation direction is already a uniform, not a constant**: `DirMask(dirRange, depthCamDir, surfNormal, dirOffset)` fed from C# via `Shader.SetGlobalVector("_depthCamDir", sceneDepthCam.transform.forward)`. **`dot(normal, radialUp)` falls out for free.** Also has occlusion (no snow under roofs) via a projected coverage volume, **variance-shadow-map soft area edges**, paint add/erase masks and dynamic footprint traces. ❌ **But its shaders are 100% Built-in — zero URP markers anywhere — so they cannot be imported despite the shader allowance.** Pure re-implementation job, which harvest-only requires anyway. Two `.y` leaks to skip: `CoverageBase.cs:595` and `SRS_RainCoverage.cginc:210`. |
| **Brute Force Snow & Ice** | ⚠️ **The only importable URP snow shaders** — but coverage is **baked into mesh vertex-colour alpha at edit time** (`Vector3.Angle(Vector3.up, norm)` on the CPU), so **it cannot respond to a live weather grid without re-baking**. Displacement is hardcoded `float4(0, h, 0, 0) * _UpVector` where `_UpVector` is a *scalar weight*, not a direction. Partial escape hatch: `_NormalVector` weights displacement along the mesh normal instead, which on terrain ≈ radial. Proper fix is a small localised edit to `float4(radialUp * h, 0)`. |
| **Snowify** | Editor-time only, no runtime, no shaders. Extrudes a **separate snow-cap mesh**; `snowDirection` is a *serialized field*, so per-object radial would work. **Best aesthetic match for Synty** — a chunky extruded cap reads correctly on flat-shaded geometry where a texture blend reads as mush. File under **static biome dressing**, not dynamic accumulation. |

**Recommended shape:** Weatherade's `DirMask` + coverage-volume concept, but **driven by our weather grid directly** (we already have condensation/rain-rate per cell — sample that instead of a projected mask) with `radialUp = normalize(worldPos - planetCentre)`. That skips their whole orthographic projector rig *and* its world-up leaks.

### 8.5 Ground fog ✅ inspected

**Volumetric Fog & Mist 2 — alive, and it has the hook we want.** Per-volume 2D-heightfield raymarch rather than a froxel grid, so cost scales with volume screen coverage. It ships **its own downsample + edge-preserving upscale with an explicit `downscalingEdgeDepthThreshold`** — a second data point for the cloud work, and the *correct* form Altos gets wrong.

🔑 **`FogOfWar.cginc` is a writable RGBA texture projected over a world-space rect** (`_FogOfWar`, up to 2048²), gated by keyword. Intended for RTS fog-of-war; the mechanism is a **generic world-space density mask**. **We can blit our biome + weather grid straight into it and drive fog density entirely from simulation instead of hand-placed volumes.** ⚠️ Hard-locked to Y-up / XZ (`wpos.xz`), so the mask projection needs a per-region tangent frame.

**Aura 2 — dead.** `OnRenderImage`, no `ScriptableRendererFeature`, no RenderGraph anywhere in 169 files. Architecturally it *is* the froxel design (frustum-aligned 3D grid, per-light injection, temporal reprojection) and its `Core/Code/Classes/` is worth reading if we ever build one — but as shipped it cannot render a pixel, and porting is a full camera-integration rewrite.

### 8.6 The rest

| Asset | Take | Note | Tag |
|---|---|---|---|
| Weather Maker / EzCloud | **maybe** | Read only if Altos leaves gaps. | 🔎 expected |
| InfiniCLOUD | **skip** | HDRP-first; ARTnGAME's URP ports are LWRP-era relics elsewhere in this library. | 🔎 expected |
| Jupiter — Procedural Sky | **1 thing** | Dusk-band handling as a `CelestialManager` comparand. | 🔎 expected |
| AllSky / Polyverse / Farland Skies | **art** | Mostly irrelevant — we render our own atmosphere. | 🔎 expected |
| Environment / Weather / Nature VFX pack | **art** | Rain splashes, wind gusts, falling leaves. | 🔎 expected |

**Download gate:** none remaining.

---

## 9. Digging, caves & voxel 🔎

**Current state.** Nothing. Designed as a future Phase 9; the collision-strategy doc notes SDF alignment (collision as a meshing byproduct).

### 9.1 Voxelica — **read-and-reimplement only; the engine cannot express a spherical shell** ✅ inspected

It is a **global XYZ lattice engine down to the bit-shift level.** Every read and write funnels through:

```csharp
public static int ConvertLocalToInner(float localCoordinate, float rootSize)
    => (int)((localCoordinate / rootSize) * INNERWIDTH);   // INNERWIDTH = 65536
```

and octree descent is pure bit arithmetic on that 16-bit lattice. Neighbour stitching is an explicit **26-neighbour axis-aligned offset** (`Neighbours[13 + (x + y*3 + z*9)]`). Its data model is a pointer octree — 48 B/node, `UnsafeUtility.Malloc` from a free-list reservoir, sparse by construction.

**Could edits be expressed in a surface-local frame?** Only trivially. The affine `worldToLocalMatrix` is the sole hook, so you could orient a whole chunk to a tangent basis — but **the chunk interior stays rectilinear**. Two adjacent surface-local chunks on a sphere are rotated relative to each other, so their lattices don't align, their marching-cube grids don't share boundary samples, and you get **cracks at every chunk boundary**. The border-skirt trick (`BorderedWidth = width + 3`) can't fix it because it assumes an axis-aligned neighbour.

It *does* ship a planet generator — and it's exactly what you'd expect: **a radial SDF carved out of a global cube lattice.** A ball inside a cube world. No cube-sphere, no quadtree, no radial-gravity awareness, no altitude-tied LOD.

| Take | Part | Why |
|---|---|---|
| **CORE** | **`Module_Transvoxel.cs` (2,630 lines)** | The highest-value artifact in the package. Complete Lengyel Transvoxel tables (`transitionCellClass[512]`, `TransitionCellData`, `transitionCornerData`, `transitionVertexData`) plus face-suppression logic. **If Phase 9 does caves with marching cubes across LOD levels, this saves transcribing the tables.** Data + a well-understood algorithm, not architecture. |
| **a lot** | The two-stage **"sample sparse store → dense bordered grid → Burst mesh job"** pattern | `NativeCreateUniformGrid_V3 : IJobParallelFor` `[BurstCompile]` then a `[BurstCompile] IJob` surface pass. **The `width + 3` border and bit-shift addressing are directly reusable against a surface-local `(u, v, radial)` grid** — the grid sampler doesn't care that the lattice is global, only the *tree* does. |
| **a lot** | The deferred **dirty-AABB → cell-queue → N-cells-per-frame** throttle | Clean, small, and exactly the shape a per-chunk edit budget needs. Note it's a hand-pumped `IEnumerator` state machine, **not `StartCoroutine`** — ports cleanly to `Awaitable`. |
| **1 thing** | `Module_DualContouring_CPU.cs` (QEF, sharp features) or `SimpleSurfaceNets.cs` (411 lines, cheapest smooth option) | Pick by look. |

🔑 **The design we should adopt instead:** store voxels in a **surface-local `(u, v, h)` lattice per cube-sphere quadtree chunk** — `u,v` being the chunk's existing face-local parametric coords, `h` radial altitude in fixed point. **Every neighbour's lattice then aligns automatically** (they already share the quadtree's u,v edges), radial gravity is trivially `down = -h`, and the marching-cube border skirt works unmodified. Voxelica gives us zero help with the mapping and almost all of the meshing math.

⚠️ **No greedy meshing anywhere.** ⚠️ **Collision cooking is synchronous** — `meshcollider.sharedMesh = voxelMesh` on the main thread, no `Physics.BakeMesh` job. With its default 8³ cells that's **up to 512 MeshColliders per chunk**. "Collision as a meshing byproduct" is the right shape, but the *cooking* is the part to redo. ⚠️ **LOD seam stitching is commented out** in the consolidated hull path (the Transvoxel module implements real transition cells but nothing wires them). ⚠️ Ships a **vendored fork of Unity.Mathematics and Unity.Collections** (~40k lines) that would collide with the real packages, and an asmdef literally named **`Internal`**.

### 9.2 The rest of the voxel section
| ARTnGAME Common Tools | **1 thing** | `MarchingCubesComputeShader.compute` — MIT (dario-zubovic). **Get it from the upstream GitHub repo**, not the vendored copy. | ✅ inspected |
| LUMINA GI (voxel GI) | **skip** | Different problem entirely. | 🔎 expected |
| POLYGON Dungeons | **art** | Cave entrances, cave interiors (incl. `SM_Env_Cave_01_DoubleSided`), rubble, cracked rock, crypt entrances. | 📦 cached |
| Synty PolygonNature | **art** | `SM_Rock_CaveEntrance_01/02`. | ✅ inspected |

**Download gate:** Voxelica.

---

## 10. Portals & fast travel 🔎

**Not previously in scope.** You've named portals as *the* fast-travel system, which makes this a shipping feature rather than a nicety.

### 10.1 The result: the **1 MB** asset beats the **158 MB** one ✅ inspected

**Fluid Seamless Portals — Basic** is the primary reference. Its core (`Teleport.cs`, `PortalCamMovement.cs`, `PortalSetup.cs`) contains **zero `Vector3.up`, zero `Vector3.down`, zero `Quaternion.LookRotation`, zero Y-only rotations.** All orientation maths is frame conjugation: `otherPortal.rotation * Quaternion.Inverse(thisPortal.rotation) * X`. The only world-up in the package is in its bundled sample FPS controllers, which we'd never ship.

**Dynamic Portals** has correct portal maths but its `Player.cs` is unusable on a sphere and, worse, **actively destroys the transferred orientation the next frame**: `_camRotZ = Mathf.LerpAngle(_camRotZ, 0, …)` is a *world-Z* roll recentre, so after any traversal the camera smoothly rotates itself back to global-up-aligned.

### 10.2 The elastic plane — the idea that makes traversal seamless ✅ inspected

**The insight: don't teleport at the instant the camera touches the plane. Push the visible plane away from the camera as it approaches, so the near plane can never clip it, and teleport at a threshold *after* the camera has notionally passed.**

`Teleport.cs:461` — three terms:

```csharp
float totalOffset =
      elasticPlaneOffset            // 1. constant margin, must exceed camera near plane
    + _trespassProgress             // 2. how far the player has crossed
    + relativeSpeed * velocityFactor; // 3. predictive term from camera delta
plane.position += TowardDestination(...) * totalOffset;
SetClippingOffset(-(_trespassProgress) + clippingOffset);   // ← the coupling
```

That last line is the actual mechanism: **the oblique clip plane walks backwards by the same amount the quad moves forward**, so rendered content stays registered with the displaced plane. `PortalSetup.cs:277` even asserts the invariant at boot (`nearClipPlane < elasticPlaneOffset`).

### 10.3 The composite spec — what to build

| # | Step | Source |
|---|---|---|
| 1 | Per frame: `trespass = dot(-portalForward, portalPos - cameraPos)` | Fluid |
| 2 | While crossing: displace the quad by `const + trespass + velocityTerm`, **and** set the destination camera's oblique clip offset to `-trespass + base`. Assert `const > nearClipPlane` at boot. | Fluid |
| 3 | At threshold, **atomically**: `Q = B.rotation * inverse(A.rotation)`; `pos = B.TransformPoint(A.InverseTransformPoint(pos))`; `rot = Q * rot`; `velocity = Q * velocity` minus the re-based portal velocity; **`localUp = Q * localUp`; `cameraYawRef = Q * cameraYawRef`** | Fluid + Dynamic + **ours** |
| 4 | Capture camera-relative-to-body offset **before**, re-apply **after**; force both portal cameras to `Recalculate()` before the render phase | Fluid `:206`, `:229` |
| 5 | ~50 ms re-entrancy lock + **emergency teleport on trigger exit** for the fast case | Fluid `:325`, `:392` |
| 6 | Quaternions only — **no Euler round-trips** | ⚠️ fixing Fluid's one real flaw |
| 7 | No `[DefaultExecutionOrder]` — one orchestrator drives `movement → crossing-detect → portal-cam-recalc → render`; hang the render off `RenderPipelineManager.beginCameraRendering` | ⚠️ replacing Fluid's mechanism |
| 8 | RT: screen-relative, 16-bit depth, `Release()` on resize, **pooled across pairs** | Fluid, improved |
| 9 | **Recursion depth 1.** Portal-in-portal is a puzzle-game feature we don't need. | ours |

🔑 **Line 3 contains the two lines neither asset has** — rotating `localUp` and the camera yaw reference by the same delta quaternion. **This is the planet wrinkle, and Fluid's design accommodates it natively**: `DoTeleport` already writes the object's full rotation and already captures/restores the camera's body-relative offset, so it's one extra statement in the same block. Dynamic Portals, by contrast, would need its player controller discarded wholesale.

### 10.4 What to take from each

| Asset | Take | Detail |
|---|---|---|
| **Fluid Seamless Portals** | **CORE** | Elastic plane + coupled clip offset (`Teleport.cs:461`); quaternion-conjugation teleport (`:167-186`); pre-render `Recalculate()` continuity (`:206`); camera-offset preserve/restore; re-entrancy lock + emergency teleport; the **near-distance oblique bail-out** (`PortalCamMovement.cs:90`) that Dynamic Portals lacks. ⚠️ **Fix on the way in:** the Euler round-trip at `PortalCamMovement.cs:64` — a gimbal/precision liability for **portal pairs on opposite sides of a planet, where delta rotations are near 180° about arbitrary axes**. |
| **Dynamic Portals** | **1 thing** | Velocity re-basing **including a portal-velocity term** for moving portals (`Portal.cs:143-161`) — the one thing Fluid lacks. ⚠️ its own line divides by `Time.deltaTime` in `FixedUpdate`; take the idea, not the line. |
| **Skill & Attack Indicators** | **art (shaders)** | See §10.5. |
| **Fantasy Portal FX** | ❌ **skip** | Bloom-heavy, normal-mapped, semi-realistic — reads as generic asset-store magic, a different visual language from Synty's flat colours and hard silhouettes. Frame models are redundant (we have Synty's). **One idea worth 2 minutes:** a *backdrop quad* behind the portal surface so an unrendered/culled portal shows something rather than see-through geometry. |
| **KCC** | **1 thing** | `Teleporter.cs` — the simple `OnCharacterTeleport` event hook our motor needs so grounding and camera don't fight the discontinuity. |

⚠️ **Neither asset solves mesh clipping of the traveller at the portal plane.** Both use pose-swapped clones and let the model visibly intersect. If that reads badly on the wizard, budget a world-space `clip()` plane in the character shader — a separate problem.

### 10.5 AoE telegraphs — solved, and better than expected ✅ inspected

**Skill & Attack Indicators** (actually *DTT Area of Effect Regions*) ships three parallel implementations. **Only the `SRP/` folder matters, and it's exactly right for us.**

Its four Shader Graphs target `UniversalDecalSubTarget` — **URP decals, projected from the depth buffer**. That means:

- **No ground colliders needed** — decals key off depth, not physics. Our analytic grounding is irrelevant here.
- **No Unity Terrain needed** — our cube-sphere chunks write depth like anything else.
- **Curvature is handled natively.** Orient the projector so **−Z points along the planet's local down** and give `size.z` generous depth; the decal wraps whatever it hits.

Grep for `Terrain|Raycast|SampleHeight|heightmap` across its runtime returns nothing.

**Take:** the four graphs — circle with radial `_FillProgress` (a clean cast-bar convention), arc/cone with adjustable angle, line, scatter-line. **These are shaders, so importable.** The C# is ~50 lines of `DecalProjector.size`/`.pivot` bookkeeping.
⚠️ Requires adding the **URP Decal Renderer Feature** to our renderer — a renderer-asset change, but a non-event given how depth-heavy we already are. Watch for decal angle-fade thinning indicators on steep slopes.
❌ **Ignore the mesh-indicator path entirely** — hard XZ-plane offsets, flat quads.

### 10.6 Highlight Plus — **the first asset to pass the RenderGraph test** ✅ inspected

After four silent failures, this one is done properly. `HighlightPlusRenderPassFeature.cs:180`:

```csharp
public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
    using (var builder = renderGraph.AddUnsafePass<PassData>("Highlight Plus Pass RG", out var passData)) {
        builder.AllowPassCulling(false);
        ...
        builder.UseTexture(resourceData.activeColorTexture, AccessFlags.ReadWrite);
        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
```

Both the legacy `Execute` and the RG path share **one** `static void ExecutePass(PassData)`, so there's no behaviour drift. And `builder.UseTexture(cameraDepthTexture, Read)` is precisely the dependency declaration the four broken assets were missing.

**Take:** the outline blur/compose shader trio and the **see-through** shaders (a wizard behind a rock should still highlight) — for our "look at a thing, press E" prompt we need maybe 10% of the package. **Independently valuable: `:180` is a correct reference implementation of an `AddUnsafePass` RenderGraph migration**, useful for any of our own features still on the `Execute` path. ⚠️ Route its two `cmd.SetGlobalFloat` names (`FadeFactor`, `ResampleScale`) through `ShaderGlobalIds`. ⚠️ It uses stencil — check for conflicts with our existing passes.

**Download gate:** none remaining for this subsystem.

---

## 11. Mounts, taming, carts & flight ⏳

**Current state.** Nothing. You want horses first, other mounts later, carts progressing wheelbarrow → handcart → wagon, and late-game flight.

### 11.1 Malbers Horse Animset Pro — the riding architecture ✅ inspected

359 MB, and it ships the **whole Animal Controller**, not just riding. The riding layer itself is only **12 files** under `Common/Scripts/Riding System/`.

**How mounting actually works** — worth knowing before designing ours:

1. `MountTriggers.OnTriggerEnter` filters on `other == rider.MainCollider` (body-part colliders rejected) → caches the mount, sets `CanMount`.
2. `MRider.MountAnimal()` sets an animator **bool** `Mount` and **int** `MountSide`. It **never calls `Anim.Play`** — the Animator layer transitions on those two params.
3. `MountBehavior` (a `StateMachineBehaviour`) `OnStateEnter` → `Start_Mounting()`; **`OnStateMove` drives the transit** — takes `animator.rootRotation` + `animator.velocity * deltaTime * ScaleFactor`, lerps toward the trigger for the first 0.2 of normalised time, then toward `MountPoint` via an `AnimationCurve` or a `TransformAnimation` asset.
4. `OnStateExit` → `ConnectWithMount()`.

So: **trigger volumes for discovery, animator params for intent, `OnStateMove` for transit, hard transform writes.** No animation events anywhere.

| Take | Part | Why |
|---|---|---|
| **a lot** | The mount/dismount state shape (`MRider` + `Mount` + `MountTriggers` + the two `StateMachineBehaviour`s) | Small and clean enough to re-implement in a day. **Replace `OnStateMove` with an `Awaitable` transit loop reading `animator.deltaPosition`/`deltaRotation` in `LateUpdate`** — same result, no `StateMachineBehaviour`, and it fits our rules. |
| **a lot** | **Parameter mirroring, not IK, for rider/mount pose sync** | `ConnectWithMount()` subscribes the rider's animator setters to the animal's `SetBool/Int/Float/TriggerParameter` delegates, and `Animators_Locomotion_ReSync` force-plays the rider clip at the horse's phase when they drift past `ResyncThreshold` (0.1). **Two Animators, one driving the other.** This is the non-obvious trick that makes riding look right. |
| **1 thing** | Reins are **not** IK and not physics | `IK_Reins()` just writes `Montura.LeftRein.position = LeftHand.TransformPoint(LeftReinOffset)`. Two transforms on the horse dragged to the hand bones each IK pass, with `freeLeftHand/freeRightHand` routing both reins to one hand when a weapon occupies the other. Real IK is used only for **feet** (`SetIKPosition(LeftFoot, Montura.FootLeftIK.position)` + knee hints) and a `SetLookAtPosition` for spine. |
| **1 thing** | On-mount state swap | Rigidbody frozen + `useGravity=false`, **all rider colliders disabled** ("or the Rider will try to push the animal"), capsule swapped for a `MountCollider`, ground controller slept via `ISleepController`. A checklist we'd otherwise discover by bug. |
| **art** | 105 **Humanoid** rider clips | Retarget onto the Synty hero. Includes 12 mount/dismount clips — `_Above`, `_Left/_Right`, `_Front_Left/_Front_Right`, `_Back`, `_Carriage_Left/_Right`, `_Cart_Left/_Right`, and **`_Dragon`** and **`_Elephant_Left`**. Plus `Rider_Fly_Flap/Glide/Lean/Stand`. |
| **art** | 138 Generic horse clips + 18 wing clips | Full gait set with 16 `_IP` in-place variants, **plus a complete flight set** (`H_Fly_Flap/Glide/Glide Closed/Stand/Back/Strafe_*`) — late-game flying mount, already animated. Meshes include Realistic / **Poly Art** / MineCraft horses, wings, horns; Poly Art is the one that sits next to Synty. |

⚠️ **Not separable as code.** `Mount` has `[RequiredField] public MAnimal Animal`, and `MRider` reaches into `Animal.hash_State`, `hash_Grounded`, `hash_Mode`, `ActiveStateID`, `ScaleFactor`, `AdditiveRotation`, `StateCycle`… Riding pulls `MTools` ×26, `BoolReference` ×20, `MEvent` ×19 from Malbers core. **Re-implement, don't extract.**

✅ **Good news for the sphere:** the riding layer has only **4** `Vector3.up` uses (all trivially replaceable) and **zero** NavMesh. And `MAnimal.UpVector => -m_gravityDir.Value` means **Malbers' quadruped locomotion core is already gravity-direction-agnostic** — read `MAnimalLogic.cs` before writing our own creature controller.

### 11.2 Carts & wagons — half useful

| Part | Verdict |
|---|---|
| `WagonController` | A **vehicle**, not a trailer: Rigidbody + **4 `WheelCollider`s** + a `ConfigurableJoint` to the horse's Rigidbody. ⚠️ `GetStearAngle()` does `BodyDirection.y = StearDirection.y = 0` — **flattens to XZ, breaks on a sphere**. `WheelCollider` itself is hardcoded to world-down gravity and a flat plane: **dead end.** |
| **The transferable idea** | `ConfigurableJoint` from puller-rigidbody to vehicle-rigidbody, plus a separate `StearCollider` child whose forward is compared against the body forward to derive steer angle, with a **jackknife-angle limit** (45°) that triggers rotate-in-place. Swap the `y=0` flattening for `Vector3.ProjectOnPlane(dir, localUp)` and it works. Replace `WheelCollider` with our own analytic wheel raycast against the local surface normal. |
| `PullingHorses` | **Already uses `MainAnimal.UpVector`, not `Vector3.up`** — this part is gravity-agnostic as written. Only the second-horse offset has a stray `.y`. |
| `PullWagons` | 39 lines, reads `Input.GetAxis` directly, `RotateAround(..., Vector3.up, ...)`. **Throwaway.** |
| **art** | 9 Humanoid `Rider_CarriageCart_*` clips (Speed1/Speed2/Wip × centre/left/right) + 4 carriage/cart mount clips. Wagon prefabs and a `Western Wagon.FBX`. |

### 11.3 Everything else for mounts, carts and flight

| Asset | Take | What | Tag |
|---|---|---|---|
| KCC | **a lot** | `PhysicsMover` + `IMoverController` — standing on a moving platform. **A boat deck and a moving cart are the same problem**, already solved on the motor we're adopting. | ✅ inspected |
| KCC | **1 thing** | `Walkthrough/12- NoClip state/` — structural template for a **flight state**. | ✅ inspected |
| POLYGON Fantasy Kingdom | **art** | The full progression: `SM_Veh_Wheelbarrow_01` → `SM_Veh_Cart_01–04` → `SM_Veh_TraderWagon_01`, plus `SM_Prop_Horse_Hitching_Post_01`. | ✅ inspected |
| POLYGON Vikings | **art** | `SM_Prop_Wheel_Barrow_01` + `SM_Prop_Wagon_01`. | ✅ inspected |
| POLYGON Horse | **art** | The **Synty-native** mount — preferable to Toon Farm's style-mismatched horse. Cached, not yet inspected. | 📦 cached |
| Toon Farm Pack | **art** | Only pack with **animated livestock** (horse ×11 variants with its own animation folder, cow, goat, pig, sheep, chicken, duck, dog). ⚠️ toon-outline style. | ✅ inspected |
| Wheel Controller 3D | **maybe** | Only if wagons ever need real suspension. Given `WheelCollider`'s flat-world assumption, our own analytic wheel is probably better anyway. | 🔎 expected |
| Obi Rope / Filo | **maybe** | Tethers between draft animal and cart. | 🔎 expected |
| Stylized Fantasy Dragons / Dragons Customizable / Tiny Dragon 3 | **art** | Late-game flying mounts. Malbers already supplies `Rider_Mount_Dismount_Dragon`. | 🔎 expected |
| **Wyrms** | ⚠️ **corrupt** | **The cached 670 MB file is a truncated download** — the tar stream ends mid-entry and the gzip ISIZE doesn't match. Only 108 entries recoverable. **Re-download before evaluating.** Also: realistic PBR (Synty clash), Built-in Amplify shaders, and ships `.terrainlayer` assets. Likely skip even when intact. | ⚠️ inspected |

**Download gate:** Stylized Fantasy Dragons Pack, Dragons — Customizable Dragon Pack. **Re-download: Wyrms** (currently corrupt).

---

## 12. Editor tooling & dev workflow

Per the rule these live in the scratch project — **except** the ones that are genuinely editor-assembly-only and would improve daily work here. Those are flagged for your ruling in §Maybe.

| Asset | Take | What | Tag |
|---|---|---|---|
| Odin Inspector and Serializer | **maybe (in-repo?)** | Would substantially improve the settings-SO authoring surfaces we hand-roll. Editor-only for the inspector half; the *serializer* half is runtime, which the rule bars. Worth an explicit decision. | 📦 cached |
| Hot Reload | **maybe (in-repo?)** | Edit C# without domain reload. Pure dev-velocity; no shipped code. | 📦 cached |
| Broccoli Tree Creator | **scratch bakery** | Bake LOD'd tree prefabs + billboard atlases; import output only. | ✅ inspected |
| **The Vegetation Engine** (base) | **scratch bakery — highest-value tool in the library** | ✅ inspected. **Confirmed: the base resolves all four of the Polygonal module's missing asmdef GUIDs**, so all three modules compile once it's present. 🔑 **The prize is its sidecar conversion pattern**, and it generalises exactly onto our `Planet/PropLit` pipeline — see §12.1. Its Element/Geometry shaders are importable art, and **`Prop Standard Lit`/`Prop Subsurface Lit` exist**, i.e. it already treats non-vegetation props as a first-class conversion target. Editor/runtime split is clean (5 well-named asmdefs, correctly platform-gated; **zero coroutines, zero `async void`, zero `[DefaultExecutionOrder]`, zero `RuntimeInitializeOnLoadMethod` in the runtime** — the best-behaved package in the whole survey). ⚠️ ~80 **raw-string `TVE_*` shader globals**, no `PropertyToID` caching anywhere. ⚠️ **Wind direction is packed as `(dir.x, dir.z)` — a 2-component compass**, meaningless away from one pole-facing patch on a sphere; any harvested motion shader needs it re-derived per-vertex from the surface tangent frame. |
| The Vegetation Engine **Terrain Details Module** | ❌ **skip** | ✅ inspected. It works by **shadowing Unity's built-in `Hidden/TerrainEngine/Details/WavingDoublePass`**. With no Unity Terrain it renders nothing, and there is **no non-Terrain path**. We have GPU compute grass anyway. |
| Asset Inventory 4 | **scratch tool** | Indexes the whole library with semantic + code search. Never been run. Would make the *next* version of this document unnecessary. | 📦 cached |
| Quantum Console | **⏳ pending** | We have ~193 commands already; reading only for gaps. | 📦 cached |
| Shapes (Freya Holmér) | ❌ **do not import** | ✅ **Resolved 2026-08-12.** The "build fails on missing Shapes files" note was a **stale generated artifact**, not a missing dependency — 33 of 39 csprojs referenced deleted sources. Deleting `*.csproj`/`*.sln` (gitignored, untracked) and regenerating produced 6 clean projects, zero missing references. Nothing in the project references Shapes. | 📦 cached |
| ALINE / Draw XXL / Better Gizmos / Oh My Gizmos | **maybe** | Debug drawing. ALINE is the fast one; we do a lot of gizmo-based diagnosis. | mixed |
| vHierarchy 2 / vInspector 2 / vFolders 2 / vTabs 2 / vFavorites 2 | **maybe** | Editor QoL, all editor-only. | 📦 cached |
| Grabbit | **maybe** | Editor physics transforms — useful for hand-placing props on a curved surface. | 📦 cached |
| Graphy | **1 thing** | In-game FPS/stats overlay. We have an F6 HUD already; compare only. | 🔎 expected |
| Fast Script Reload | **maybe** | Overlaps Hot Reload; pick one. | 📦 cached |
| Editor Console Pro / Smart Console | **skip** | We have our own console. | 📦 cached |

### 12.1 The TVE sidecar pattern — copy this into the `Planet/PropLit` pipeline ✅ inspected

We already re-material third-party props onto `Planet/PropLit` by hand, across a growing number of packs. TVE solved the same problem properly, and **the pattern is worth more than the code.** Four moves:

**1. Put the source GUID in the sidecar's filename.**
```
outputDataFormat = "CONVERTROOT/Prefabs Data/DATATYPEs/DATANAME DATAGUID (TVE DATATYPE)"
```
A Synty prefab at `Assets/Synty/Prefabs/Tree_01.prefab` yields `.../Prefabs Data/Models/Tree_01 <srcGUID> (TVE Model).asset`. **Re-conversion becomes a filename lookup** — no side database to keep in sync.

**2. Serialise the recipe onto the converted prefab, not into a database.** A three-string component:
```csharp
public string storedPrefabBackupGUID;
public string storedPreset;      // "PresetName;OptionName"
public string storedOverrides;   // "OverrideA;OverrideB;"
```
Re-opening the converter reads these back and re-selects the same options. **That is what makes re-conversion idempotent.**

**3. Overwrite sidecars in place so GUIDs never churn** — the key trick:
```csharp
if (File.Exists(savePath)) {
    var asset = AssetDatabase.LoadAssetAtPath<Mesh>(savePath);
    asset.Clear();
    EditorUtility.CopySerialized(mesh, asset);   // GUID preserved
} else {
    AssetDatabase.CreateAsset(mesh, savePath);
}
```
Every prefab and scene reference survives a re-run. Without this, re-converting breaks every reference in the project.

**4. Keep an untouched backup and a revert path.** `AssetDatabase.CopyAsset(src, ".../<name> (TVE Backup).prefab")` plus a `RevertPrefab()` that re-instantiates from the stored backup GUID. Conversion is non-destructive by construction.

Two more details worth stealing: an **`_IsTVEShader` marker property** on the material so the converter can tell "already ours, keep the tuned values" from "adopt the uber-shader", and an **`_IsShared` flag** so N prefabs whose source materials matched can share one converted material. Its collider path also reuses an already-written model sidecar when the collider mesh is the same asset, avoiding a duplicate cook.

**Per the rule, TVE itself runs in the scratch project and only its output crosses over** — but this pattern should be implemented in *our* editor tooling, because it's the thing that makes importing 20 more prop packs sustainable rather than a one-way manual grind.

---

## 13. Audio 🔎

**Current state.** Effectively nothing. This is the emptiest subsystem in the project and one of the cheapest to improve.

### 13.1 Ambient Sounds — **the layering model is a direct hit; the engine is a trap** ✅ inspected

**The deciding answer: it is fully external-data-driven, and there is not a single collider, trigger or zone anywhere in the driving path.**

```csharp
AmbienceManager.SetValue(string Name, float Value)   // :1866, clamped 0..1
AmbienceManager.ActivateEvent(string EventName)      // :1751
```

Those feed `Sequence.FadeValue` (`Sequence.cs:209-221`), which combines named-value ranges by `ALL`→`Min`, `ANY`→`Max`, `NONE`→`1-max`. **This maps onto our existing data with no adaptation**: `SetValue("rain", weather.RainRate)`, `SetValue("night", 1 - dayFactor)`, and — the good trick — **feed the biome *blend weight* rather than a hard biome ID and you get crossfaded ambience across biome borders for free**, which is the same seam-hiding problem the terrain already has.

| Take | Part | Why |
|---|---|---|
| **CORE** | **`SliderRange.cs` (45 lines) — copy `Eval` near-verbatim** | Named float range with dual falloff and invert. The single most valuable thing in the package; maps 1:1 onto rain rate, storm intensity, humidity, sun altitude and biome blend weight. |
| **CORE** | The Value + Event gating model (~80 lines) | `EvaluationType {ALL, ANY, NONE}` + `CheckValues`/`CheckEvents` combination rules. No dependencies. |
| **a lot** | The `Sequence` SO **schema** → our `AmbienceLayerSettings` SO/DTO | Clip list with per-clip volume, `m_trackFadeTime`, `m_crossFade`, `m_randomizeOrder`, `m_loopCount`, and crucially **`m_delayChance` + `m_minMaxDelay`** — what makes one-shots like distant thunder feel non-mechanical. |
| **a lot** | **Max-combine resolution** | Many drivers can request the same layer; **loudest request wins**. Cheap, correct, and no priority table to maintain. |
| **1 thing** | `Modifier` as a concept | Conditional clip-list *swap* (`Replace/Add/Remove`) — how you get "forest bed, but the bird layer becomes owls at night" without a combinatorial explosion of Sequences. |
| **art** | ~50 of its 82 WAVs | Exactly our gap: rain loops, `wind_0-0_lp`/`wind_gust`/`wind_howl`, `water_ocean_0-0_lp`, `thunder_clap`/`_distant`/`_roll`, 9 crickets, 11 birds, 5 frogs. Skip the `Spooky/`, `Technical/`, `Guns/` folders. |

⚠️ **Discard the engine — this is the disqualifying detail.** It implements **its own PCM mixer on the audio thread** and `RawAudioData` decompresses every clip fully into managed memory: `new float[BaseClip.samples * ChannelCount]`. **A 2-minute stereo 44.1 kHz ambience bed is ~42 MB of managed `float[]`**, and `m_preloadAudio = true` preloads every clip of every global Sequence on enable. Across a 17-biome × time-of-day × weather matrix that explodes. The mixer buys crossfade, pitch-stretch and dsp-locked `SyncGroup` — **none of which ambience beds need.** Play them through paired `AudioSource`s with a manual A/B crossfade and let Unity stream.

⚠️ Also: 6 coroutines, a singleton with static state surviving scene loads, 14 `FindObjectOfType` (some per-`Update`), no runtime asmdef. And `GetValue(name, AddIfMissing: true)` **silently inserts unknown names** — a typo fails open at 0 with no error. Re-implement with a validated key set, not raw strings.
⚠️ Spatialisation is half-safe: the **parented** branch applies its Euler offset in listener-local space (planet-safe); the **unparented** branch applies the same Euler in world space (assumes global +Y). Use parented mode only.

### 13.2 SurfaceData — front-end welded to physics, back-end perfectly agnostic ✅ inspected

**The answer to the critical question is better than expected.** Its three resolution paths all need a `Collider` — `hit.collider.sharedMaterial`, then a renderer-material scan, then a Unity `Terrain` splatmap fallback that's `[RequireComponent(typeof(Terrain))]`. **All three are dead on arrival for us.**

But everything downstream keys off **nothing but a `Surface` reference**:

```csharp
if (!surface.TryGetModule(out SurfaceFootstepsModule footstepsModule)) return false;
footstepsModule.PlaySound(hit.point, strength);
```

`Surface` never sees a collider, a material or a terrain. **So: replace `TryGetSurface(RaycastHit)` with `TryGetSurface(BiomeId)` — a flat `Surface[17]` resolved at init — and you keep the entire payload architecture while deleting ~250 lines of physics plumbing.**

🔑 **Better still: their splat-weighted terrain path is conceptually exactly our biome-blend case** — a dictionary of surface→weight, playing each above threshold at `strength * weight`. Re-point that at our biome blend weights and **footsteps crossfade across biome borders for free.**

| Take | Part |
|---|---|
| **CORE** | `Surface` as an SO with a **list of composable modules** — already our SO→DTO shape. 17 assets, one per biome. ⚠️ resolve modules at init into **typed fields**, not their `Dictionary<string,…>` keyed on `typeof(T).Name`. |
| **a lot** | The weighted multi-surface playback loop, re-pointed at biome weights. |
| **1 thing** | `GetRandom(out prev, prev)` no-immediate-repeat picker + random pitch 0.9–1.1 + a pooled one-shot. **That trio is what stops footsteps sounding like a machine gun.** |
| **1 thing** | Their demo trigger uses `Physics.SphereCast(pos, 0.1f, -transform.up, …)` — `-transform.up`, not `Vector3.down`. Already sphere-shaped; swap the cast for our analytic query. |

⚠️ **Despite the store name there are no particles and no decals** — all four modules emit only `AudioClip`s. `m_mediumClips` is declared and never used.
⚠️ **Worst rule compliance of the three:** 3 × `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`, 5 singletons, and **unguarded `using UnityEditor;` in five runtime files — which breaks player builds as shipped.** Also pre-Unity-6 (`PhysicMaterial`, `Rigidbody.velocity`).

### 13.3 Real Footsteps — pure clips, and better-matched than expected ✅ inspected

**378 entries, exactly one of which is not audio (`readme.txt`). There is no C# at all** — so there is nothing to compare against SurfaceData. They're complements: **SurfaceData's architecture, Real Footsteps' clips.**

354 WAVs across 22 folders. The three that matter most and that nothing else covers: **Horse (18)** for mounts, **Massive (12)** for the golem we own, **Knight (15)** for armoured NPCs. Plus Forest 19 / Leaves 12 / Snow 31 / Mud 10 / Sand-Gravel 22 / Shallow Water 7 / Puddle 21 — most of a 17-biome spread directly. ~93 clips (Basketball Court, Carpet, High Heels, Dress Shoes, Sneakers) are modern-setting dead weight.

### 13.4 Monster Sounds Pack — the dragon kit is the standout ✅ inspected

680 clips, 33 folders, no code. Mapping onto creatures we already own:

| Owned | Match | Verdict |
|---|---|---|
| **Dragons** | `Large Creature 01–12` (154) + `Wing Flap ×3` (30) + `Breath 1–3` | **Excellent — the standout.** 12 distinct roar identities plus dedicated wing and breath layers is precisely a dragon kit. |
| Synty **goblins** | `Humanoid Small 1–4` (125, incl. 17 `Laugh`) | Excellent — the goblin cackle is exactly the Laugh set |
| **Demons** | `Demon 1–3` (99) | Excellent |
| Synty **golem** | `Humanoid Large 1–4` (80) | Good |
| Wizard / boss VO | `Ancient Language 1–3` (49) | Chanted incantation phrases — **nothing else we own covers this** |
| Synty **skeletons** | `Hissing`, `Growl` | Weak — skeletons want dry bone rattle, absent here |
| Synty **ghosts** | `Ghost` (10) | **Thin — 10 clips is not enough for an enemy type** |
| Polyperfect's 68 species | — | **No match.** Zero naturalistic animal vocalisations. Farm Animal Sounds remains the wildlife source. |

⚠️ **Clips are named by *duration* (`Long`/`Short`), not by game state.** No attack/hurt/death/idle taxonomy — expect to audition and hand-tag all 680. Take ~475; skip Zombie (72) unless undead land.

### 13.5 Sound Shapes — skip, keep two ideas ✅ inspected

**Fatally flat-world.** Containment throws away Y outright: `Vector2 p2D = new(worldPos.x, worldPos.z)` then even-odd polygon test. Emitter height is pinned by averaging `p.y`. Occlusion spread uses `Vector3.Cross(dir, Vector3.up)`. Nothing survives contact with a sphere without rewriting the geometry core, and we have no authored interiors yet.

Two transferable ideas for when Synty dungeon interiors land: (a) the **perimeter-constrained emitter** — an area's source slides along the boundary to the nearest point to the listener, so a river or cave mouth sounds like a *line* source, not a point; (b) **`AudioLowPassFilter` cutoff lerp (22000 → 7000) as the occlusion primitive** — cheap, stock, and the right answer for "player walked into a cave".

### 13.6 Build order — most audio for least code

1. **`SliderRange` + Value/Event gating** (~130 lines) as an `AmbienceDirector` service, fed once per tick from `WeatherManager`, `CelestialManager` and biome blend weight. Paired `AudioSource` A/B crossfade — **not** the PCM mixer.
2. **`Surface` SO + module list** (~200 lines), keyed by biome ID, reusing the weighted-blend loop for biome borders.
3. Clips: Ambient Sounds' beds + thunder + wildlife · Real Footsteps' Forest/Leaves/Mud/Snow/Sand/Water/**Horse/Massive/Knight** · Farm Animal Sounds' 5 beds · Monster Sounds' dragon + goblin + demon + Ancient Language sets.

### 13.7 The rest of the audio library

| Asset | Take | What | Tag |
|---|---|---|---|
| Master Audio: AAA Sound | **maybe** | Big mixing framework; heavier than we need. | 🔎 expected |
| Farm Animal Sounds | **art** | 5 loopable ambience beds (forest, 2× stream, 2× rain) that map onto our biomes and rain-rate tiers, plus 369 one-shots covering 14 Polyperfect species — which ship silent. See catalog §10. | ✅ inspected |
| Fantasy Ambience Sounds Pack | **art** | Fantasy-specific ambience. | 📦 cached |
| Magic Spells Sound Effects / Magic Spells Sounds Bundle / RPG Magic Sound Effect Pack 3 [Elemental] | **art** | Spell SFX for the wizard PC. Elemental categorisation matches a spell-school model. | 📦 cached |
| Medieval Fantasy SFX Bundle / Fantasy Sounds Bundle / RPG & Dungeon Sounds | **art** | General fantasy SFX. | 📦 cached |
| **Action RPG Characters** | **art** | ⚠️ **Misfiled — this is NOT a character pack.** 2.01 GB, **3,944 `.wav`**, zero FBX, zero prefabs, zero code. It's a **voice-over library** — character barks and lines for an action RPG, with voice-actor documentation. Genuinely useful for townsfolk and monsters, and HARVEST-clean by definition since it carries no code. **But audit before importing — 2 GB is a lot of disk for barks.** | ✅ inspected |
| Monster Sounds Pack / Monster·Creature·Hero SFX | **art** | Creature vocalisations. | 🔎 expected |
| Monster (3DMaesen) | **1 thing** | The meshes are a skip (Unity-4-era package, UnityScript `.js` that won't compile, dated realistic art), but it carries **40 decent monster vocal/foley WAVs** worth salvaging. | ✅ inspected |
| Outdoor Atmospheres Sound Effects Pack | **art** | Weather/outdoor beds. | 🔎 expected |
| The Fantasy Music Collection (PRO) / Fantasy RPG Orchestral / Bard's Tales / Medieval Music Pack | **art** | Music. Plenty owned; pick later. | mixed |
| FMOD for Unity | **skip** | Middleware; overkill and it's a runtime dependency. | 🔎 expected |

**Download gate:** Ambient Sounds, SurfaceData, Sound Shapes, Real Footsteps, Monster Sounds Pack.

---

## 14. Performance & streaming 🔎

**Current state.** GPU-indirect scatter (73→37 ms banked), incremental tile-cache gather, quadtree chunk LOD. Known remaining lever: half-res cloud raymarch.

| Asset | Take | What | Tag |
|---|---|---|---|
| SECTR World Streaming for Unity 6 | **a lot** | Sector-based streaming and portal-culling. Our chunk streaming is bespoke; wanted is its **cell graph + visibility** model, especially once buildings and interiors exist. | 🔎 expected |
| Mesh Combine Studio 2 | **1 thing** | Runtime mesh combining by cell. Relevant to **player-built structures**, which will otherwise be thousands of small pieces. | 🔎 expected |
| QuickLod | **maybe** | LOD management; we have our own. | 🔎 expected |
| Clothing Culler | **1 thing** | Hides occluded modular-character parts. Directly relevant — Synty modular characters have 719 sub-meshes. | 🔎 expected |
| SkinnedMesh Combiner | **1 thing** | Combines modular character parts into one skinned mesh. Same problem, complementary answer. | 📦 cached |
| PoolManager / Pool, Trigger, Constrain Bundle | **maybe** | Object pooling for spells and projectiles. Unity has `ObjectPool<T>` now; probably YAGNI. | 🔎 expected |
| Graphy | **1 thing** | See §12. | 🔎 expected |

**Download gate:** SECTR, Mesh Combine Studio 2, Clothing Culler.

---

## 15. Art & content — the Synty sweep ✅ inspected

All 13 packs below are **already in the download cache** and were inventoried without downloading anything.

### 15.0 The two Synty generations — read this before importing any of them

| Generation | Packs | Shader situation |
|---|---|---|
| **Modern (2021+)** | Fantasy Kingdom, Dungeons, Elven Realm, Adventure Pack (120 MB re-release) | **Each bundles its own copy** of `Synty/PolygonGeneric/Shaders/` — `Generic_Basic`, `_Specular`, `_Bloody`, `Triplanar_Basic`, `Generic_Decals`, `SkyDome`, `Skybox_Generic`, `ParticlesLit/Unlit`, and 8 subgraphs including `SnowMask`, `SplitTriplanar`, `DepthFade`. **URP-ready and self-sufficient**; importing several just overwrites the same folder. |
| **Legacy (2017–18)** | Vikings, Pirates, Samurai, Snow Kit, Starter Pack, Adventure Pack (11 MB original) | **Zero shader files.** Built-in materials over a palette atlas. Every material needs conversion or rebinding to `Generic_Basic`. |

**The single most important structural finding for Valheim-style building:** all four modern packs ship the **same shared `SM_Bld_Base_*` modular kit** — Wall / Wall_Half / Wall_Quarter / Wall_Angle / Wall_Door / Wall_Window / Floor / Floor_Round / Ceiling / Roof_Straight + Corner_In/Out + Trim / Stairs / Stair_Half / Pillar / Pillar_Half / Wall_Destroyed. **Identical geometry across packs, so pieces interoperate.** That is your snap-grid vocabulary, and it comes free.

### 15.1 Must-have (4)

| Pack | Size | Serves | The parts that matter |
|---|---:|---|---|
| **POLYGON Fantasy Kingdom** | 266 MB | **Building · crafting · cooking · farming · carts · town · magic props · the wizard PC** | The flagship, and the highest-value single import in the library. Deep house kit (~40 thatch + ~40 tile roof pieces, 9 wall types, chimneys, stairs+railings) on top of the shared base grid. **Crafting stations, best in library:** `SM_Prop_Forge_01–04`, `_Workbench_Alchemy_01`, `_Workbench_Forge_01`, `_Anvil_01–03`, `_Bellows`, `_Grinding_Wheel`, `_Cauldron_01–04`, `_Alchemy_Stand_01–03`, `_Spit_Roaster`, `_Cooking_Block/Rack/Rangehood`, `_Stove`, `_Pizza_Oven`. **The only true ore veins:** `SM_Prop_Ore_Iron_01`, `_Ore_Gold_01`, `_Ore_Gem_01`. **Full cart progression:** `SM_Veh_Wheelbarrow_01` → `SM_Veh_Cart_01–04` → `SM_Veh_TraderWagon_01`. Farming: `_Plow`, `_Scarecrow`, `_PlanterBox`, `_Beehive`, `SM_Env_Ground_Farm_Row_01–05`. **`SM_Chr_Mage_01` is your wizard PC**, with `SM_Chr_Attach_Mage_Cape_01`. **`SM_Wep_Staff_01–10` + `SM_Wep_Sceptre_01–09`.** 31 rigged characters covering blacksmith, merchant, bartender, peasant, priest, king/queen. ⚠️ Its bundled PolygonGeneric drags in ~120 modern props — **delete `PolygonGeneric/Prefabs/{Props,Characters}` after import, keep `Shaders/` and `Models/Base`.** |
| **POLYGON Dungeons** | 157 MB | **Caves · mines · ore dungeons · monsters · the entire rune/portal vocabulary** | Cave kit with `_DoubleSided` variants, `SM_Env_MineTunnel_*`, `SM_Env_Minetrack_*` + `SM_Prop_Minecart_01`, crypt entrances. **Magic, strongest in the sweep:** `SM_Env_Rune_Pillar_01–05`, `_Rune_Rounded_01–05`, `_Rune_Square_01–05`, `SM_Env_Tiles_Rune_01–05`, `_Tiles_Pentagram_01`, `_GlowingOrb_01–05`, plus `FX_Magic_Swirl` / `FX_SkeletonSpawn` / `FX_Spiral` / `FX_Ring`. **Use the runestone + magic-circle prefabs as the portal set (§10).** 16 rigged monsters (goblins ×6, skeletons ×4, rock golem, ghosts ×2, tormented soul) plus a ready goblin-camp kit. Trap set (spikes, pit, saw, swing-blade, flame) for dungeons. Giant mushrooms, stalactites/stalagmites for cave dressing. |
| **POLYGON Vikings** | 20 MB | **Norse village · fishing · boats · the tone-setter** | Tiny and the best thematic fit for a Norse survival loop. **Best fishing content in the library:** `SM_Prop_Fish_Rack_01/02`, `_Fish_Hanging_01/02`, `_Fish_Pile`, `_Fish_Dead_01–03`, `_Net_01/02`, `SM_Wep_FishingSpear_01`. Boats + docks + `SM_Prop_Boat_Build_01` (a boat under construction — a crafting-station in itself). `SM_Prop_Wheel_Barrow_01` + `SM_Prop_Wagon_01`. Blacksmithing (`_Anvil_01`, `_Anvil_Stump_01`, tongs, hammers). Ritual: `SM_Prop_Alter_01`, `_Rock_Totem_01`, `_Rock_Circle_01`. **Every prefab has a `_Snow` twin** (135 of them) — directly useful to the Snow/Tundra biomes. 6 rigged characters × 3 skin tones. ⚠️ Legacy generation: **needs URP material conversion**. |
| **POLYGON Elven Realm** | 291 MB | **The wizard's tower — alchemy, spellbooks, astronomy; plus cloak/hood wearables** | Irreplaceable for a wizard PC. **`SM_Item_Spellbook_01–06` + `_Spellbook_Open_01–06`**, `SM_Item_Elixir_01–07` + holder, `_TestTube_01–03`, `_Quill` + holder, `_Inkwell`, `_Wax_Seal`, `SM_Prop_Distiller_01`, `_Melting_Pot_01`, `_Potion_Stand_01–03`, `_Potion_Tank_01`, `_Magic_Well_01`, `_Constellation_Rings_01`, `_Telescope_01` + bases, `_Sun_Dial_01`, `_Scales_01`. **`FX_Rune_Doorway_01` is a literal portal doorway effect.** Wearables: **`SK_Chr_Attach_Cape_01/02`, `SM_Chr_Attach_Cloth_Cloak_01`, `_Cloth_Hood_01`, `_Cloth_Shoulders_01`** — exactly the wizard silhouette. `SM_Chr_Scholar_Male_01` is a second wizard candidate. Own shaders: `Waterfall` ×2, `Aurora_ElvenRealm`, `RockTriplanar`, `MoonNoFog`. ⚠️ Ornate high-fantasy, not rough Norse — **import Props/Items/Characters selectively; skip the Buildings kit** (Fantasy Kingdom's is the better base). |

### 15.2 Nice-to-have (5)

| Pack | Size | Take | Note |
|---|---:|---|---|
| **POLYGON Adventure Pack** (120 MB modern re-release) | 120 MB | **art** | Village presets + `SM_Prop_Cart_01–03`. Its real value is **~40 trees each with a `_Snow` twin**, plus `_Hill_01–04`/`_HillSnow_01–04`, `_Ice`, `_SnowPile` — **the cleanest paired snow/non-snow vegetation set in the sweep**, directly usable by the 16-biome scatter system. Ships a unique `Ghost_Shader.shadergraph`. **Delete the 11 MB legacy copy — strictly superseded.** |
| **POLYGON Pirates** | 38 MB | **art (selective)** | **Best sailing content by far:** 5 hull classes (Rowing / Small / Medium / Large / Warship) with `_Bare`/`_Upsized`/`_Attachments` variants, `SM_Prop_ShipWheel_01–03`, anchor, oars, rafts, shipwrecks. Docks, cranes, lobster pots, fish racks. The **Shanty building kit is genuinely modular** (base, stilts, deck, rooms, 5 roofs, side attachments). 3 reusable skeleton characters. ⚠️ ~60 gunpowder items — skip. Caribbean palette; take the boats, leave the theme. |
| **Toon Farm Pack** (SICS) | 492 MB | **art (selective)** ⚠️ style | **Deepest agriculture anywhere:** 125 agricultural plants with `_Plant_01A/02A/03A` **growth stages**, `TFP_Orchard_Tree_Stage_1→4`, 363 produce prefabs, modular beehives, 80 hand tools, querns. **The only animated livestock:** horse (11 variants with a dedicated animation folder — a real mount candidate), cow/bull/calf, goat, pig, sheep, chicken, duck, goose, dog, crow. ⚠️ **Outlined toon shader — a visibly different look from Synty's flat atlas.** Either restyle onto `Generic_Basic` or accept a seam. ~130 modern-industrial items (tractors, combines) — skip wholesale. |
| **EXPLORER Stone Age** (SICS) | 321 MB | **art** ⚠️ style | An excellent **tier-0 progression tier** for a Valheim opening: querns (`Stone_Mill_1A/1B/2A`), tanneries, furnaces, hide beds, palisades, modular fences, primitive boats, cave entrances, totems and obelisks. Closer to flat-stylised than the other SICS packs but still a third distinct look. |
| **Toon Adventure Island** (SICS) | 714 MB wrapper | **art** ⚠️ style | ⚠️ **Nested packaging** — the outer package contains two 375 MB inner packages (BuiltIn and URP); import the outer, then the **URP** inner. Best **swimming/fishing** content: animated fish, seagulls, `TAI_Swordfish_01A` with `_Bite`/`_Steak` **loot states**, fishing nets, rafts, underwater plants and rocks. Ships `BoidManager`/`FishManager`/`BirdManager`. Only worth it if a tropical biome is on the cards. |

### 15.3 Skip (4)

| Pack | Why |
|---|---|
| **POLYGON Samurai** (18 MB) | Deep modular building, but an unmistakably East-Asian silhouette that won't sit next to Norse/European Synty. **Cherry-pick only:** `SM_Env_CropRow_01–03`, `SM_Env_FarmTerrace_01–05`, `SM_Env_RicePlant`, `SM_Prop_Vegetables_01–08`, `SM_Prop_StoneBrazier_01–03`, `SM_Wep_BoStaff_01`. |
| **POLYGON Snow Kit** (3 MB) | It's **winter sports**, not survival — skis, snowboards, chairlifts, snowmobile. Salvage `SM_Prop_Sled_01/02`, `_Snowman`, and the beanie/balaclava attachments. |
| **Starter Pack** (5 MB) | A sampler. Every mesh ships at higher fidelity inside the must-haves. Only value is greybox blocks for blocking out the build grid. |
| **Low Poly Ultimate Pack** (135 MB, 10,044 entries) | ~85% off-theme (287 sci-fi, 203 apocalypse, 191 modern furniture, cars, WW2, firearms) and an incompatible flat-sampler art style. Nothing unique enough to justify the consistency hit. |

### 15.4 Content gaps that remain after all 13

1. **No humanoid animation clips.** Every Synty pack ships meshes + rig and **zero** locomotion. Confirms Kevin Iglesias (§3) as the base set.
2. **Ore veins are thin** — Fantasy Kingdom's three `SM_Prop_Ore_*` are the only Synty-native ones. Mining needs more variety, or recolours.
3. **Three cached Synty packs weren't in the sweep and fill real holes:** **POLYGON Horse** (the Synty-native mount, versus Toon Farm's style-mismatched one), **Modular Fantasy Hero Characters** (the customisable PC body — the packs above ship fixed NPC meshes only), and **Knights** (Synty-consistent armour and enemy roster).

### 15.5 Import order for content

1. **Fantasy Kingdom** — then immediately delete `PolygonGeneric/Prefabs/{Props,Characters}`
2. **Dungeons** — caves, runes, monsters
3. **Vikings** — convert materials to `Generic_Basic` first
4. **Elven Realm** — Props/Items/Characters only
5. **Modular Fantasy Hero Characters** + **POLYGON Horse** (from cache, not in the sweep)
6. Adventure Pack's snow-paired vegetation into the scatter system

---

## 16. Wildlife, monsters, NPC AI & navigation ✅ inspected

**Current state.** Nothing. And the hardest constraint in the project lives here: **the world is a sphere with no ground colliders**, which breaks the flat-world assumption baked into almost every AI asset ever shipped.

### 16.1 The headline — A* Pathfinding Project Pro solves the sphere, officially

`Aron Granberg / A* Pathfinding Project Pro` **v5.3.6**, 393 `.cs` / 94,564 lines, **10 asmdefs**, Burst + Collections + Mathematics. Already in the cache.

**Spherical navigation is a shipped, documented feature — not a hack.** Changelog: *"Added support for pathfinding, local avoidance and movement on spherical worlds, and other strange world shapes."* There is a shipped spherical example scene.

| Take | Part | Why it matters |
|---|---|---|
| **CORE** | `Graphs/Utilities/GraphTransform.cs` — `IMovementPlane` (`:16`), **`NativeMovementPlane`** (`:102`), `ToPlane`/`ToWorld` (`:41`,`:79`), `MatchUpDirection` (`:122`) | **Re-implement this first, before anything else in this document.** A quaternion-valued, per-agent tangent plane with `ToPlane(float3)→float2` / `ToWorld(float2, elevation)→float3`. All movement math happens in the local tangent plane, then lifts back. ~150 lines, and it is the correct primitive for **every** spherical gameplay system we will build — AI, character controller, mounts, carts, buoyancy, flight. Highest-value single idea in the entire library. |
| **a lot** | `Graphs/Navmesh/Jobs/JobConnectTiles.cs` (`:15`) + `JobCalculateTriangleConnections.cs` | Per-tile navmeshes stitched by shared edges, parallelised with a **chessboard even/odd colouring** so worker threads never touch the same tile pair. **This maps directly onto our cube-sphere chunk streaming** — per-chunk nav tiles + edge stitching is how you get planet-wide navigation without a global bake. |
| **a lot** | `Core/Pathfinding/HierarchicalGraph.cs` (536 lines, read `:19-38`) | Incrementally maintained connected components → "is B reachable from A" in O(1), and unreachable destinations clamp to the nearest reachable node. Essential when chunks stream in and out. |
| **a lot** | `Utilities/PathTracer.cs` (1,809 lines; doc block `:17-37`) | Incremental path *repair* with an `isStale` test, instead of re-searching every frame. The difference between usable and unusable NPC counts. |
| **a lot** | `Utilities/Funnel.cs` (790 lines) | Simple-stupid-funnel string-pulling over a triangle corridor. Pure geometry, works in any tangent plane. |
| **1 thing** | `Core/ECS/Components/MovementSettings.cs:10-49` — `enum MovementPlaneSource { Graph, NavmeshNormal, Raycast }` | The design answer to "where does *up* come from?", with the docs explicitly naming spherical worlds for the latter two. |
| **1 thing** | `Behaviors/AIPathAlignedToSurface.cs` + `Core/AI/AIBase.cs:596-599` | Surface-relative gravity (`gravity` is a `Vector3`, not `-9.81*up`), plus batched `Mesh.AcquireReadOnlyMeshData` smooth-normal interpolation across all agents. Directly applicable to our analytic grounding. |
| **1 thing** | `Graphs/NavMeshGraph.cs:62-83` — the `recalculateNormals` doc | *"Disable for spherical graphs… make sure the normals in your source mesh are properly set."* This is effectively **a spec for our own chunk mesh generator** if we ever feed it nav data. |
| **1 thing** | Architectural proof | **Zero `[DefaultExecutionOrder]` across 94k lines with a threaded core.** It sequences via an explicit work-item queue. A working existence proof for our own ordering rule. |

**Which graph type works on a sphere:** only **`NavMeshGraph`** (fed from a mesh we supply). `RecastGraph` rasterises within one AABB along one axis — **cannot wrap a sphere**. `GridGraph`/`LayerGridGraph` are single-plane. `PointGraph` is geometry-agnostic but too weak for free roam (fine for a portal/waypoint network).

**Known limitation, quoted** (`Core/AstarPath.cs:2098`): *"For spherical navmeshes… this method will not work as expected, as there's no well defined 'up' direction"* — applies to `IsPointOnNavmesh` and a few convenience queries. Core search, funnel and movement are unaffected.

⚠️ **Why we still can't ship it:** 393 files, real OS threads (`PathProcessor.cs:116-122`), 95 `IEnumerator` / 96 `yield return`, 23 global-namespace files including `AstarPath` itself, and a `com.unity.modules.terrain` dependency. Harvest-only stands. **But both the algorithms *and* the architecture transfer** — which is unusual and worth the reading time.

**Dead weight, named:** `Drawing/` (ALINE, ~7k lines), `Graphs/GridGraph.cs` + `Grid/**` (~7k, single-plane), `Graphs/Navmesh/Voxels/**` + `RecastGraph.cs` + `RecastMeshGatherer.cs` (~5k, can't wrap a sphere, pulls in Unity Terrain), `Editor/`, `TurnBased/`, `Core/Serialization/`.

### 16.2 Behaviour trees — NodeCanvas

`Paradox Notion / NodeCanvas` v3.29, 455 `.cs` / 37,056 lines, 2 asmdefs.

| Take | Part | Why |
|---|---|---|
| **a lot** | `Modules/BehaviourTrees/Nodes/Composites/` (9 files, 725 lines) + `Nodes/Decorators/` (14 files, 754 lines) | The real value: precise, battle-tested semantics for `Selector` (including `dynamic` re-evaluation with correct child-reset bookkeeping, `Selector.cs:26-56`), `Parallel`, `PrioritySelector`, `Interruptor`, `Guard`, `Monitor`, `Timeout`, `Iterator`, `Repeater`, `Optional`. Read all 23 — they encode a decade of edge-case fixes. |
| **1 thing** | `ActionTask.Execute` latch protocol (`ActionTask.cs:59-110`) | How a task calls `EndAction()` from anywhere — including an event callback — without corrupting tree state. **This is the part people get wrong when hand-rolling a BT.** |
| **1 thing** | Its attribute-driven node registry (`[Name]`/`[Category]`/`[Description]`) | Same reflection pattern as our `ConsoleCommandAttribute`. If we build a BT, **reuse our existing registry machinery** rather than importing theirs. |

**Sphere-safe:** yes. `Vector3.up` appears only 3×, and all 30 `NavMesh` references are confined to **8 leaf task files** under `Tasks/Actions/Movement/Pathfinding/`. Delete those 8 and NodeCanvas has zero NavMesh dependency. The BT algorithm is geometry-agnostic and transfers unchanged.

⚠️ **The framework beneath is incompatible:** 3× `RuntimeInitializeOnLoadMethod`, a `DontDestroyOnLoad` auto-spawning `MonoManager` singleton, and a vendored FullSerializer (~3,200 lines of reflective polymorphic JSON) that duplicates what our SO→DTO snapshots already do. Editor code is `#if UNITY_EDITOR`-islanded **inside runtime files**, so you can't cleanly strip it.

### 16.3 Perception — Sensor Toolkit

`Micosmo / Sensor Toolkit` v1.6.7 (the older v1, not the rewritten v2). ~200 lines of ideas, zero files worth copying.

| Take | Part | Why |
|---|---|---|
| **1 thing** | Fractional visibility — `testObjectVisibility` (`BaseVolumeSensor.cs:505-533`) | Casts N rays at points sampled in the target's bounds and returns `nSuccess / testPoints.Count`, gated by `MinimumVisibility`. Gives "the wolf half-sees you through the bushes" and **kills flickering detection**. ~30 lines. Best idea in the package. |
| **1 thing** | `LOSTargets` opt-in aim points (`LOSTargets.cs`, 20 lines) | Designer-placed head/torso/feet transforms override random bounds sampling. |
| **1 thing** | Trigger staleness recovery (`TriggerSensor.cs`, `isColliderStale` + `OnTriggerStay` heartbeat) | Guards the classic "collider disabled outside the volume → no `OnTriggerExit` → ghost detection" bug. **We will hit this when chunk streaming despawns props.** |

⚠️ **Its seam is `Collider`-typed** (`addCollider(Collider)`), so perception is *not* decoupled from Unity `Physics`. Worse, its LOS **fails open** — `Physics.Raycast` finding nothing returns "visible", which on a collider-less planet means terrain never occludes. Re-implement LOS against our analytic terrain query.

### 16.4 Interactor — rejected

`Negen Games / Interactor` v0.96. Inspected specifically as the "look at a thing, press E" candidate. **It doesn't do that.**

Detection is a `SphereCollider` **trigger**, not a look-ray. The only raycast path is **mouse-cursor** (`Interactor.cs:346-352`) and applies to one interaction type. Prioritisation is seven lines — `priority desc, then sqrDistance` (`:372-378`) — with **no view-angle scoring at all**. It ships `[DefaultExecutionOrder(-20)]`, no asmdefs, a flat-XZ grid A* under `InteractorHelpers/Pathfinding/` that is useless on a sphere, and core code that hard-references its own *example* scripts.

**One idea survives:** the shape of `InteractionTypeSettings : ScriptableObject` with a subclass per verb (Touch, Pickable, Climbable, Push, Cover). That maps cleanly onto our SO→DTO doctrine for chop/mine/loot/open. **Steal the shape, not a line of code.** Its genuine strength — IK hand placement so a hand lands on the door handle — is a different subsystem (§3).

### 16.5 Console — Quantum Console

Inspected only for gaps against our own ~193-command console, which is already the same architecture and in places the same vocabulary (`ConsoleCommandAttribute` + `MonoTargetType`, `IConsoleArgumentParser` ≈ `IQcParser`, `ConsoleRegistry.RegisterInstance<T>` ≈ `QuantumRegistry`) — **plus** a script runner and Awaitable support QC lacks. **Do not replace it.** Four gaps worth considering, in value order:

1. **Return-value serializers** — `IQcSerializer` + 13 implementations with recursive delegation, so a command returning `Dictionary<string, Vector3>` renders structured and coloured. Ours returns a flat `string`; commands format their own output.
2. **Yieldable interactive command actions** — `ICommandAction` (`Start`/`Finalize`/`IsFinished`) with `Choice`, `ReadLine`, `WaitUntil`, `Typewriter`. Lets a command prompt mid-execution ("Delete save? [y/N]"). **Our `ConsoleAsyncRunner` + Awaitable is a better substrate than their coroutines** — we'd only need the action vocabulary.
3. **Assembly-exclusion scan rules** — skip whole assemblies during reflection scan. A measurable startup win at 193 commands.
4. **Recursive parser delegate** — `IQcParser.Parse(value, type, recursiveParser)` is what lets it parse `[(1,2,3),(4,5,6)]` into `List<Vector3>`. Ours are flat per-type.

### 16.6 Creature & monster content

| Asset | Take | Note | Tag |
|---|---|---|---|
| POLYGON Dungeons | **art** | 16 rigged monsters — goblins ×6 (incl. shaman, warchief), skeletons ×4, rock golem, ghosts ×2, tormented soul — plus a ready goblin-camp kit. Synty-native, so no style clash. | ✅ inspected |
| Polyperfect Low Poly Animated Animals | **art** | 68 species, 617 clips. **Generic rigs**, and its AI is unusable (hard-coded `Vector3.up`). Take rigs + clips + the 66 `STAT_*` ScriptableObjects. | ✅ inspected |
| Quirky Series Animals ×3 | **maybe** | May be a better Synty style match than Polyperfect. Needs a side-by-side. | 🔎 expected |
| Blaze AI Engine | **⏳ download** | Modern NPC/enemy AI. Judge against the sphere constraint. | 🔎 expected |
| Love/Hate | **maybe** | Faction/relationship modelling for townsfolk. Only once NPCs exist. | 🔎 expected |
| AI Tree / Agents Navigation / Polarith AI / Apex Path / AnyPath / Breadcrumb / RV Smart AI | **skip (probably)** | A* Pro already answers navigation better than any of these, and it's the only one with documented spherical support. | 🔎 expected |

**Download gate:** Blaze AI Engine, Quirky Series Animals ×3, HEROIC FANTASY CREATURES, Monsters Ultimate Pack 02+09, Skeletons Pack, Love/Hate.

---

## 17. Grass, scatter & vegetation tech ✅ inspected

**Current state.** Mature. GPU compute grass blanket with indirect draw + dither LOD + wind; GPU-indirect scatter with LOD tiers and baked billboard impostors; incremental tile-cache gather. So the bar here is high — these packages only earn a place where they do something we *don't*.

They do, in four places.

### 17.1 Vegetation Studio Pro — the highest-value read

228 `.cs`, 5 asmdefs, best-architected of the six. **No renderer feature** — it draws via `Graphics.DrawMeshInstancedIndirect`, so no RenderGraph risk.

| Take | Part | Why we don't have it |
|---|---|---|
| **a lot** | `Compute/Resources/GPUFrustumCulling.compute` — `GetShadowBounds` / `IsShadowVisible` | **Shadow-caster culling.** Casts 4 rays from the object AABB along `_LightDirection`, intersects a ground plane, encapsulates the hits, and frustum-tests the *swept* bounds — so off-screen casters that shadow *into* frame are still drawn, and nothing else is. It's a shader, so it can be adapted almost directly. |
| **a lot** | `BillboardAtlasRenderer.cs` + `AlphaPadding.compute` | ⚠️ **Partly superseded** — commit `e185631` already moved tree impostors to an octahedral angle-grid atlas with camera-facing cell sampling, so the 2-axis frame grid is done. **Three deltas remain:** a **normal atlas** baked alongside albedo via replacement shaders; **`RecalculateMeshNormals` blending leaf normals toward the bounding-sphere normal** before baking (kills the flat-card look at grazing angles); and **alpha dilation** to stop mip bleed at atlas cell edges. `BillboardGenerator` also emits a small mesh with baked normals rather than a single quad. |
| **a lot** | `IVegetationStudioTerrain.cs` + `Terrains/MeshTerrain.cs` (634 L) + `Utility/BVHTree/` | The design template for **"scatter that queries a mesh, not a heightmap."** An LBVH over triangles answers height/normal/slope via Burst-jobbed raycasts. Its `SampleTerrain(spawnRect, …)` maps onto our cube-sphere chunks almost 1:1 if you swap `Rect` for a face-local UV rect. |
| **1 thing** | `PredictiveCellLoader.cs` | Spawns cells **ahead of the camera velocity vector**, not just by radius. We don't do this, and it's the cheapest fix for pop-in when moving fast. |
| **1 thing** | `lodFadeQuantified = 1 - clamp(round(lodFade*16)/16, 0.0625, 1)` | 16 fixed fade steps instead of continuous — makes cross-fade **stable under TAA**. |
| **1 thing** | `_FloatingOriginOffset` applied in-shader | Relevant at planet scale if we ever shift origin. |

❌ Dead weight: the whole `ShaderSystem/*` vendor-adapter tree, `External/Clipper` (4,905 lines), `PersistentStorage`, a 3,331-line inspector, `TouchReactSystemPro`. ⚠️ 41 raw-string `Shader.SetGlobal*` calls — a `ShaderGlobalIds` conflict if ever imported.

### 17.2 GrassFlow 2 — harvest the compute, ignore the C#

2 asmdefs, clean packaging. **No renderer feature** (hooks `RenderPipelineManager.beginCameraRendering`), so no RG risk. URP variants are *generated at import* from a template that carries both Built-in and URP `LightMode` tags.

| Take | Part | Why |
|---|---|---|
| **a lot** | `GrassFlowCompute.compute` — `RemoveGrassFromBuffer` (`:581`) | **In-place buffer compaction** via `InterlockedAdd(countBuff[chunkID], 1, start)`. This is the direct answer to **Valheim-style building clearing grass under structures** — a feature we will need and have no plan for. |
| **a lot** | `FillMeshPosBuffer` (`:327-386`) | Barycentric area-correct scatter over an arbitrary mesh (`sqrtR` weighting), with **randomised triangle pick** rather than `idx % triCount` — the comment notes the modulo version makes cull-section ordering "too consistent". Slope reject is `dot(norm, float3(0,1,0))`, a one-line fix for a sphere. |
| **a lot** | `AddRipple` / `UpdateRipples` kernels | Interaction as a self-compacting **ring buffer** of 128 ripples (`pos.w` = strength, decay/radius/sharpness/speed). Our grass interactor hook is currently inert — this is a ready design. |
| **1 thing** | Stride-based LOD + `fracFade` | LOD by **buffer stride**, not dither: `ogInstID = chunkInstIdx * chunkInvLodStepMult`, with the last blade **fractionally faded** so density changes continuously instead of popping. A different, cheaper axis that could stack with our dither LOD. |
| **1 thing** | `grassPerTri` grouping | `if(id % grassPerTri == 0) InterlockedAdd(indirectArgs[1], 1)` — one indirect instance drives N blades, cutting atomic pressure N×. |
| **1 thing** | `GrassPosCompressed` packing | 36 B/instance (20 B in the `NOBAKE` variant) via `f32tof16` normals/UV and 4×8-bit params. |
| **1 thing** | `EmptyChunkDetect` kernel | Pre-pass marking zero-density chunks so they're never dispatched. |

⚠️ **Worst rule compliance of the six:** `async void` ×9, `Task.Run` ×11, 2× `RuntimeInitializeOnLoadMethod`. The C# is unusable; the compute is excellent.

### 17.3 Gaia 2 — one idea, zero code

**Totally Terrain-bound** (`Spawner.cs` alone references `Terrain` 189 times). Nothing executable transfers. But its **mask-stack model** is the right shape for biome-driven scatter:

An **ordered list of typed masks** composited to a single 0–1 weight texture, each with `invert` / `strength` / an `AnimationCurve` remap and an explicit **per-entry blend mode**:

```
ImageMaskOperation { ImageMask, DistanceMask, HeightMask, SlopeMask, NoiseMask,
                     CollisionMask, StrengthTransform, HydraulicErosion, Smooth,
                     TerrainTexture, ConcaveConvex, WorldBiomeMask }
ImageMaskBlendMode { Multiply, GreaterThan, SmallerThan, Add, Subtract }
ImageMaskInfluence { Local, Global }
```

Two subtleties worth copying: the **`Local` vs `Global` influence flag**, and **`HeightMaskType.Relative`** — a percentile within the local terrain rather than absolute world units. **`Relative` is exactly right on a planet where "high" means something different per biome.** Evaluation is GPU blits, one 30-line shader per operation (`FilterHeightMask`, `FilterSlopeMask`, `FilterDistanceMask`…) — five minutes to read.

Secondary: `m_areaAvgSlopeWU` (slope averaged over the object **footprint**, not the sample point) and `m_virginTerrain` (occupancy reject) from `SpawnCriteria.cs`.

### 17.4 Brute Force Grass — two formulas

| Take | Part |
|---|---|
| **1 thing** | The gust envelope: `(sin(t + n*5) + sin(0.5t + 1.051))/5 + 0.15n` — two incommensurate sines (periods 1 : 2) give a long non-repeating beat. One line, better-looking than a single sine, drops straight into our grass compute wind. |
| **1 thing** | `_GlobalEffectRT` ortho-RT trample encoding (`r` = shadow, `b` = height/trample). A cheaper alternative to GrassFlow's ripple buffer — but it only maps onto a sphere if the ortho camera is re-projected per player-local tangent plane. |

❌ Everything else: **geometry-shader blades** (deprecated in Unity 6 URP, non-starter on Metal, and a regression against our indirect compute), 4×-duplicated 1,400-line shaders, a vendored TAA and Bloom, 26 global-namespace types.

### 17.5 Polaris (in Low Poly Tools Bundle) — three mesh ideas

| Take | Part | Why |
|---|---|---|
| **1 thing** | `GStitchSeamLODJob.cs` | LOD-boundary seam stitching — the same problem our quadtree chunk edges have. |
| **1 thing** | `GCreateVertexJob.cs:178-207` | Per-vertex **flat↔smooth normal blend from a mask** (`Vector3.Lerp(normal, smoothNormal, mask).normalized`). |
| **1 thing** | `GCreateVertexJob.cs:245` | Deterministic per-vertex jitter from `Random.CreateFromIndex(seed ^ uv hash)` for the faceted look. |

Also has adaptive triangle subdivision via a subdiv-mask tree rather than uniform grids. ⚠️ Its URP support is a **nested `URP_Support.unitypackage`** not extracted; the on-disk shaders are Built-in surface shaders.

### 17.6 RenderGraph audit — the silent-failure check

| Feature | Verdict on 6000.6 |
|---|---|
| Oceanis `BlitPassSunShaftsWaterSRP`, `CustomPostProcessingPassOCEANIS` | ✅ real `RecordRenderGraph` |
| Oceanis `VolumetricLightScattering` | ❌ `Execute`-only — **silently dead** |
| Poseidon `PWaterEffectRendererFeature` | ❌ `Execute`-only — **silently dead** |
| GrassFlow · Brute Force · VSP · Gaia | n/a — no renderer features; the indirect-draw ones are unaffected |

**That's three assets now** (Staggart grass, Oceanis volumetrics, Poseidon water FX) that import cleanly and silently do nothing. Treat "has a `ScriptableRendererFeature`" as a red flag requiring a `RecordRenderGraph` check every time.

---

## 18. Towns, NPCs, dialogue & quests 🔎

**Current state.** Nothing. Needed for "friendly NPCs / townsfolk / towns".

> **Both "priority" packages inverted on inspection.** The two *small* packages — Conversa (2.7k LOC) and Love/Hate (~3k) — are the ones worth building on. Dialogue System for Unity is 90k LOC of Chat Mapper heritage, and **Blaze AI is a write-off** (§18.1).

### 18.1 Conversa — **the dialogue base** ✅ inspected

71 files / **2,686 LOC**, own asmdef with `"references": []`. **Zero MonoBehaviours, zero coroutines, zero `RuntimeInitializeOnLoadMethod`, zero `[DefaultExecutionOrder]`, zero singletons, zero `async void`.** The only package in this entire survey that would pass our rules almost as-is.

```csharp
public class Conversation : ScriptableObject {
    [SerializeField]     private BookmarkNode startNode;
    [SerializeReference] private List<INode>        nodes;
    [SerializeReference] private List<EdgeData>     edges;      // flat, not embedded in nodes
    [SerializeReference] private List<BaseProperty> properties; // typed, conversation-scoped
}
```

🔑 **The key architectural idea: two node kinds.** `IEventNode` is push-based control flow; `IValueNode` is pull-based dataflow. **So a condition is a *subgraph*, not an embedded scripting language:**

```csharp
var condition = conversation.GetConnectedValueTo<bool>(this, "condition");
var nextNode  = conversation.GetOppositeNodes(GetNodePort(condition ? "true" : "false")).FirstOrDefault();
```

Supporting value nodes — `And/Or/Not/Xor`, `Add/Subtract/Multiply/Divide`, `Compare/GreaterThan/LessThan`, `RandomFloat`, `Parse`, property get/set. **Zero Lua, zero reflection-based expression parsing.** That composes with our SO→DTO doctrine far better than a string DSL.

Also worth adopting: **GUID node identity** (stable across merges, unlike int indices), **flat edge list** with typed ports declared by attribute (`[Port("Next", "next", typeof(BaseNode), Flow.Out, Capacity.One)]`), and a **plain-C# runner** emitting a typed event stream — `CurrentNodeGuid` + property snapshot *is* the entire save payload.

⚠️ One wart: `Runtime/Utils/Sanitizer.cs` is *declared* in `namespace Conversa.Editor.Utils` despite living in the runtime asmdef. A mislabel, not a real dependency — rename it.

### 18.2 Dialogue System for Unity — take four semantics, discard 90k lines ✅ inspected

**1,084 `.cs` files, zero asmdefs**, 69 files using coroutines, 34 `RuntimeInitializeOnLoadMethod`, a static-facade singleton. And the fatal one: **the save format *is* Lua source code** — `ApplySaveData` literally calls `Lua.Run(saveData)`. 146 files / 5,788 LOC of hand-written Lua interpreter.

Its schema is also stringly-typed throughout — every asset is a bag of `Field {title, value, type}`, so `entry.ActorID` is `Field.LookupInt(fields, "Actor")`. That exists to round-trip Chat Mapper XML. **We should not copy it.**

**But four semantics are worth porting into Conversa's shape:**

1. 🔑 **`ConditionPriority` link ordering + `falseConditionAction` = Block | Passthrough** (`DialogueEntry.cs:66,72`). **The cleverest thing in the package, and Conversa has no equivalent.** You need it the moment an NPC has six conditional greetings.
2. **SimStatus** — per-node `WasDisplayed` / `WasOffered`, driving "[already asked]" dimming and once-only lines. Store as a typed `HashSet<Guid>` pair, **not** their packed Lua string.
3. **`IDialogueUI`** — an 8-method interface with `ShowResponses(subtitle, responses, timeout)`. Copy near-verbatim; it's the right boundary. Plus `hasPCAutoResponse` (single unforced response ⇒ skip the menu).
4. **`QuestState` `[Flags]` enum** — `Unassigned/Active/Success/Failure/Abandoned/Grantable/ReturnToNPC/Done` as flags so conditions test `Active|Success` in one mask. Copy verbatim, but as a **separate typed quest system**, not stored in the dialogue database. Their `QuestLog.cs` is 1,447 LOC of Lua string plumbing over ~10 real operations.

❌ Discard: the `Field` bag and `Template.cs`, all of `Lua/`, `PersistentDataManager`, `QuestLog`, all of `UI/`, all 10 importers, the 7,302-LOC sequencer DSL, and the ~200-file `Wrappers/` mirror tree (duplicate empty subclasses that exist purely to relocate namespaces).

### 18.3 Love/Hate — **highest signal-to-noise of the four; lift the model wholesale** ✅ inspected

The entire data model — ~800 LOC across `Faction`, `Relationship`, `TraitDefinition`, `Deed`, `Rumor`, `Pad`, `Temperament` — contains **no MonoBehaviour and no ScriptableObject**. It is pure serialisable C#.

| Take | Part | Why |
|---|---|---|
| **CORE** | **Faction DAG, not a matrix** | `Faction { int[] parents; float[] traits; List<Relationship>; float percentJudgeParents; }` — inheritance plus *"how much do I judge you by your faction vs. you personally"*. |
| **CORE** | **The Deed / Rumor split** | `Deed` is what objectively happened; `Rumor` is a *witness's memory of it*, adding `count`, `confidence`, PAD emotion, `memorable`, and separate short/long-term expiries. **Separating objective event from subjective, decaying, second-hand memory is the design's best idea.** |
| **a lot** | **Two-tier memory with magnitude-scaled expiry** | `shortTermExpiration = now + (changeMagnitude * shortTermMemoryDuration)` — **big events are remembered proportionally longer**, for free. Short-term feeds PAD emotion; long-term is what gets gossiped. |
| **a lot** | `acclimatizationCurve` | Diminishing returns on repeated identical deeds — an `AnimationCurve` indexed by `rumor.count`. |
| **1 thing** | `impressionability` | Witnessing deeds by people you *like* slowly shifts your own personality traits. Cheap emergent characterisation. |
| **1 thing** | PAD emotion → `Temperament` octant mapping, and `Traits.Alignment(a, b)` vector similarity |
| **1 thing** | Its runtime is **already delegate-injected** — `CanSee`, `ShareRumor`, `EvaluateRumor`, `GetTrustInSource`, `GetPowerLevel` are public delegate fields, so every policy is overridable without subclassing. Good pattern to copy. |

Compliance is mild: 3 coroutines (memory-cleanup tick → `Awaitable`), 4 `RuntimeInitializeOnLoadMethod`, and **JSON saves, not Lua**. The only `Vector3.up` is in `FieldOfView`/`CanSeeAdvanced`, which we'd replace with a tangent-plane version anyway.

### 18.4 Blaze AI — **write-off on a sphere** ✅ inspected

Not because of NavMesh alone. **The disqualifying line is `BlazeAI.cs:1113-1121`:**

```csharp
Quaternion lookRotation = Quaternion.LookRotation(rotationVector);
lookRotation = new Quaternion(0f, lookRotation.y, 0f, lookRotation.w);   // zeroing x,z
transform.rotation = Quaternion.Slerp(new Quaternion(0f, transform.rotation.y, 0f, transform.rotation.w), …);
```

That isn't "LookRotation without an up vector" — it's an **explicit assertion that the character's up axis is world +Y**. Every AI would lie on its side away from the north pole. Add 25 `Vector3.up|down` hits, 37 `position.y` idioms, world-Y vision gates, and jump arcs interpolating on world Y.

**And NavMesh is welded, not pluggable.** `[RequireComponent(typeof(NavMeshAgent))]`; locomotion *is* `navmeshAgent.Move(...)`; and — worse — **NavMeshAgent geometry is used as a general-purpose tuning constant throughout the decision layer** (`blaze.navmeshAgent.height * 2`, `.radius`), so even the behaviours are NavMesh-dependent. The overridable methods are the wrong half: `GoToCorner`, `GetNextCorner`, `MovementRotate`, `ComputeAvoidanceOffset` are all **private non-virtual**. There is no `ISteering`/`IMover` boundary anywhere — behaviours call `blaze.MoveTo(...)` directly.

Also: 19.5k LOC in one 4,079-line god MonoBehaviour, a **hard-coded closed state enum**, ~150 inspector fields, 56 coroutine sites, no asmdefs, 13 files in the global namespace.

**Six algorithms worth reading and re-implementing:**

| What | Where |
|---|---|
| **Vision meter** — distance-bucketed gradual detection with decay, and "partial detection triggers investigate" | `BlazeAI.cs:2107-2200`, `Vision.cs:141-155` |
| **Multi-ray detection score** — ray at *each* collider of the target, require N hits. Fixes "only the head is exposed" | `BlazeAI.cs:1740-1788` |
| Frame-skipped, **per-agent-jittered** sensing | `BlazeAI.cs:414`, `:1282` |
| Local avoidance side-step — only consider agents in front, pick side by relative position, cool-down when stuck | `BlazeAI.cs:919-1010` (swap `Vector3.up` → surface normal) |
| Corner-queue path follower shape — the template for consuming A* Pro corners | `BlazeAI.cs:680-828` |
| Cover selection — the best-designed part; only if ranged enemies happen | `BlazeAICoverManager.cs` |

✅ One genuinely good property: **its LOS fails *closed*** (`return false` when nothing is hit), which is correct-by-default on a collider-less planet — the opposite of Sensor Toolkit's fail-open.

### 18.5 The rest

| Asset | Take | Note | Tag |
|---|---|---|---|
| NodeCanvas Dialogue module | **skip** | We take only its BT nodes (§16.2). | ✅ inspected |
| Storyteller / Intrigues | **maybe** | Quest/narrative structure. Later. | 🔎 expected |
| SALSA LipSync | **skip** | Synty characters have no facial rig. | 🔎 expected |
| Merchants Enemies And Townsfolk | **art** | NPC roster. | 🔎 expected |
| POLYGON Fantasy Kingdom | **art** | 31 rigged NPCs — blacksmith, merchant, bartender, peasant M/F, priest, king/queen — plus market stalls, quest board, signage, well, street lamps. **The town layer is already covered.** | ✅ inspected |
| Polygonmaker Regular / Interactions | **art** | `talk`, `salute`, `surrender`, `taunt`, `decline`, `pointing_*`, `dance1`, `laugh`. | ✅ inspected |
| Conversation Gestures Pack | **art** | 42 Humanoid speaker/listener gesture clips, zero C#, no material debt. Best signal-to-noise of the social packs. | ✅ inspected |
| Vendors_and_Customers | **maybe** | Shop/tavern NPC work clips with prop-synced animation. Only once shops exist. | ✅ inspected |

**Download gate:** Dialogue System for Unity, Love/Hate, Conversa, Merchants Enemies And Townsfolk.

---

## 19. Inventory, items & UI 🔎

**Current state.** No game UI at all (debug console + F6 HUD only).

| Asset | Take | What | Tag |
|---|---|---|---|
| Survival Engine / Ultimate Crafting System | **CORE** | The item/recipe data model — see §5. Inventory is inseparable from crafting; decide both at once. | 🔎 expected |
| Inventory Pro | **maybe** | Older. Compare schemas only. | 🔎 expected |
| GUI Pro — Fantasy RPG · GUI Pro — Survival Clean | **art** | Two directly on-theme UI kits. Survival Clean is the closer match to a Valheim-ish HUD. | 🔎 expected |
| Medieval Kingdom UI · Necromancer GUI · GUI — The Stone | **art** | More fantasy UI skins; pick one family and commit. | 🔎 expected |
| 6200 Fantasy RPG Icons · 6000 Fantasy Icons · 5000 Fantasy Icons | **art** | Item/spell icons. **Three overlapping sets — pick one**, likely the 6200. A spell-heavy game needs a lot of icons. | 🔎 expected |
| 400 Low Poly RPG Weapons · Medieval Weapons | **art** | Includes **staves and wands** for the wizard. Overlaps Fantasy Kingdom's 10 staves + 9 sceptres. | 🔎 expected |
| Text Animator for Unity | **maybe** | Animated dialogue text. Nice polish, not near-term. | 🔎 expected |
| DoozyUI / Better UI / Lean GUI / NGUI | **skip** | UI frameworks; Unity's UI Toolkit is the modern answer and we have no UI debt yet. | 🔎 expected |

**Download gate:** GUI Pro — Fantasy RPG, GUI Pro — Survival Clean, 6200 Fantasy RPG Icons, 400 Low Poly RPG Weapons.

---

## 20. Save & persistence 🔎

**Current state.** Only `SurfaceEditStamp` (versioned, seed-keyed) plus a camera store. No character, inventory, or world state.

**The important observation:** `SurfaceEditStamp` is already the right shape — a **replayable list of world edits keyed by seed**, with derived caches (path wear, scorch) rebuilt from it. Harvested trees, mined ore, placed buildings and tilled soil are all the same shape of problem. **Generalise that spine rather than adopting a save framework.**

| Asset | Take | What | Tag |
|---|---|---|---|
| Odin Serializer | **maybe** | Polymorphic serialisation without reflection-JSON pain. ⚠️ It's **runtime** code, which the harvest-only rule bars — see §M1. | 📦 cached |
| Survival Engine | **1 thing** | How it persists buildables, crops and container contents. Read the schema. | 🔎 expected |
| NodeCanvas FullSerializer | **skip** | ~3,200 lines of reflective polymorphic JSON. Our SO→DTO snapshots already solve this. | ✅ inspected |

---

## 21. Terrain, biomes & procgen 🔎

**Current state.** Mature and bespoke — cube-sphere quadtree chunks, 17 biomes, interlock blend. Nothing here replaces any of it.

| Asset | Take | What | Tag |
|---|---|---|---|
| Gaia 2 | **1 thing** | The **typed mask stack** for biome scatter — see §17.3. One idea, zero code. | ✅ inspected |
| Polaris | **1 thing** | LOD-seam stitching + flat/smooth normal mask blend — see §17.5. | ✅ inspected |
| MapMagic 2 | **maybe** | Node-graph procedural terrain. Its **graph-of-generators authoring model** might inform how biome rules are authored, but it's Terrain-bound. | 🔎 expected |
| Orbis — DOTS Terrains | **maybe** | DOTS terrain; likely nothing transferable. | 🔎 expected |
| Titan Rock Generator / Stylized Rocks / Realistic Cliffs | **art** | Rock variety. **Pick one family** — see §M2. | 🔎 expected |
| Game Buffs texture megapacks (350+/110+/90+/80+/70+) | **art** | Realistic PBR ground textures. ⚠️ **Style clash** with Synty-flat, and we already have biome texture arrays. Likely skip entirely. | 📦 cached |

---

## 22. Digging, caves & voxel

Covered in §9.

---

## M. Maybe — unresolved, needs your call or more evidence

Grouped by *why* it's unresolved, because the resolution differs.

### M1. Rule questions — ✅ **RESOLVED 2026-08-11**

**Bryan's ruling: editor-only tools get an in-repo exception.** The harvest-only rule stands for **runtime** C#; editor-assembly tools that never ship in a build may live in the repo.

| Item | Disposition |
|---|---|
| **Odin Inspector** | ✅ **Allowed in-repo** (editor half). ⚠️ Odin is famously *both* — keep the **serializer** out; it's runtime code and the rule still bars it. Import under an editor asmdef and use it for the settings-SO authoring surfaces. |
| **Hot Reload** | ✅ **Allowed in-repo.** Pick this over Fast Script Reload — they overlap and Hot Reload is the maintained one. |
| **Better Animation Events** | ✅ **Allowed in-repo.** Both its asmdefs are `includePlatforms: ["Editor"]`, so it's clean. Events are authored per-clip and travel with the clip, so it needs to be where the clips are — the scratch-project-only rule genuinely didn't work here. |
| **vHierarchy / vInspector / vFolders / vTabs / vFavorites** | ✅ **Allowed in-repo.** Editor-only, low stakes. |
| **ALINE** | ✅ **Allowed in-repo.** Fast immediate-mode debug drawing; we do a lot of gizmo-based diagnosis. |
| **Shapes (Freya Holmér)** | ✅ **Resolved — and it's a non-issue. Do not import.** `Assets/Plugins/Shapes` does not exist, **nothing in the project references Shapes** (`using Shapes` → zero matches), and `ShapesEditor/Runtime/Samples.csproj` are dated 04-25 — leftovers from a brief import months ago. **33 csproj files reference source that no longer exists**, including `Assembly-CSharp.csproj` (8/8 refs missing) and stale ones for GrassFlow, Stylized Grass, Quantum Console and AssetInventory. `*.csproj` is **gitignored and untracked**. **Fix: delete all `*.csproj` and let Unity regenerate.** CLAUDE.md's "fails on missing Shapes files" is a stale-artifact problem misdiagnosed as a missing dependency — **correct that note.** |

### M2. Style-match calls that need your eyes

| Item | The question |
|---|---|
| **Quirky Series Animals Mega Pack ×3** | Possibly a *better* Synty match than Polyperfect (which we already own and which has broken AI). If so it partly supersedes a 679 MB pack. Needs a side-by-side. |
| **KayKit Adventurers / Dungeon** | Low-poly but a distinctly different flavour from Synty. Charming, possibly clashing. |
| **Polyart Dreamscape** | Painterly/PBR-stylised, not Synty-flat. Biggest style-clash risk in the library, and also the most technically interesting art pack. |
| **Corals** | 2K PBR underwater vs. flat-shaded land. May be acceptable *because* it's below the waterline. |
| **Toon Fantasy Nature / Toon Enchanted Meadow** | Toon-outlined and more saturated than Synty. |
| **Realistic Cliffs and Rocks / Titan Rock Generator / Stylized Rocks** | Three rock sources of differing realism; pick one family and stay consistent. |

### M3. Probably-redundant with what we already built

| Item | The question |
|---|---|
| **GrassFlow 2 / Brute Force Grass / Vegetation Studio Pro** | We have a mature GPU compute grass blanket and a GPU-indirect scatter system. These may have nothing to teach — or may have exactly one idea each. Agent inspection in flight; if it returns "nothing new", they drop to skip. |
| **Gaia 2 / MapMagic 2 / Polaris / Orbis** | All Unity-Terrain-bound. Only the **spawner rule model** (noise/slope/height/curvature filters) might transfer to biome-driven scatter. |
| **Quantum Console / Editor Console Pro / Smart Console** | We have ~193 commands across 13 prefixes. Almost certainly skip; reading only for missing affordances. |
| **PoolManager / Pool Trigger Constrain** | Unity has `ObjectPool<T>` now. Likely YAGNI. |
| **QuickLod** | We have our own LOD tiers. |

### M4. Depends on a design decision not yet made

| Item | Blocked on |
|---|---|
| **DestroyIt / RayFire / Mesh Slicer / Impact Deformable** | Whether felled trees and mined rocks are *fractured* or *swapped to pre-broken prefabs*. If swapped (likely, and style-correct), this whole cluster drops to skip. |
| **Easy Grid Builder Pro** | Whether building is grid-snapped or free-placement. Valheim is free-placement with snap-points, which would make a grid system the wrong model. |
| **Wheel Controller 3D / Obi Rope / Filo** | Whether carts get real wheel physics or are kinematic followers. A hand-pulled wheelbarrow almost certainly doesn't need suspension. |
| **A\* Pathfinding Pro / Agents Navigation / Polarith AI** | Whether NPC navigation is graph-based at all on a sphere, or whether we do local steering + surface-following instead. Agent inspection may settle it. |
| **RPG Builder / UMA for RPG Builder** | Whether we ever want a generic RPG framework's stat/class model, or keep bespoke DTOs. Strong prior: bespoke. |
| **Motion Matching System** | Needs a large mocap corpus. Only relevant if locomotion quality becomes a headline goal. |
| **FMOD / Master Audio** | Whether audio needs middleware at all. Prior: no. |

### M5. Interesting but I can't place them yet

| Item | Note |
|---|---|
| **Space Graphics Planets** | Planet rendering from the *space* side. We render from the surface. Might matter if the game ever shows the planet from orbit — which a teleport/portal system arguably could. |
| **Gravity Engine** | N-body gravity simulation. Almost certainly the wrong scope, but the name is too on-the-nose to dismiss unread. |
| **Horizon[ON]** | Unclear from the title; possibly horizon/curvature rendering, which would be squarely relevant. |
| **Dreamteck Splines / Spline Mesh Deform** | Roads, rivers, fences, and cart paths on a curved surface all want splines. No spline system exists in the project. |
| **Archimatix Pro** | Parametric modelling. Could generate building kits procedurally rather than importing them. |
| **Boing Kit** | Bouncy secondary motion for grass and props — cheap "aliveness", and our grass interactor hook is currently inert. |
| **Time Rewind** | Rewind mechanic. Not in your stated design, but a wizard game is exactly where it'd fit. |
| **Love/Hate** | NPC faction/relationship modelling. Fits "good guys / townsfolk" but only once NPCs exist. |
| **Slate Cinematic Sequencer / uSequencer** | Cutscenes. Not near-term. |
| **Feel (MMFeedbacks)** | Game-juice framework — screen shake, hit stop, flashes. Harvest-only makes this a *pattern* to copy rather than import, and the pattern is genuinely good. |

---

## D. Download list

Everything below is **owned but not on this machine**. Download into the scratch project (`D:\Unity\Explore Assets`) and I'll inspect and rule on each. Ordered so that stopping partway still leaves you with the most valuable set.

⚠️ **Do not import any of these into ProceduralPlanets.** They go in the scratch project so I can read the code. Only harvested reimplementations and explicitly-approved art cross over.

> **The list shrank as inspection went on.** Three Tier-1 entries were demoted once cached packages turned out to already solve them — **Stylized Water 2** (Poseidon is cached, style-matched and better), **Final IK** (iStep is cached and already sphere-ready), and the whole animation category (Kevin Iglesias + Stylish Archer + ARPG Samurai + 11 Polygonmaker packs are all cached). **The 63 GB download cache is carrying far more of this project than the catalog suggested.** Check the cache before buying time on a download.

### Tier 1 — biggest gaps between the project and the game you described (18)

The survival/crafting/building pillar has **zero** code today and is the largest single gap.

| # | Asset | For | Why it's tier 1 |
|---|---|---|---|
| 1 | **Survival Engine — Crafting, Building, Farming** | §5 | Closest thing in the library to the target game. Item/recipe/buildable data model + placement/snapping. |
| 2 | **Ultimate Crafting System** | §5 | Second opinion on the recipe/inventory schema. |
| 3 | **RPG Farming Kit** | §5 | Crop growth stages, tilling, watering, seasons. |
| 4 | **Dynamic Portals** | §10 | Portals are your fast-travel system; the traversal maths is the hard part. |
| 5 | **Fluid Seamless Portals — Basic** | §10 | Comparand for #4. |
| 6 | ~~Final IK~~ → **demoted to Tier 4** | §3 | **Superseded.** iStep is cached, inspected, and its foot-IK math is **already sphere-ready** (every cast takes the up-axis as a parameter). Download only if iStep's solver falls short. |
| 7 | **Dynamic Water Physics 2** | §7 | Submerged-volume buoyancy — what boats need. |
| 8 | ~~Stylized Water 2~~ → **demoted to Tier 4** | §7 | **Superseded.** Poseidon (cached, inspected, flat-shaded URP, Bézier crest waves, noise-clipped shoreline foam) already fills this slot and matches the Synty target better. Download only if Poseidon falls short. |
| 9 | **Altos — Volumetric Clouds & Weather (URP)** | §8 | Its downsample/upsample scheme is our #3 perf item. |
| 10 | **UniStorm** | §8 | Weather-state transition model vs. our weather grid. |
| 11 | **Ambient Sounds (Unity 6)** | §13 | Audio is the emptiest subsystem; biome+weather-driven ambience is our exact shape. |
| 12 | **SurfaceData: Effects System** | §6/§13 | Surface-type → footstep/impact FX. Our biome map already knows the surface. |
| 13 | **Blaze AI Engine** | §18 | Modern NPC/enemy AI; needed for monsters and townsfolk. |
| 14 | **Dialogue System for Unity** | §19 | Towns and NPCs need dialogue; this is the mature one. |
| 15 | **Voxelica — Voxel Engine** | §9 | Only owned voxel engine; digging/caves is a named goal. |
| 16 | **The Vegetation Engine** (base) | §12 | Unblocks the Polygonal Shaders module already in the scratch folder. |
| 17 | **Skill & Attack Indicators** | §4 | AoE telegraphs — readable spell combat needs these. |
| 18 | **Crates & Barrels — Stylized Destructible Props** | §6 | The cheap, style-correct answer to "chop a tree". |

### Tier 2 — subsystem comparanda and second opinions (20)

| # | Asset | For |
|---|---|---|
| 19 | Character Controller Pro | §1 — third grounding opinion |
| 20 | Character Movement Fundamentals | §1 |
| 21 | Easy Character Movement 2 | §1 |
| 22 | Magica Cloth | §3 — wizard robes/capes |
| 23 | Ragdoll Animator 2 | §3 — death/knockback |
| 24 | Tail Animator + Look Animator + Legs Animator | §3 — creature secondary motion |
| 25 | UMotion Pro | §3 — author our own spell/cart clips |
| 26 | Magic Arsenal | §4 |
| 27 | Combat Magic Spells Bundle | §4 |
| 28 | Epic Toon VFX 2 | §4 — best style match |
| 29 | Stylized VFX Bundle | §4 |
| 30 | All In 1 Vfx Toolkit | §4 — shader toolkit |
| 31 | Highlight Plus | §4 — interaction highlighting |
| 32 | Easy Grid Builder Pro | §5 |
| 33 | Inventory Pro | §5 |
| 34 | FS Swimming System | §7 |
| 35 | Weatherade: Snow and Rain System | §8 — surface snow/wetness accumulation |
| 36 | Snowify + Brute Force Snow & Ice Shader | §8 — Snow/Tundra biomes are our thinnest |
| 37 | SECTR World Streaming for Unity 6 | §14 |
| 38 | Mesh Combine Studio 2 + Clothing Culler | §14 — player-built structures, modular characters |

### Tier 3 — art and content (16)

| # | Asset | For |
|---|---|---|
| 39 | POLYGON Farm Pack | §5 — farming, barns, crops |
| 40 | POLYGON Construction Pack | §5 — building materials |
| 41 | POLYGON Western Frontier Pack | §5 — carts, wagons, frontier building |
| 42 | KayKit Adventurers + Dungeon Remastered | §22 — low-poly, style-adjacent |
| 43 | Quirky Series Animals Mega Pack Vol 1–3 | §18 — cute low-poly wildlife; likely a better Synty match than Polyperfect |
| 44 | Merchants Enemies And Townsfolk | §19 — NPC roster |
| 45 | Stylized Fantasy Dragons Pack | §11 — flying mounts |
| 46 | Dragons — Customizable Dragon Pack | §11 |
| 47 | HEROIC FANTASY CREATURES FULL PACK VOL 1 | §18 — monsters |
| 48 | Monsters Ultimate Pack 02 + 09 (Cute Series) | §18 — style-matched monsters |
| 49 | Skeletons Pack + Ghoul Crew | §18 — undead (pairs with Polygonmaker Undead Animations) |
| 50 | GUI Pro — Fantasy RPG + Survival Clean | §20 |
| 51 | 6200 Fantasy RPG Icons Pack | §20 — item icons |
| 52 | 400 Low Poly RPG Weapons + Medieval Weapons | §20 — includes staves for the wizard |
| 53 | Toon Harbor Pack + FANTASTIC — Seaside Town | §7 — docks, sailing |
| 54 | Fantasy Portal FX | §10 |

### Tier 4 — only if the tier above leaves a gap (10)

Poseidon · Thalassophobia · Underwater FX · Volumetric Fog & Mist 2 · Wheel Controller 3D · Obi Rope · Non-Convex Mesh Collider · DestroyIt · Sound Shapes · Real Footsteps

### Bulk-grab note

Tiers 1–2 are 38 assets and mostly small (code, not art). Tier 3 is the heavy one. If you only have time for a subset, **Tier 1 alone changes the most**, because it covers the three subsystems that currently have no code at all: survival/crafting/building, portals, and audio.

---

## Appendix A — off-theme, excluded on sight

Named so you know they were considered and rejected, not overlooked. Roughly 210 of the 885.

**Sci-fi / space** (~45): all POLYGON Sci-Fi City / Cyber City, Sci-Fi Arsenal, Sci-Fi Effects, Sci Fi Level Construction Pack 1 & 2, Sci-Fi Heavy Station Kit, SciFi Space Base, Space Graphics Toolkit, Space Ship Shooter, NASA Space Flight Assets, SpaceGen, Top-Down Sci-Fi, Sci-Fi Turret Constructor, Sci-fi Weapons Arsenal, Shift Complete Sci-Fi UI, Modular Research Center, Sci-Fi UGUI Pack, and the sci-fi sound packs.
_Two exceptions held back for technique: **Sci-fi Portal And Machines Pack** (portal rendering) and **Space Graphics Planets** (planet shading from the space side)._

**Modern / urban / military** (~40): CScape City System, Cartoon City Builder, City Builder Urban, Low Poly Megapolis, Cyber-town, Japanese Street, Extreme Street/Road Pack, POLYGON City/Heist/Apocalypse/Western/Kids, Simple Military, Low Poly War Pack, POLYGON War Pack, Battle Royale Hero PBR, Soldier/Soldiers Packs, FPS frameworks (Aurora FPS, UFPS ×2, MFPS 2.0, FPS Framework 2.0, Modular Multiplayer FPS), gun sound packs, Shooting Range Interiors, Hazard & Safety signs.

**Vehicles / racing** (~25): Realistic Car Controller, Edy's Vehicle Physics, Sim-Cade, Powerslide Kart, Highroad Engine, Race Track Generator, Track BuildR, Toon Racing, Toon Planes, Cartoon Vehicles, Extreme Vehicle Pack, 7 Cars Pack, Rusty Cars, Retro Cartoon Cars, Hot Rod Constructor, Stylized Vehicles, Simple Racer, Train Controller, Road & Traffic System.
_Two exceptions held back: **Wheel Controller 3D** (cart wheels) and the vehicle-physics **suspension model**, which a wagon needs._

**2D / mobile / UI-framework** (~35): all 2D packs, Character Creator 2D, Flare Engine, Destructible Sprite Toolkit, 2DDL Pro, Smart Lighting 2D, NGUI, DoozyUI, Better UI, Lean GUI, EnhancedScroller, Figma Converter, UI Toolkit samples, Scratch Card, CCG Kit, Isometric Toolkit, Turn Based ToolKit, Dungeon Crawler Grid Controller, Blocks Engine 2.

**Networking / multiplayer** (~15): FishNet ×2, Mirror, PUN, TNet, Smooth Sync, uMMORPG ×2, Super Multiplayer Shooter, DFVoice, Best HTTP, File Transfer Server, Steamworks ×3.
_Single-player for now; revisit only if multiplayer becomes a goal._

**Horror / apocalypse / zombie** (~12), **AR/VR** (~5), **misc off-genre** (~30): Modular Burger Shop, Will's Room, Apocalypse Hospital, Horror Prison, THE CARNIVAL, Steampunk Western Carnival, Airstream Substance Showcase, Dice Pack, EndlessBook, MegaBook 2, Speech Recognition ×2, DeepVoice AI, Easy AR, Native Goodies for Android, Input.Touches, Mobile Tools.

_Full per-asset disposition is in the triage table (§Download list and Appendix B, pending)._

## 2026-09-10: Sidekick character and style trial

Adopted one `SidekickCharacters/Characters/Starter/Starter_01` art export into `Assets/Art/Characters/Human/`.
The saved Humanoid avatar uses existing project animation and procedural binding. Vendor scripts and shaders remain outside the project.
Selected Fantasy Kingdom sword, shield, hat, cape, doorway, roof, and palette art support the dedicated fitting scene.
The scene reuses current Polyperfect deer and wolf assets and the original POLYGON character.
The gameplay default remains unchanged pending Bryan's visual choice. Legacy clothing conversion remains unproven beyond the recorded samples.
See [Sidekick trial results](2026-09-10-sidekick-trial-results.md) for measured rig checks, accessory limitations, scene controls, and test evidence.

The body fitting follow-up adopts 22 `SK_HUMN_BASE_01` human part meshes into `Human/BaseParts/`.
`Human/BodyReview/` contains the bare-body prefab and five experimental POLYGON torso/arm conversions on the Sidekick skeleton.
`SidekickBodyReview.unity` compares these actors with the original POLYGON hero and existing animals/building samples.
The four body-shape sliders now drive native and converted parts together through baked POLYGON shape frames.
The frame transfer uses the bare Sidekick surface; skin colour and neutral seam refinement remain open.

### Sidekick full-body fitting extension — 2026-09-10

Added an independent trial under `Assets/Art/Characters/Human/BodyReview/FullBody/`. It derives ten body meshes and 40 shape frames from existing adopted POLYGON and Sidekick art. No additional vendor import occurred. The new `SidekickFullBodyReview.unity` scene retains the original and upper-body comparisons. Gameplay adoption remains pending visual acceptance.

### Second Sidekick outfit and skin matching — 2026-09-10

Derived ten Outfit 02 meshes and 40 shape frames under `Assets/Art/Characters/Human/BodyReview/Outfit02/` from the existing imported Fantasy Hero model. No new vendor import occurred. `SidekickSkinAuthor` reuses the Sidekick material for exposed skin and preserves clothing UVs. `SidekickOutfitReview.unity` presents both outfits beside the source rig. Cross-pack and loose-clothing compatibility remain unproven.

### Fantasy Kingdom character fitting trial — 2026-09-10

Copied `Models/FantasyKingdom_Characters.fbx` from the owned Synty PolygonFantasyKingdom pack to `Assets/Art/Characters/Human/Review/`. Reused `Kingdom.mat`, `KingdomAtlas.png`, and the Sidekick skin material. No vendor code or new textures/shaders were imported. Local importer Hips mapping corrects the source Root assignment.

Derived monk and peasant bodies under `Assets/Art/Characters/Human/BodyReview/Kingdom/`. The source combined heads are excluded from derived triangle lists; original references remain intact. Each candidate has four cached body shapes and uses the existing fitting algorithm. `SidekickKingdomReview.unity` compares source and Sidekick rigs. All 215 tests pass. Cloth dynamics and final visual acceptance remain open.

### Sidekick accessory fitting trial — 2026-09-10

Copied `SM_Prop_Bag_Explorer_01.fbx` and `SM_Item_Pouch_01.fbx` from the same Kingdom Models directory into the existing Review folder. Reused the priest hat, mage cape, material, and atlas. `SidekickAccessoryReview.unity` provides four attachment types on both rigs. No vendor scripts were imported. Rigid mounts need body-shape offsets; the cape needs independent motion and collision. The original monk front-panel overlap remains unresolved. Full findings and captures are listed in `2026-09-10-sidekick-trial-results.md`.

Follow-up 2026-09-11: Sidekick backpacks and pouches now use baked body-shape mount offsets. `MageCapeCloth.asset` derives a denser mesh from the already imported mage cape for a native Unity Cloth trial. The accessory scene has shoulder pins and body capsule proxies on both rigs. Cloth currently supports neutral body proportions; other shapes use rigid skinning. No additional vendor imports occurred. Floor, accessory, and self-collision remain open.

Further trial 2026-09-11: `SidekickAccessoryMotionReview.unity` uses the existing `BoneChainSpring` for capes, pouches, and separated backpack cups. Derived meshes under `BodyReview/AccessoryMotion/` preserve source assets. Robes omit covered shin skin and gain clearance; Sidekick hats sit lower. Tail Animator 2 and Magica Cloth 1.12.13 are locally available, but no vendor runtime was imported. All 229 EditMode tests pass. Walking improves, while extreme-motion cape contact remains unresolved; the live sweep recorded a maximum 0.860136 m contact error. This is review-only work, not gameplay adoption.

Wardrobe coverage 2026-09-11: `SidekickWardrobeReview.unity` adds Rider, Soldier Male, Blacksmith Female, and Priest pairs plus a modular helmet. Mage remains a separate saved conversion. All derive from already imported FBX assets. No vendor imports occurred. The existing fit, cached shapes, and palette conversion were reused. Attachment-bone mappings were added for the helmet. The 312-combination live sweep found no invalid vertices; 235 distinct tests passed across the full and focused runs. Loose cloth and integrated hoods remain exceptions. See `2026-09-11-sidekick-wardrobe-matrix.md` for provisional fit classes and evidence.
