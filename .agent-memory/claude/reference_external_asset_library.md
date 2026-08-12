---
name: reference_external_asset_library
description: External Unity asset scratch project at D:\Unity\Explore Assets — catalogued 2026-08-10 (2 rounds) into docs/research/2026-08-10-external-asset-catalog.md
metadata:
  type: reference
---

Bryan keeps an external Unity scratch project at `D:\Unity\Explore Assets\Assets` holding
his unimported Asset Store library. Surveyed twice on 2026-08-10: round 1 covered 18 packs
(~11.6 GB); round 2 covered **32 more packs added the same evening**, overwhelmingly
animation. Now **50 packs, ~22 GB, ~135k files**. ProceduralPlanets has imported only
**~101 MB / 64 FBX** (4 packs in `Assets/AssetPacks/`).

Full catalogue: [docs/research/2026-08-10-external-asset-catalog.md](../../docs/research/2026-08-10-external-asset-catalog.md)
— 14 sections, categorised, sized, per-pack clip/prefab names, verdicts, compatibility
landmines (§11), UNSURE list (§12), suggested sequence (§14).

Facts worth carrying without re-reading the doc:

- **CORRECTION to round 1.** Round 1 concluded "no humanoid animation set exists anywhere
  in the library." **That is false as of round 2.** ~27 animation packs, ~8 GB, ~7,000
  Humanoid (`animationType: 3`) clips, all retargetable onto Synty's Humanoid rig.
  Recommended base set: **Kevin Iglesias Human Mega Animations** — 1,373 clips, **zero rig
  import errors**, in-place by default with `[RM]` twins, 8-dir walk/run + 5-dir sprint +
  crouch + jump/fall/land + swim, male+female mirrored. Layer **Opsive OmniAnimation** on
  top for per-foot start/stop and 45° turn-in-place.
- **Two pieces of prior art for the spherical character controller.** (1)
  **`SuperCharacterController`**, bundled free inside `ExplosiveLLC`, permissive licence,
  **zero coroutines**: `up => transform.up` throughout, and `Code/Examples/Gravity.cs` has
  a serialized field literally named `planet` plus a `SpaceZone.unity` sphere demo. Its
  `BSPTree` mesh nearest-point solver feeds [[reference_collision_strategy]]. **Read it
  before writing more `CharacterMotor`.** (2) **Malbers Animal Controller** ships
  `IGravity { Vector3 Gravity {get;set;} Vector3 UpVector {get;} }` — structurally our
  `IGravityProvider` — plus a 20-line radial-gravity `GravityChanger`.
- **Wildlife AI is still the unsolved gap, and the two candidates fail differently.**
  Polyperfect = broken math (hard-coded `Vector3.up` plane projection) — unfixable.
  Malbers = correct math, wrong world contract (grounds via `Physics.Raycast` on
  `GroundLayer`; stock AI needs `NavMeshAgent`). **Malbers becomes viable after the physics
  bubble lands**; until then neither works. Malbers is also a **fork** (`//MWC:` /
  `//CustomPatch:` markers) and violates our rules with 113 coroutines + 21
  `[DefaultExecutionOrder]`.
- **Import traps that break the build, not just the look:**
  - **CS0101** — 8+ RamsterZ packs ship a byte-identical `Scripts/SimpleCameraController.cs`
    under different GUIDs. Importing two = compile failure. Delete all but one.
  - `ExplosiveLLC/Editor/SetupInputLayers.cs` force-opens an `EditorWindow` from
    `OnPostprocessAllAssets` on **every** import.
  - Fantasy Adventure Environment's shadergraphs use `UniversalPBRSubShader`, removed in
    Shader Graph 10 — highest-risk import against our SG 17.6.
  - Staggart Stylized Grass Shader imports cleanly and **silently does nothing** on Unity
    6000.6 (empty `RecordRenderGraph` stubs).
- **Synty `Generic_Basic.shadergraph` is a hard cross-pack dependency** (GUID
  `0730dae39bc73f34796280af9875ce14`, 8 packs). Moot while we import raw FBX and
  re-material onto `Planet/PropLit`.
- **Synty packs ship baked convex collision meshes** (`Models/Collision/*_Convex.asset`,
  thousands). Relevant to [[reference_collision_strategy]].
- **Pre-authored clumping precedents**: `TEM_Mushroom_Clump_01..06` and 44 `_G*` coral
  colony prefabs. Reference for [[project_scatter_clumping_direction]].
- Biome content gaps driving the recommendations: **Tundra = 0 prototypes, Ocean = 0**;
  Desert/Snow/IceBog/Mountain have 2–3 each.

- **The scratch folder is only ~6% of what Bryan owns.** Full Asset Store library =
  **885 purchases** (2012–2026), dumped to
  `docs/research/2026-08-10-unity-asset-store-purchases.tsv` and analysed in §15 of the
  catalog. **Retrieval recipe (works, ~5 min):** Unity MCP `execute_code` →
  `ServicesContainer.instance` (needs `BindingFlags.FlattenHierarchy`, it's a static on the
  generic `ScriptableSingleton<T>` base) → `Resolve<IAssetStoreClient>()` →
  `ListPurchases(new PurchasesQueryArgs(0, 1000, null, null))` → read
  `AssetStoreCache.m_PurchaseInfos` via reflection. All types are `internal` in
  `UnityEditor.CoreModule`, so everything must be reflection; the MCP executor is
  **CodeDom / C# 6**, so no local functions, tuples, or `out var`. No credentials needed —
  it rides the Editor's existing session.
- **Owned-but-unexplored assets that matter most:** **Kinematic Character Controller**
  (arbitrary-up is first-class — check before finalising the motor design), Character
  Controller Pro, Character Movement Fundamentals, Easy Character Movement 2, **The
  Vegetation Engine base** (unblocks the module in the scratch folder), **Voxelica voxel
  engine** (Phase 9 digging), SECTR World Streaming, Altos/EzCloud/UniStorm/InfiniCLOUD/
  Weather Maker (cloud comparanda), Stylized Water 2, Dynamic Water Physics 2 (buoyancy),
  GrassFlow 2, Odin Inspector.

- **Adoption map (2026-08-11, COMPLETE):** `docs/research/2026-08-11-asset-adoption-map.md` —
  organised **by subsystem**, every entry names a *part* not a pack, tagged ✅inspected /
  📦cached / 🔎expected. Includes a **Maybe** section and a **download list in 4 tiers**.
- **BIGGEST PROCESS LESSON: check the 63 GB download cache before downloading anything.** A
  6-agent sweep over cached `.unitypackage` files answered more than the scratch folder did.
  Three Tier-1 downloads got demoted because a cached pack solved it better (Poseidon >
  Stylized Water 2; iStep > Final IK), and the **entire animation category resolved with zero
  downloads**.
- **Per-subsystem harvest picks (all cached):** locomotion = **KCC**; foot IK = **iStep**
  (already sphere-ready — every cast takes the up-axis as a parameter); animation graph =
  **Animancer's Playables topology** (~150 lines; `ScriptPlayable` root as update pump replaces
  `[DefaultExecutionOrder]`; `AnimationLayerMixerPlayable` + additive masked layer = the
  spellcasting answer); water = **Poseidon** (flat-shaded per-facet lighting with no geometry
  shader, Bézier crest waves, noise-clipped shore foam, takes any mesh); navigation = **A\* Pro**;
  spell VFX = **Synty Particle FX** (180 prefabs, only on-style portal art) + **GAPH Set 1**
  with its toon patch; scatter = **VSP** (shadow-caster culling, impostor normal atlas + alpha
  dilation) + **GrassFlow** compute (in-place buffer compaction = clearing grass under buildings).
- **⚠️ SILENT-FAILURE PATTERN — 6 assets found** that import cleanly and do **nothing** on Unity
  6000.6 because they only override `Execute(...)` and never (meaningfully) declare
  `RecordRenderGraph`: Staggart Stylized Grass · Oceanis `VolumetricLightScattering` · Poseidon
  `PWaterEffectRendererFeature` · **MK Glow** (whole URP path) · **Aura 2** (`OnRenderImage`) ·
  **Stylized Water 2** — whose stub is *deliberate*: `RecordRenderGraph(...) { }` under a
  `//Silence warning spam` comment. **Always grep `RecordRenderGraph` before trusting a
  `ScriptableRendererFeature`.** The only two that passed: **Highlight Plus** (a correct
  `AddUnsafePass` migration — use it as the reference pattern) and **Altos** / **Volumetric Fog
  & Mist 2**.
- **ROUND 3 (86 newly downloaded packages + 2 standalone projects) — the recurring result was
  that the SMALL package won:** Fluid Seamless Portals (1 MB) > Dynamic Portals (158 MB);
  Conversa (2.7k LOC) > Dialogue System for Unity (90k); Ultimate Crafting System's 531-line
  `Placement/` > Survival Engine's building system; Love/Hate (~800 LOC model) = highest
  signal-to-noise in the survey. Both nominal "priority" AI packages were write-offs.
- **Round-3 harvest picks:** survival/crafting = **Survival Engine's schema** (`CraftData`
  polymorphic spine + `GroupData` empty-SO tags) over **UCS's plumbing** (`RuntimeID`/`ItemStack`/
  multi-output recipes) **and UCS's `Placement/` port-snap system** (quaternion-pure, sphere-safe;
  Survival Engine has *no* snap points at all). Portals = **Fluid's elastic-plane + coupled
  clip-offset**. Dialogue = **Conversa's `IEventNode`/`IValueNode` split** (a condition is a
  subgraph, not a scripting language) + 4 semantics from DSU. NPC relations = **Love/Hate's
  Deed/Rumor split** with magnitude-scaled memory expiry. Audio = **Ambient Sounds' `SliderRange`
  + Value/Event gating** (fully data-driven, zero colliders) + **SurfaceData's `Surface`-SO module
  list keyed by biome ID**. Clouds = **Altos's ¼-res + checkerboard-reprojection chain** (~16×
  fewer rays). AoE telegraphs = **DTT's URP decal shadergraphs** (depth-projected, conform to
  curvature natively). Prop pipeline = **TVE's sidecar conversion pattern** (§12.1).
- **⚠️ Round-3 rejections (don't re-evaluate):** **Blaze AI** (`BlazeAI.cs:1113` builds rotation by
  *zeroing quaternion x,z* — asserts world +Y; NavMesh welded into the decision layer),
  **Voxelica** as an engine (global XYZ lattice to the bit-shift level; its "planet" is a ball in
  a cube world — but **take its Transvoxel tables**), **Easy Grid Builder Pro** (4,251-line class,
  absolute world-Y levels), **Inventory Pro** (`InventoryItemBase : MonoBehaviour` — item
  definitions are prefabs), **RPG Farming Kit** (it's 2D/tilemaps; seasons are a `// TODO`),
  **FS Swimming** (one cached `float waterSurfaceY`; bundles a rival TPS controller),
  **Fantasy Portal FX**, **TVE Terrain Details Module** (shadows a Unity Terrain internal shader).
- **⚠️ Three decisions that must precede any survival/crafting code** (no package solves them):
  deterministic identity for scattered objects derived from `(seed, face, quadtree node, scatter
  index)`; a spatial key on every save record; and an `ISurfaceQuery` service so placement/settling/
  regrowth don't need the unbuilt physics bubble.
- **Rejected on inspection (don't re-evaluate):** Interactor (detection is a sphere trigger,
  7-line prioritisation, no view-angle scoring), SineVFX Portal (**no portal tech at all** — a
  textured quad, and 91/92 `.cs` are a vendored PostProcessing v2 fork), pelengami (14/26 are
  `#pragma surface` = magenta), Low Poly Ultimate Pack (~85% off-theme), Synty Snow Kit (winter
  sports), Procedural Walk Animation (`Vector3.up` in all 8 casts).
- **⚠️ Wyrms cached file is a CORRUPT/truncated download** — re-download before evaluating.
- **⚠️ `Action RPG Characters` is misfiled** — it's a 2 GB, 3,944-WAV **voice-over** library, not
  characters.
- **THE THREE FINDINGS THAT OUTRANK EVERYTHING** (all from packages already in the download
  cache — no downloads needed):
  1. **`NativeMovementPlane`** — A* Pro `Graphs/Utilities/GraphTransform.cs:102`. A
     quaternion-valued per-agent **tangent plane** with `ToPlane(float3)→float2` /
     `ToWorld(float2,elevation)→float3`. ~150 lines. **The correct shared primitive for every
     spherical system** (AI, character, mounts, carts, buoyancy, flight). Build it first.
  2. **Kinematic Character Controller** (Philippe St-Amand, cached) is the locomotion harvest
     target, superseding SuperCharacterController. Arbitrary-up is native
     (`_characterUp = _transientRotation * _cachedWorldUp`), `ICharacterController` seam ≈ our
     `CharacterMotor`, `Core/` has **0 coroutines / 0 async void / 0 Task.Run /
     0 RuntimeInitializeOnLoadMethod**, and it ships `PlanetManager.cs` + walkthroughs for
     arbitrary-up, swimming, ladders and moving platforms.
  3. **A* Pro has documented first-class SPHERICAL navigation** (changelog + shipped example
     scene). Only `NavMeshGraph` works — `RecastGraph` cannot wrap a sphere. Their tiled
     navmesh + shared-edge stitching with chessboard thread colouring maps straight onto our
     cube-sphere chunk streaming.
- **Unitypackage inspection without importing:** helper scripts live in the session scratchpad
  (`unpack.ps1`, `listpkg.ps1`). A `.unitypackage` is a gzipped tar of
  `<guid>/{asset,pathname}`; reconstruct by reading each `pathname` and copying `asset` there.
  ⚠️ Windows `tar.exe` is **bsdtar** and rejects GNU's `--wildcards` — use positional patterns
  (`tar -xzf pkg -C tmp "*/pathname"`) or Git Bash's GNU tar.

**Why:** the library is too big to re-scan (three multi-agent surveys); the docs are the
durable answer.

**How to apply:** when content breadth, a new biome, characters, animation, or wildlife
comes up, read the catalog before browsing `D:\` — and check §11 before importing anything,
since several packs import cleanly and then misbehave, and three will break the build.
