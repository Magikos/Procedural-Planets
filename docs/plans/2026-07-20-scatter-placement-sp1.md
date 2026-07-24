# Scatter Placement SP1 — Implementation Plan (v5)

> **For executors:** implement task-by-task, in order. SP1 touches only the
> **Planet** assembly — new `.cs` files are meaningful only after a **Unity
> import** regenerates the project (see Global constraints), then `dotnet build
> ProceduralPlanets.Planet.csproj`. Stage **explicit paths** (never `git add -A`)
> and commit only when Bryan authorizes. Runtime proof is `scatter.verify`
> (Task 6), run by Bryan — **no test framework**; do not add one. Preserve
> unrelated dirty-tree changes. Honor the STOP conditions.
>
> **v4.** Three Codex audit rounds folded in (all retained at the bottom as the
> review record): round 1 = 7 architectural blockers; round 2 (F01–F10) =
> edge-correctness + self-contained Task 1; round 3 (R3-01…08) = grass-exact area
> formula + `CeilToInt` level, release-safe `ScatterId.Pack` validation, Configure
> moved past the last cancellable await, finite DTO checks, public-`Gather` guards,
> and corner-straddle treated as an **expected** SP1 gap (Bryan's scope call:
> SP1 stays corner-incomplete; the shared corner fix is its own later slice).
> Round 4 (R4-01…06) = diagnostic candidate-budget preflight (main-thread hang
> guard), post-override DTO re-validation, grass path left **untouched** (scatter
> gets its own local-frame builder entry — no grass visual-gate needed), dead
> logger removed, corner/proof wording reconciled. Anchors verified against commit
> `d39e50f` + the dirty tree, 2026-07-22.

**Goal:** A headless, deterministic service emitting a stable stream of scatter
instances (id + world transform + prototype) for discrete props, gated by
biome / slope / altitude / water-clearance, with biome-border density falloff.

**Architecture:** Placement = pure function of `hash(worldSeed, face, level,
nodeX, nodeY, slotId)`. Each prototype's level is fixed **once per generated
world** from a canonical metric (not per query). A gather enumerates that level's
cube-face cells in a region, clips to the exact ROI, samples surface height +
biome in the planet's **local frame**, and runs a sampler-free `TryPlace`.

**Tech stack:** Unity 6 / C#. Reuses `FaceSpaceCellRangeBuilder`,
`IPlanetSurfaceSampler` (Planet), `IBiomeProvider` (`ColorGenerator`, injected
directly — it is an internal pipeline, not a ServiceLocator entry), the SO→DTO
settings pattern, and the plain-class console pattern (`MonoTargetType.Registry`
+ `ConsoleRegistry.RegisterInstance`).

## Global constraints (verbatim, every task)

- **No test framework.** Proof = `scatter.verify` + a fresh play run by Bryan.
- **Awaitable only** — no coroutines / `async void` / `Task.Run`.
- **Settings SO = editor authoring; runtime reads an immutable DTO.** Register
  the DTO **before the settings freeze**, via `Planet.RequiredSettings` +
  `Planet.RegisterWorldSettings` (Planet.cs:10-14, 111-126) — post-freeze
  `Register` throws (`SceneBootstrap` freezes after validating required DTOs).
- **ILogger / LoggerProvider**, never `UnityEngine.Debug.Log*`. No shader globals.
- **Comments only for non-obvious WHY.** No change-history comments. **The `Fnn`/
  `SPn`/`Rn` audit tags that appear in code snippets below are plan annotations —
  do NOT copy them into source; keep only the invariant a comment explains.**
- **Commit discipline (R3-08):** the `git commit` lines in each task are
  *checkpoints*, not licence. **Commit only when Bryan explicitly authorizes it in
  the execution turn**, and use the repo's imperative subject style
  (e.g. `Add scatter placement core`), not `feat(scope): …`.
- **Build gate (SP1 touches only the Planet assembly, so Core is not rebuilt):**
  `dotnet build ProceduralPlanets.Planet.csproj`. Expected `Build succeeded`, 0 errors.
  **Caveat (F07):** `ProceduralPlanets.Planet.csproj` lists sources with explicit
  `<Compile Include>` entries (104 of them; **no** Scatter entry yet). A brand-new
  `.cs` file is **not compiled** until Unity imports it and regenerates the
  project — so a `dotnet build` right after creating a file can report false
  success. For any task that **creates** new files, the build is only meaningful
  after a **Unity import/project-regeneration** (Bryan, in the editor); confirm
  the new path appears in the `.csproj` before trusting the result. Never
  hand-edit the generated `.csproj`. If the pinned editor is unavailable, mark the
  compile check inconclusive and stop.
- `graphify update .` after code changes.
- New code under `Assets/Scripts/Planet/Scatter/`. Include `.meta` for every new
  asset/script (Unity generates the `.meta` on import — stage it once it exists).
  Before each commit run `git diff --cached --name-only` and stage only scatter
  files + the named `Planet.cs` / `FaceSpaceCellRangeBuilder.cs` edits.

## Coordinate contract (resolved — no "confirm on read")

All sampling is in the planet's **local frame**, matching `Planet.TrySampleClimate`
(Planet.cs:443-468):
- `dir` from `FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere` is already a
  local-frame unit vector.
- Local surface radius: `Planet.TryGetSurfaceRadius(worldDir)` returns **world**
  radius, so convert: `worldDir = transform.TransformDirection(dir)`, then
  `localRadius = worldRadius / uniformScale`. `uniformScale =
  FaceSpaceCellRangeBuilder.GetUniformWorldScale(transform)`.
- Biome elevation (normalized, as the live path uses): `localRadius /
  PlanetRadius - 1f`; pass `dir` (local) + this elevation to
  `IBiomeProvider.EvaluateBiome`.
- Altitude gate in metres above sea: `(localRadius - seaRadiusLocal) *
  uniformScale`. `seaRadiusLocal` = `Planet.LastSeaLevelRadius` (local).
- `FaceSpaceCellRangeBuilder.BuildRanges` takes the **local** `planetRadius`
  (Planet.cs `PlanetRadius`) and applies scale itself — pass local, not world.
- World position of an accepted instance: `transform.TransformPoint(dir *
  localRadius)`.

---

### Task 1: Scatter primitives (embedded — the plan is self-contained here)

**Files:** Create `ScatterInteraction.cs`, `ScatterInstance.cs`, `ScatterId.cs`,
`ScatterHash.cs` under `Assets/Scripts/Planet/Scatter/`.

**Produces:** `ScatterInteraction`, `ScatterInstance`, `ScatterId.{Pack,Unpack,
IsPlayer,MaxLevel,MaxSlot}`, `ScatterHash.{Mix,Node,Slot,To01}`.

- [ ] **Step 1: `ScatterInteraction.cs`**

```csharp
public enum ScatterInteraction { None = 0, Collect = 1, Chop = 2 }
```

- [ ] **Step 2: `ScatterInstance.cs`**

```csharp
using UnityEngine;

public readonly struct ScatterInstance
{
    public readonly ulong Id;
    public readonly Vector3 PositionWS;
    public readonly Quaternion Rotation;
    public readonly float Scale;
    public readonly int PrototypeIndex;

    public ScatterInstance(ulong id, Vector3 positionWS, Quaternion rotation, float scale, int prototypeIndex)
    {
        Id = id; PositionWS = positionWS; Rotation = rotation; Scale = scale; PrototypeIndex = prototypeIndex;
    }
}
```

- [ ] **Step 3: `ScatterId.cs`** — bit budget face3/level5/x24/y24/slot4/player1 = 61.
  `MaxLevel` is **derived from `CoordBits`** so the id budget and the level cap can
  never diverge; `Pack` fails loud via unconditional argument checks on any overflow
  (`slot` is the prototype's `slotId`, never the array index).

```csharp
public static class ScatterId
{
    const int FaceBits = 3, LevelBits = 5, CoordBits = 24, SlotBits = 4;
    const int LevelShift = FaceBits;                 // 3
    const int XShift = LevelShift + LevelBits;       // 8
    const int YShift = XShift + CoordBits;           // 32
    const int SlotShift = YShift + CoordBits;        // 56
    const int PlayerShift = SlotShift + SlotBits;    // 60

    const ulong FaceMask = (1UL << FaceBits) - 1;
    const ulong LevelMask = (1UL << LevelBits) - 1;
    const ulong CoordMask = (1UL << CoordBits) - 1;
    const ulong SlotMask = (1UL << SlotBits) - 1;

    // Operational max placement level = coordinate-bit count: at level L, cell coords span
    // 0..2^L-1, which must fit CoordBits. Also fits LevelBits (max 31). Single source of truth.
    public const int MaxLevel = CoordBits;           // 24
    public const int MaxSlot = (1 << SlotBits) - 1;  // 15
    public const int FaceCount = 6;

    // Unconditional validation (not #if): the id is a persistence key — a masked-in invalid value
    // in a shipped build would rebind a saved chop/collect to the wrong object. Masks below are
    // packing mechanics only.
    public static ulong Pack(int face, int level, int x, int y, int slot, bool player = false)
    {
        if ((uint)face >= FaceCount)
            throw new System.ArgumentOutOfRangeException(nameof(face), face, "scatter face must be 0..5");
        if (level < 0 || level > MaxLevel)
            throw new System.ArgumentOutOfRangeException(nameof(level), level, $"scatter level must be 0..{MaxLevel}");
        if ((uint)x >= (1u << CoordBits) || (uint)y >= (1u << CoordBits))
            throw new System.ArgumentOutOfRangeException(nameof(x), $"scatter cell ({x},{y}) overflows {CoordBits}-bit id");
        if ((uint)slot > MaxSlot)
            throw new System.ArgumentOutOfRangeException(nameof(slot), slot, $"scatter slot must be 0..{MaxSlot}");
        return ((ulong)face & FaceMask)
             | (((ulong)level & LevelMask) << LevelShift)
             | (((ulong)(uint)x & CoordMask) << XShift)
             | (((ulong)(uint)y & CoordMask) << YShift)
             | (((ulong)slot & SlotMask) << SlotShift)
             | ((player ? 1UL : 0UL) << PlayerShift);
    }

    public static bool IsPlayer(ulong id) => ((id >> PlayerShift) & 1UL) != 0UL;

    public static void Unpack(ulong id, out int face, out int level, out int x, out int y, out int slot)
    {
        face = (int)(id & FaceMask);
        level = (int)((id >> LevelShift) & LevelMask);
        x = (int)((id >> XShift) & CoordMask);
        y = (int)((id >> YShift) & CoordMask);
        slot = (int)((id >> SlotShift) & SlotMask);
    }
}
```

- [ ] **Step 4: `ScatterHash.cs`**

```csharp
public static class ScatterHash
{
    public static uint Mix(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352du;
        x ^= x >> 15; x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    public static uint Node(int worldSeed, int face, int level, int x, int y)
    {
        uint h = (uint)worldSeed * 747796405u + 2891336453u;
        h = Mix(h ^ ((uint)face * 0x9e3779b1u));
        h = Mix(h ^ ((uint)level * 0x85ebca77u));
        h = Mix(h ^ ((uint)x * 0xc2b2ae3du));
        h = Mix(h ^ ((uint)y * 0x27d4eb2fu));
        return h;
    }

    public static uint Slot(uint nodeSeed, int slot) => Mix(nodeSeed ^ ((uint)slot * 0x9e3779b1u));

    public static float To01(uint h) => (h & 0x00FFFFFFu) / 16777216f;
}
```

- [ ] **Step 5: Unity import (Bryan)** so the four files enter the project (F07),
  then build `ProceduralPlanets.Planet.csproj`. Expected `Build succeeded`.
- [ ] **Step 6: Commit** — `git add Assets/Scripts/Planet/Scatter && git diff --cached --name-only` (scatter files + their `.meta` only) → `git commit -m "feat(scatter): deterministic id + hash primitives"`

---

### Task 2: Prototype + Library SOs and DTOs — stable slotId, signed altitude, water, pre-freeze registration

**Files:**
- Create `ScatterPrototype.cs`, `ScatterLibrary.cs`, `ScatterDtos.cs`
- Modify `Assets/Scripts/Planet/Planet.cs` (RequiredSettings + RegisterWorldSettings)
- Editor: `Assets/Resources/Settings/ScatterLibrary.asset` + prototypes

- [ ] **Step 1: `ScatterPrototype.cs`**

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Scatter Prototype", fileName = "ScatterPrototype")]
public sealed class ScatterPrototype : ScriptableObject
{
    public string DisplayName = "Prototype";

    [Header("Identity (persistence key — never reuse or reorder)")]
    [Tooltip("Immutable 0-15 id packed into every instance id. Unique per library.")]
    [Range(0, 15)] public int SlotId = 0;

    [Header("Placement")]
    [Min(0.05f)] public float SpacingMeters = 8f;
    public BiomeType Biome = BiomeType.Grassland;
    [Range(0.25f, 4f)] public float BiomeBlendPower = 1f;
    [Range(0f, 4f)] public float Weight = 1f; // independent density multiplier

    [Header("Slope gate")]
    [Range(0f, 90f)] public float MaxSlopeDegrees = 35f;
    [Range(0f, 15f)] public float SlopeFadeDegrees = 5f;

    [Header("Altitude gate (metres above sea; negative = underwater)")]
    public bool HasMinAltitude = false;
    public float MinAltitudeMeters = 0f;
    public bool HasMaxAltitude = false;
    public float MaxAltitudeMeters = 0f;
    [Tooltip("Land props: min metres above the waterline. 0 to disable.")]
    [Min(0f)] public float MinWaterClearanceMeters = 0.05f;

    [Header("Transform jitter")]
    public Vector2 ScaleRange = new Vector2(0.85f, 1.2f);
    public bool RandomYaw = true;

    [Header("Interaction (SP5)")]
    public ScatterInteraction Interaction = ScatterInteraction.None;
}
```

- [ ] **Step 2: `ScatterLibrary.cs`**

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Scatter Library", fileName = "ScatterLibrary")]
public sealed class ScatterLibrary : ScriptableObject
{
    public ScatterPrototype[] Prototypes = System.Array.Empty<ScatterPrototype>();
}
```

- [ ] **Step 3: `ScatterDtos.cs`** — validation fails loud (no silent truncation, unique in-range slotIds).

```csharp
using System.Collections.Generic;
using UnityEngine;

public sealed record ScatterPrototypeDto(
    string DisplayName,
    int SlotId,
    float SpacingMeters,
    BiomeType Biome,
    float BiomeBlendPower,
    float Weight,
    float MaxSlopeDegrees,
    float SlopeFadeDegrees,
    bool HasMinAltitude, float MinAltitudeMeters,
    bool HasMaxAltitude, float MaxAltitudeMeters,
    float MinWaterClearanceMeters,
    Vector2 ScaleRange,
    bool RandomYaw,
    ScatterInteraction Interaction)
{
    public static ScatterPrototypeDto From(ScatterPrototype p)
    {
        Validate(p);
        return new(
            p.DisplayName, p.SlotId,
            Mathf.Max(0.05f, p.SpacingMeters), p.Biome,
            Mathf.Max(0.01f, p.BiomeBlendPower), Mathf.Max(0f, p.Weight),
            p.MaxSlopeDegrees, Mathf.Max(0f, p.SlopeFadeDegrees),
            p.HasMinAltitude, p.MinAltitudeMeters,
            p.HasMaxAltitude, p.MaxAltitudeMeters,
            Mathf.Max(0f, p.MinWaterClearanceMeters),
            p.ScaleRange, p.RandomYaw, p.Interaction);
    }

    // Inspector [Range]/[Min] are editor hints, not runtime invariants — fail loud on
    // authoring that would silently produce a permanently-rejecting or degenerate prototype.
    static void Validate(ScatterPrototype p)
    {
        void Fail(string why) => throw new System.InvalidOperationException($"Scatter prototype '{p.DisplayName}': {why}");
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        if (!Finite(p.SpacingMeters) || p.SpacingMeters <= 0f) Fail("spacingMeters must be finite and positive.");
        if (!Finite(p.BiomeBlendPower) || !Finite(p.Weight)) Fail("biomeBlendPower/weight must be finite.");
        if (!Finite(p.MaxSlopeDegrees) || !Finite(p.SlopeFadeDegrees)) Fail("slope values must be finite.");
        if (!Finite(p.MinAltitudeMeters) || !Finite(p.MaxAltitudeMeters) || !Finite(p.MinWaterClearanceMeters))
            Fail("altitude/water values must be finite.");
        if (!Finite(p.ScaleRange.x) || !Finite(p.ScaleRange.y)) Fail("scale range must be finite.");
        if (!System.Enum.IsDefined(typeof(BiomeType), p.Biome)) Fail($"undefined biome {(int)p.Biome}.");
        if (p.HasMinAltitude && p.HasMaxAltitude && p.MinAltitudeMeters > p.MaxAltitudeMeters)
            Fail($"min altitude {p.MinAltitudeMeters} > max {p.MaxAltitudeMeters}.");
        if (!(p.ScaleRange.x > 0f) || !(p.ScaleRange.y > 0f) || p.ScaleRange.x > p.ScaleRange.y)
            Fail($"scale range {p.ScaleRange} must be positive and non-inverted.");
        if (p.MaxSlopeDegrees + Mathf.Max(0f, p.SlopeFadeDegrees) > 90f)
            Fail($"maxSlope + fade ({p.MaxSlopeDegrees}+{p.SlopeFadeDegrees}) exceeds 90 deg.");
    }
}

public sealed record ScatterLibraryDto(ScatterPrototypeDto[] Prototypes)
{
    // An empty library is a VALID world (no scatter props); scatter.verify reports it INCONCLUSIVE
    // rather than failing. Invalid *prototypes* still throw below.
    public static ScatterLibraryDto From(ScatterLibrary src)
    {
        var protos = src != null ? src.Prototypes : null;
        if (protos == null) return new ScatterLibraryDto(System.Array.Empty<ScatterPrototypeDto>());

        var seen = new HashSet<int>();
        var dtos = new ScatterPrototypeDto[protos.Length];
        for (int i = 0; i < protos.Length; i++)
        {
            var p = protos[i];
            if (p == null)
                throw new System.InvalidOperationException($"ScatterLibrary has a null prototype at index {i}.");
            if (p.SlotId < 0 || p.SlotId > ScatterId.MaxSlot)
                throw new System.InvalidOperationException($"Scatter prototype '{p.DisplayName}' SlotId {p.SlotId} out of range 0-{ScatterId.MaxSlot}.");
            if (!seen.Add(p.SlotId))
                throw new System.InvalidOperationException($"Scatter prototype '{p.DisplayName}' duplicates SlotId {p.SlotId}.");
            dtos[i] = ScatterPrototypeDto.From(p);
        }
        var dto = new ScatterLibraryDto(dtos);
        dto.EnsureValid();
        return dto;
    }

    // R4-03: world-setting overrides replace the registered DTO WITHOUT going through From, so the
    // final DTO must be re-validated before use. ScatterField.Configure calls this after resolving
    // the (possibly overridden) DTO. Single source of DTO invariants.
    public void EnsureValid()
    {
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        var seen = new HashSet<int>();
        for (int i = 0; i < Prototypes.Length; i++)
        {
            var p = Prototypes[i];
            if (p == null) throw new System.InvalidOperationException($"Scatter DTO prototype {i} is null.");
            if (p.SlotId < 0 || p.SlotId > ScatterId.MaxSlot)
                throw new System.InvalidOperationException($"Scatter '{p.DisplayName}' SlotId {p.SlotId} out of range.");
            if (!seen.Add(p.SlotId))
                throw new System.InvalidOperationException($"Scatter '{p.DisplayName}' duplicate SlotId {p.SlotId}.");
            if (!Finite(p.SpacingMeters) || p.SpacingMeters <= 0f
                || !Finite(p.BiomeBlendPower) || !Finite(p.Weight)
                || !Finite(p.MaxSlopeDegrees) || !Finite(p.SlopeFadeDegrees)
                || !Finite(p.MinAltitudeMeters) || !Finite(p.MaxAltitudeMeters) || !Finite(p.MinWaterClearanceMeters)
                || !Finite(p.ScaleRange.x) || !Finite(p.ScaleRange.y))
                throw new System.InvalidOperationException($"Scatter '{p.DisplayName}' has a non-finite/non-positive field.");
            if (p.ScaleRange.x <= 0f || p.ScaleRange.x > p.ScaleRange.y)
                throw new System.InvalidOperationException($"Scatter '{p.DisplayName}' invalid scale range {p.ScaleRange}.");
            if (p.HasMinAltitude && p.HasMaxAltitude && p.MinAltitudeMeters > p.MaxAltitudeMeters)
                throw new System.InvalidOperationException($"Scatter '{p.DisplayName}' min altitude > max.");
        }
    }
}
```

> No `EnsureRegistered` self-loader: registration is driven by `Planet` before
> freeze (Step 4), matching `PlanetDto`/`BiomeDto`.

- [ ] **Step 4: Register before freeze in `Planet.cs`.** Modify RequiredSettings (Planet.cs:10-14) and RegisterWorldSettings (Planet.cs:122-125):

```csharp
    static readonly System.Type[] RequiredSettings =
    {
        typeof(PlanetDto),
        typeof(BiomeDto),
        typeof(ScatterLibraryDto),
    };
```
Add inside `RegisterWorldSettings`, after the BiomeDto registration:

```csharp
        if (!settings.IsRegistered<ScatterLibraryDto>())
        {
            var scatterLib = Resources.Load<ScatterLibrary>("Settings/ScatterLibrary");
            if (scatterLib == null)
                throw new System.InvalidOperationException(
                    "ScatterLibraryDto requires Resources/Settings/ScatterLibrary.asset.");
            settings.Register(ScatterLibraryDto.From(scatterLib));
        }
```

- [ ] **Step 5: Unity import (Bryan) → confirm the new files are in `ProceduralPlanets.Planet.csproj` → build it.** Expected `Build succeeded`.
- [ ] **Step 6: Editor asset** — create `Assets/Resources/Settings/ScatterLibrary.asset` + prototypes with distinct `SlotId`s (e.g. Tree SlotId 0 / Spacing 24 / Forest; Bush SlotId 1 / Spacing 6 / Grassland; Flower SlotId 2 / Spacing 3 / Grassland). Assign into the library.
- [ ] **Step 7: Commit** — `git add Assets/Scripts/Planet/Scatter Assets/Scripts/Planet/Planet.cs Assets/Resources/Settings && git diff --cached --name-only` (confirm no stray files) then `git commit -m "feat(scatter): prototype/library SOs + DTOs with slotId validation; register pre-freeze"`

---

### Task 3: Quadtree math — fixed level per world, slot-seeded jitter, area-keep

**Files:** Create `ScatterQuadtree.cs`

- [ ] **Step 1: `ScatterQuadtree.cs`**

```csharp
using UnityEngine;

public static class ScatterQuadtree
{
    const float JitterMargin = 0.12f;

    public static float CellUvWidth(int level) => 1f / (1 << Mathf.Clamp(level, 0, ScatterId.MaxLevel));

    // Fixed per generated world: canonical face span 2*planetWorldRadius (the grass reference),
    // independent of the query origin, so a prototype's cells never move as the camera moves.
    public static int LevelForSpacing(float planetWorldRadius, float spacingMeters)
    {
        float span = 2f * Mathf.Max(planetWorldRadius, 1f);
        float ratio = Mathf.Max(1f, span / Mathf.Max(spacingMeters, 0.05f));
        // Ceil so the chosen cell is never LARGER than the target spacing (rounding down would be
        // permanently under-dense — one candidate per cell cannot recover the missing density).
        int raw = Mathf.Max(0, Mathf.CeilToInt(Mathf.Log(ratio, 2f)));
        if (raw > ScatterId.MaxLevel) // fail loud, never silently clamp a persistence key
            throw new System.InvalidOperationException(
                $"Scatter spacing {spacingMeters} m needs quadtree level {raw} > max {ScatterId.MaxLevel} " +
                $"on world radius {planetWorldRadius} m. Increase spacing.");
        return raw;
    }

    // Candidate jitter is slot-seeded so two same-level prototypes never share a point.
    public static Vector2 CandidateUv(int x, int y, float cellUvWidth, uint slotSeed)
    {
        float jx = Mathf.Lerp(JitterMargin, 1f - JitterMargin, ScatterHash.To01(ScatterHash.Slot(slotSeed, 101)));
        float jy = Mathf.Lerp(JitterMargin, 1f - JitterMargin, ScatterHash.To01(ScatterHash.Slot(slotSeed, 202)));
        return new Vector2((x + jx) * cellUvWidth, (y + jy) * cellUvWidth);
    }

    // Cube-to-sphere area probability, IDENTICAL to grass CubeFaceAreaKeep
    // (GrassNearFieldPlace.compute:112-118) so the SP3 GPU mirror matches exactly: with
    // signedUv = uv*2-1, distortion = (1 + |signedUv|^2)^-1.5. centerKeep scales for the fixed
    // cell being larger/smaller than the target spacing. Face-agnostic (uses only uv).
    public static float AreaKeep(Vector2 faceUv, float cellUvWidth, float spacingMeters, float planetWorldRadius)
    {
        Vector2 s = faceUv * 2f - Vector2.one;
        float denom = 1f + Vector2.Dot(s, s);
        float distortion = 1f / Mathf.Max(Mathf.Sqrt(denom * denom * denom), 1e-6f);
        float centerCellWorld = 2f * planetWorldRadius * cellUvWidth;
        float ratio = centerCellWorld / Mathf.Max(spacingMeters, 0.05f);
        return Mathf.Clamp01(ratio * ratio * distortion);
    }
}
```

- [ ] **Step 2: Unity import (Bryan) → confirm the new file is in `ProceduralPlanets.Planet.csproj` → build it.** Expected `Build succeeded`. (Or batch Tasks 1-4's imports into one checkpoint — none compile-check meaningfully until imported.)
- [ ] **Step 3: Commit** — `git add Assets/Scripts/Planet/Scatter && git commit -m "feat(scatter): fixed-level quadtree math + slot jitter + area-keep"`

---

### Task 4: Placement math (`TryPlace`) — add water clearance + area-keep + signed altitude

**Files:** Create `ScatterPlacementMath.cs`

- [ ] **Step 1: `ScatterPlacementMath.cs`**

```csharp
using UnityEngine;

public struct PlacementRules
{
    public float Weight;
    public float MaxSlopeCos;   // cos(maxSlope + fade)
    public float MinSlopeCos;   // cos(maxSlope)
    public bool HasMinAltitude; public float MinAltitude;
    public bool HasMaxAltitude; public float MaxAltitude;
    public float MinWaterClearance;
    public Vector2 ScaleRange;
    public bool RandomYaw;
}

public static class ScatterPlacementMath
{
    // At zero fade MaxSlopeCos == MinSlopeCos and InverseLerp(a,a,x) returns 0, which would
    // reject flat ground too. Degenerate interval → explicit hard cutoff.
    static float SlopeKeep(float maxSlopeCos, float minSlopeCos, float slopeCos)
    {
        if (minSlopeCos - maxSlopeCos <= 1e-5f)
            return slopeCos >= minSlopeCos ? 1f : 0f;
        return Mathf.InverseLerp(maxSlopeCos, minSlopeCos, slopeCos);
    }

    // dir/radius are LOCAL (caller converts to world). slopeCos = dot(surfaceNormal, dir).
    // altitudeMeters = signed metres above sea. densityKeep = areaKeep * membership^blendPower,
    // folded in by the caller so this function stays free of biome types (HLSL-portable).
    public static bool TryPlace(uint slotSeed, Vector3 dir, float localRadius,
        float altitudeMeters, float slopeCos, float densityKeep, bool hasOcean, in PlacementRules rules,
        out Vector3 posLocal, out Quaternion rot, out float scale)
    {
        posLocal = default; rot = Quaternion.identity; scale = 0f;

        if (rules.HasMinAltitude && altitudeMeters < rules.MinAltitude) return false;
        if (rules.HasMaxAltitude && altitudeMeters > rules.MaxAltitude) return false;
        if (hasOcean && rules.MinWaterClearance > 0f && altitudeMeters < rules.MinWaterClearance) return false;

        float slopeKeep = SlopeKeep(rules.MaxSlopeCos, rules.MinSlopeCos, slopeCos);
        if (slopeKeep <= 0f) return false;

        float accept = rules.Weight * slopeKeep * Mathf.Clamp01(densityKeep);
        if (ScatterHash.To01(slotSeed) >= accept) return false;

        posLocal = dir * localRadius;
        scale = Mathf.Lerp(rules.ScaleRange.x, rules.ScaleRange.y, ScatterHash.To01(ScatterHash.Slot(slotSeed, 7)));
        Quaternion align = Quaternion.FromToRotation(Vector3.up, dir);
        float yaw = rules.RandomYaw ? ScatterHash.To01(ScatterHash.Slot(slotSeed, 9)) * 360f : 0f;
        rot = Quaternion.AngleAxis(yaw, dir) * align; // LOCAL rotation; caller applies planet rotation
        return true;
    }
}
```

- [ ] **Step 2: Unity import (Bryan) → confirm the new file is in `ProceduralPlanets.Planet.csproj` → build it.** Expected `Build succeeded`. (Or batch Tasks 1-4's imports into one checkpoint — none compile-check meaningfully until imported.)
- [ ] **Step 3: Commit** — `git add Assets/Scripts/Planet/Scatter && git commit -m "feat(scatter): TryPlace with signed altitude, water clearance, area-keep"`

---

### Task 5: `ScatterField` + local-frame gather + Planet wiring

**Files:**
- Create `ScatterField.cs`
- Modify `Planet.cs` (construct in `EnsureRuntimeOwners`; `Configure` after radii known; dispose in `TeardownWorld`)
- Modify `FaceSpaceCellRangeBuilder.cs` (add a `cameraPos` overload)

- [ ] **Step 1: add a local-frame entry in `FaceSpaceCellRangeBuilder.cs` — leave the existing `Camera` (grass) overload UNTOUCHED (R4-04).** Altering grass's shared path would change grass coverage on a *rotated* planet and trip the visual-tuning gate (before/after grass captures + Bryan's review). Scatter sidesteps that with its own entry that derives the face-UV from the **planet-local** direction:

```csharp
    public static FaceSpaceRangeResult BuildRangesLocal(
        Vector3 cameraPos, Transform planetTransform, float planetRadius, float worldRadius,
        float cellUvWidth, int pageCellSize, FaceSpaceCell[] outRanges)
    {
        // Identical cell/range logic to BuildRanges(Camera, ...), but the observer direction is
        // mapped into the planet-local frame first, so a rotated planet enumerates the right face:
        //   Vector3 toCamera = cameraPos - planetTransform.position;
        //   Vector3 localDir = planetTransform.InverseTransformDirection(toCamera).normalized;
        //   DirectionToFaceUv(localDir, out primaryFace, out primaryFaceUv);   // then the same body
        // Factor the shared post-direction cell math into a private helper both overloads call so
        // grass's existing world-frame result stays byte-identical; only scatter is local-correct.
    }
```
Scatter's `GatherCore` calls `BuildRangesLocal`; grass keeps its existing `Camera` overload. Unifying the two behind one frame convention is a later cleanup gated on rotated-grass captures — not SP1.

- [ ] **Step 2: `ScatterField.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

[CommandPrefix("scatter")]
public sealed class ScatterField : System.IDisposable
{
    readonly Transform _planetTransform;
    readonly IPlanetSurfaceSampler _surface;
    readonly IBiomeProvider _biome;
    readonly FaceSpaceCell[] _ranges = new FaceSpaceCell[FaceSpaceCellRangeBuilder.MaxRanges];

    const long CandidateBudget = 2_000_000; // diagnostic preflight guard (R4-02)

    ScatterLibraryDto _library;
    int _worldSeed;
    float _baseRadiusLocal;
    float _seaRadiusLocal;
    bool _hasOcean;
    bool _configured;
    int[] _levels; // fixed per prototype per world

    public ScatterField(Transform planetTransform, IPlanetSurfaceSampler surface, IBiomeProvider biome)
    {
        _planetTransform = planetTransform;
        _surface = surface;
        _biome = biome;
        ConsoleRegistry.RegisterInstance(this);
    }

    // Called after every successful generation (beside _grass.Configure). Radii are LOCAL.
    public void Configure(int worldSeed, float baseRadiusLocal, float seaRadiusLocal, bool hasOcean)
    {
        _worldSeed = worldSeed;
        _baseRadiusLocal = baseRadiusLocal;
        _seaRadiusLocal = seaRadiusLocal;
        _hasOcean = hasOcean;
        _library = SettingsProvider.GetSettings<ScatterLibraryDto>();
        _library.EnsureValid(); // R4-03: re-validate — overrides may have replaced the DTO post-From

        float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        float worldRadius = baseRadiusLocal * scale;
        _levels = new int[_library.Prototypes.Length];
        for (int i = 0; i < _levels.Length; i++)
            _levels[i] = ScatterQuadtree.LevelForSpacing(worldRadius, _library.Prototypes[i].SpacingMeters);
        _configured = true;
    }

    public void Reset() => _configured = false;

    public struct ScatterGatherStats { public int Candidates; public int Accepted; public bool CornerStraddle; }

    public int Gather(Vector3 cameraPos, float regionRadiusMeters, int maxLevel, List<ScatterInstance> buffer)
    {
        if (buffer == null) throw new System.ArgumentNullException(nameof(buffer));
        if (!(regionRadiusMeters > 0f) || float.IsInfinity(regionRadiusMeters))
            throw new System.ArgumentOutOfRangeException(nameof(regionRadiusMeters), regionRadiusMeters, "must be finite and positive");
        if (maxLevel < 0 || maxLevel > ScatterId.MaxLevel)
            throw new System.ArgumentOutOfRangeException(nameof(maxLevel), maxLevel, $"must be 0..{ScatterId.MaxLevel}");
        return GatherCore(cameraPos, regionRadiusMeters, maxLevel, buffer, reversed: false, out _);
    }

    // One core for both public gather and the diagnostic reverse traversal. `reversed`
    // flips prototype/cell/candidate order so scatter.verify can prove order-independence.
    internal int GatherCore(Vector3 cameraPos, float region, int maxLevel, List<ScatterInstance> buffer,
        bool reversed, out ScatterGatherStats stats)
    {
        stats = default;
        if (!_configured || _library == null) return 0;
        Transform t = _planetTransform;
        float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(t);
        // Clip against the observer's surface anchor (the point under the camera), not the
        // camera's 3D position — otherwise altitude shrinks the footprint and empties the gather.
        if (!TryResolveSurfaceAnchor(cameraPos, scale, out Vector3 anchorWS)) return 0;
        float r2 = region * region;
        int protoCount = _library.Prototypes.Length;
        int emitted = 0;

        for (int pk = 0; pk < protoCount; pk++)
        {
            int pi = reversed ? protoCount - 1 - pk : pk;
            var proto = _library.Prototypes[pi];
            int level = _levels[pi];
            if (level > maxLevel) continue;
            float cellUv = ScatterQuadtree.CellUvWidth(level);

            var result = FaceSpaceCellRangeBuilder.BuildRangesLocal(cameraPos, t, _baseRadiusLocal, region, cellUv, 1, _ranges);
            stats.CornerStraddle |= result.UncoveredCornerStraddle;
            PlacementRules rules = BuildRules(proto);

            for (int rk = 0; rk < result.Count; rk++)
            {
                FaceSpaceCell cell = _ranges[reversed ? result.Count - 1 - rk : rk];
                int cells = cell.GridSize.x * cell.GridSize.y;
                for (int ck = 0; ck < cells; ck++)
                {
                    int c = reversed ? cells - 1 - ck : ck;
                    int x = cell.PageOriginCellUV.x + (c % cell.GridSize.x);
                    int y = cell.PageOriginCellUV.y + (c / cell.GridSize.x);

                    uint nodeSeed = ScatterHash.Node(_worldSeed, cell.FaceIndex, level, x, y);
                    uint slotSeed = ScatterHash.Slot(nodeSeed, proto.SlotId);
                    Vector2 uv = ScatterQuadtree.CandidateUv(x, y, cellUv, slotSeed);
                    Vector3 dir = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(cell.FaceIndex, uv);

                    Vector3 worldDir = t.TransformDirection(dir).normalized;
                    if (!_surface.TryGetSurfaceRadius(worldDir, out float worldRadius) || worldRadius <= 0f) continue;
                    float localRadius = worldRadius / Mathf.Max(scale, 1e-4f);

                    Vector3 worldPos = t.TransformPoint(dir * localRadius);
                    if ((worldPos - anchorWS).sqrMagnitude > r2) continue;
                    stats.Candidates++;

                    float membership = Membership(dir, localRadius, proto.Biome);
                    if (membership <= 0f) continue;

                    float slopeCos = SlopeCos(cell.FaceIndex, uv, dir, localRadius, cellUv, scale);
                    float altitudeMeters = (localRadius - _seaRadiusLocal) * scale;
                    float densityKeep = ScatterQuadtree.AreaKeep(uv, cellUv, proto.SpacingMeters, _baseRadiusLocal * scale)
                                        * Mathf.Pow(membership, proto.BiomeBlendPower);

                    if (ScatterPlacementMath.TryPlace(slotSeed, dir, localRadius, altitudeMeters, slopeCos,
                            densityKeep, _hasOcean, rules, out Vector3 posLocal, out Quaternion rot, out float sc))
                    {
                        ulong id = ScatterId.Pack(cell.FaceIndex, level, x, y, proto.SlotId);
                        buffer.Add(new ScatterInstance(id, t.TransformPoint(posLocal), t.rotation * rot, sc, pi));
                        emitted++; stats.Accepted++;
                    }
                }
            }
        }
        return emitted;
    }

    bool TryResolveSurfaceAnchor(Vector3 observerWS, float scale, out Vector3 anchorWS)
    {
        anchorWS = default;
        Vector3 toObs = observerWS - _planetTransform.position;
        if (toObs.sqrMagnitude < 1e-6f) return false;
        Vector3 worldDir = toObs.normalized;
        if (!_surface.TryGetSurfaceRadius(worldDir, out float wr) || wr <= 0f) return false;
        Vector3 localDir = _planetTransform.InverseTransformDirection(worldDir).normalized;
        anchorWS = _planetTransform.TransformPoint(localDir * (wr / Mathf.Max(scale, 1e-4f)));
        return true;
    }

    // Shared diagnostic guard (R4-02): validate args and bail before a fine-spacing prototype
    // enumerates tens of millions of cells on the main thread. Uses `long` to avoid overflow.
    bool TryPrepDiagnostic(float? regionMeters, float def, float lo, float hi, int maxLevel, out float region, out string error)
    {
        error = null;
        region = regionMeters ?? def;
        if (float.IsNaN(region) || float.IsInfinity(region)) { error = "scatter: region must be finite"; return false; }
        region = Mathf.Clamp(region, lo, hi);
        if (maxLevel < 0 || maxLevel > ScatterId.MaxLevel) { error = $"scatter: maxLevel must be 0..{ScatterId.MaxLevel}"; return false; }

        float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
        float worldRadius = _baseRadiusLocal * scale;
        long est = 0;
        for (int i = 0; i < _library.Prototypes.Length; i++)
        {
            if (_levels[i] > maxLevel) continue;
            float cellWorld = 2f * worldRadius * ScatterQuadtree.CellUvWidth(_levels[i]);
            long side = (long)(2f * region / Mathf.Max(cellWorld, 1e-4f)) + 2;
            est += side * side;
            if (est > CandidateBudget) { error = $"scatter: candidate budget exceeded (~{est:N0}); reduce region or coarsen spacing"; return false; }
        }
        return true;
    }

    static PlacementRules BuildRules(ScatterPrototypeDto p) => new PlacementRules
    {
        Weight = p.Weight,
        MinSlopeCos = Mathf.Cos(p.MaxSlopeDegrees * Mathf.Deg2Rad),
        MaxSlopeCos = Mathf.Cos((p.MaxSlopeDegrees + p.SlopeFadeDegrees) * Mathf.Deg2Rad),
        HasMinAltitude = p.HasMinAltitude, MinAltitude = p.MinAltitudeMeters,
        HasMaxAltitude = p.HasMaxAltitude, MaxAltitude = p.MaxAltitudeMeters,
        MinWaterClearance = p.MinWaterClearanceMeters,
        ScaleRange = p.ScaleRange, RandomYaw = p.RandomYaw,
    };

    float Membership(Vector3 dir, float localRadius, BiomeType biome)
    {
        float elevation = localRadius / _baseRadiusLocal - 1f;
        BiomeResult r = _biome.EvaluateBiome(dir, elevation);
        if (r.PrimaryBiome == biome) return 1f - r.BlendWeight;
        if (r.SecondaryBiome == biome) return r.BlendWeight;
        return 0f;
    }

    float SlopeCos(int face, Vector2 uv, Vector3 dir, float localRadius, float cellUv, float scale)
    {
        float e = cellUv * 0.5f;
        Vector3 du = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(face, uv + new Vector2(e, 0f));
        Vector3 dv = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(face, uv + new Vector2(0f, e));
        float ru = SampleLocal(du, localRadius, scale);
        float rv = SampleLocal(dv, localRadius, scale);
        Vector3 p0 = dir * localRadius, pu = du * ru, pv = dv * rv;
        Vector3 n = Vector3.Cross(pu - p0, pv - p0).normalized;
        if (Vector3.Dot(n, dir) < 0f) n = -n;
        return Mathf.Clamp01(Vector3.Dot(n, dir));
    }

    float SampleLocal(Vector3 localDir, float fallbackLocal, float scale)
    {
        Vector3 wd = _planetTransform.TransformDirection(localDir).normalized;
        return _surface.TryGetSurfaceRadius(wd, out float wr) ? wr / Mathf.Max(scale, 1e-4f) : fallbackLocal;
    }

    public void Dispose() => ConsoleRegistry.UnregisterInstance(typeof(ScatterField));
}
```

- [ ] **Step 3: Wire into `Planet.cs`.**
  - Field: add `ScatterField _scatter;` beside `_grass` (Planet.cs:46).
  - In `EnsureRuntimeOwners` (Planet.cs:90): add `_scatter ??= new ScatterField(transform, this, _colorGenerator);`
  - In `InitializeAsync` reset (Planet.cs:190, beside `_grass.DisposeControllers()`): add `_scatter?.Reset();`
  - In `GeneratePlanetAsync`, place the Configure call **after the final `await
    Awaitable.NextFrameAsync(ct)` (Planet.cs:327) and immediately before
    `EventBus<PlanetGeneratedEvent>.Raise(...)` (Planet.cs:328)** — not at :324. `Configure`
    means "generation succeeded"; putting it before the last cancellable await would leave the
    field configured for a generation that was cancelled and never published readiness (R3-01).
    `seaLevelRadius`/`planet` are still in scope there:
    `_scatter.Configure(Seed, planet.PlanetRadius, seaLevelRadius, planet.HasOceans);`
    Ocean *presence* is `planet.HasOceans` (bool), **not** `OceanLevel > 0` — default is
    `HasOceans = true, OceanLevel = 0`; grass gates on `HasOceans` (`PlanetGrassCoordinator.cs:159`).
    `OceanLevel` feeds only `seaLevelRadius` (Planet.cs:322).
  - In `TeardownWorld` (Planet.cs:145): dispose beside grass — `_scatter?.Dispose(); _scatter = null;` (match how `_grass` is disposed there).

- [ ] **Step 4: Unity import (Bryan)** then build `ProceduralPlanets.Planet.csproj` (F07: new `ScatterField.cs` must be in the regenerated project). Expected `Build succeeded`.
- [ ] **Step 5: Commit** — stage exactly: `git add Assets/Scripts/Planet/Scatter Assets/Scripts/Planet/Planet.cs Assets/Scripts/Planet/Grass/FaceSpaceCellRangeBuilder.cs`; run `git diff --cached --name-only`; `git commit -m "feat(scatter): ScatterField local-frame gather + Planet wiring + cameraPos range overload"`

---

### Task 6: `scatter.verify` + `scatter.count` — real proof (registry commands)

**Files:** Modify `ScatterField.cs`.

- [ ] **Step 1: Add commands** — `MonoTargetType.Registry` (plain class; `Single` throws per `CommandExecutor.cs:190-201`).

```csharp
[ConsoleCommand("count", "Gather at the camera; per-prototype counts + candidates + elapsed ms.", MonoTargetType.Registry)]
string CountCmd(float? regionMeters = null, int? maxLevel = null)
{
    var cam = Camera.main; if (cam == null) return "scatter: no main camera";
    if (!_configured) return "scatter: not configured (generate a planet first)";
    int lvl = maxLevel ?? ScatterId.MaxLevel;
    if (!TryPrepDiagnostic(regionMeters, 80f, 5f, 400f, lvl, out float region, out string err)) return err;
    var buf = new List<ScatterInstance>(8192);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    GatherCore(cam.transform.position, region, lvl, buf, false, out var stats);
    sw.Stop();
    var per = new int[_library.Prototypes.Length];
    foreach (var inst in buf) per[inst.PrototypeIndex]++;
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"scatter: {buf.Count} accepted / {stats.Candidates} candidates in {region:F0} m " +
                  $"(maxLevel {lvl}, {sw.ElapsedMilliseconds} ms){(stats.CornerStraddle ? " [CORNER STRADDLE]" : "")}");
    for (int i = 0; i < per.Length; i++)
        sb.AppendLine($"  [{i}] slot {_library.Prototypes[i].SlotId} {_library.Prototypes[i].DisplayName}: {per[i]}");
    return sb.ToString().TrimEnd();
}

[ConsoleCommand("verify", "Proof: nonzero, unique, order-independent, transform-stable, region-independent, id round-trip.", MonoTargetType.Registry)]
string VerifyCmd(float? regionMeters = null)
{
    var cam = Camera.main; if (cam == null) return "scatter: no main camera";
    if (!_configured) return "scatter: not configured (generate a planet first)";
    if (_library.Prototypes.Length == 0) return "scatter.verify INCONCLUSIVE: empty library";
    if (!TryPrepDiagnostic(regionMeters, 60f, 5f, 200f, ScatterId.MaxLevel, out float region, out string err)) return err;
    Vector3 c = cam.transform.position;
    var sw = System.Diagnostics.Stopwatch.StartNew();

    var fwd = new List<ScatterInstance>(8192);
    var rev = new List<ScatterInstance>(8192);
    GatherCore(c, region, ScatterId.MaxLevel, fwd, false, out var statsF);
    GatherCore(c, region, ScatterId.MaxLevel, rev, true, out _);
    // Corner straddle is a known, deliberately-unscoped SP1 gap (shared builder) — the covered
    // cells are still deterministic, so it is reported, not failed.
    if (fwd.Count == 0) return "scatter.verify INCONCLUSIVE: no instances in view (move to a populated biome)";

    var mapF = new Dictionary<ulong, ScatterInstance>(fwd.Count);
    foreach (var i in fwd) if (!mapF.TryAdd(i.Id, i)) return $"scatter.verify FAIL: duplicate id {i.Id} (forward)";
    var mapR = new Dictionary<ulong, ScatterInstance>(rev.Count);
    foreach (var i in rev) if (!mapR.TryAdd(i.Id, i)) return $"scatter.verify FAIL: duplicate id {i.Id} (reverse)";
    if (mapF.Count != mapR.Count) return $"scatter.verify FAIL: {mapF.Count} vs {mapR.Count} unique ids across orders";

    int drift = 0;
    foreach (var kv in mapF)
    {
        if (!mapR.TryGetValue(kv.Key, out var r)) return $"scatter.verify FAIL: id {kv.Key} missing in reverse order";
        var f = kv.Value;
        if ((f.PositionWS - r.PositionWS).sqrMagnitude > 1e-6f || f.PrototypeIndex != r.PrototypeIndex
            || Quaternion.Angle(f.Rotation, r.Rotation) > 0.01f || Mathf.Abs(f.Scale - r.Scale) > 1e-4f) drift++;
    }
    if (drift > 0) return $"scatter.verify FAIL: {drift} transform drifts across orders";

    // Region independence: a smaller ROI equals the larger gather filtered to the small disc.
    float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
    if (!TryResolveSurfaceAnchor(c, scale, out Vector3 anchor)) return "scatter.verify INCONCLUSIVE: no surface anchor";
    float small = region * 0.5f, s2 = small * small;
    var smallList = new List<ScatterInstance>(4096);
    GatherCore(c, small, ScatterId.MaxLevel, smallList, false, out _);
    var smallSet = new HashSet<ulong>(); foreach (var i in smallList) smallSet.Add(i.Id);
    var filtered = new HashSet<ulong>(); foreach (var i in fwd) if ((i.PositionWS - anchor).sqrMagnitude <= s2) filtered.Add(i.Id);
    if (!smallSet.SetEquals(filtered)) return $"scatter.verify FAIL: region-independence ({smallSet.Count} small vs {filtered.Count} filtered)";

    // ID pack/unpack incl. player bit = true.
    ScatterId.Unpack(fwd[0].Id, out int f0, out int l0, out int x0, out int y0, out int sl0);
    if (ScatterId.Pack(f0, l0, x0, y0, sl0, false) != fwd[0].Id) return "scatter.verify FAIL: base id round-trip";
    ulong pid = ScatterId.Pack(f0, l0, x0, y0, sl0, true);
    ScatterId.Unpack(pid, out int f1, out int l1, out int x1, out int y1, out int sl1);
    if (!ScatterId.IsPlayer(pid) || f1 != f0 || l1 != l0 || x1 != x0 || y1 != y0 || sl1 != sl0)
        return "scatter.verify FAIL: player id round-trip";

    sw.Stop();
    string status = statsF.CornerStraddle ? "PASS_WITH_KNOWN_CORNER_GAP" : "PASS";
    return $"scatter.verify {status}: {fwd.Count} instances — unique, order-independent, transform-stable, " +
           $"region-independent, id+player round-trip (candidates {statsF.Candidates}, {sw.ElapsedMilliseconds} ms)";
}

[ConsoleCommand("profile", "Density bins at face center/edge/corner (face 0); fails on corner straddle.", MonoTargetType.Registry)]
string ProfileCmd(float? regionMeters = null)
{
    if (!_configured) return "scatter: not configured (generate a planet first)";
    if (!TryPrepDiagnostic(regionMeters, 60f, 5f, 200f, ScatterId.MaxLevel, out float region, out string err)) return err;
    float scale = FaceSpaceCellRangeBuilder.GetUniformWorldScale(_planetTransform);
    (string name, Vector2 uv)[] anchors =
        { ("center", new Vector2(0.5f, 0.5f)), ("edge", new Vector2(0.985f, 0.5f)), ("corner", new Vector2(0.985f, 0.985f)) };
    var sb = new System.Text.StringBuilder();
    var buf = new List<ScatterInstance>(8192);
    bool anyCorner = false;
    foreach (var a in anchors)
    {
        Vector3 dir = FaceSpaceCellRangeBuilder.CubeFaceToUnitSphere(0, a.uv);
        Vector3 worldDir = _planetTransform.TransformDirection(dir).normalized;
        if (!_surface.TryGetSurfaceRadius(worldDir, out float wr) || wr <= 0f) { sb.AppendLine($"  {a.name}: no surface"); continue; }
        Vector3 obs = _planetTransform.TransformPoint(dir * (wr / Mathf.Max(scale, 1e-4f)) * 1.001f);
        buf.Clear();
        GatherCore(obs, region, ScatterId.MaxLevel, buf, false, out var st);
        anyCorner |= st.CornerStraddle;
        sb.AppendLine($"  {a.name}: candidates {st.Candidates}, accepted {st.Accepted}{(st.CornerStraddle ? " [CORNER STRADDLE]" : "")}");
    }
    return (anyCorner ? "scatter.profile (corner straddle = expected SP1 gap, not a failure):\n" : "scatter.profile:\n") + sb.ToString().TrimEnd();
}
```

- [ ] **Step 2: Unity import (Bryan)** then build `ProceduralPlanets.Planet.csproj`. Expected `Build succeeded`.
- [ ] **Step 3:** `graphify update .`
- [ ] **Step 4: Commit** — `git add Assets/Scripts/Planet/Scatter && git commit -m "feat(scatter): registry scatter.verify + scatter.count with real proof"`
- [ ] **Step 5: Runtime proof (Bryan)** — play, generate, console:
  - `scatter.count` in a forest/grassland view → nonzero per-prototype counts + candidate/ms.
  - `scatter.verify` → `PASS`.
  - `scatter.profile` → center/edge/corner bins; no `CORNER STRADDLE`.
  - `scatter.count 50` mid-biome vs at a biome border → target prototype's count drops at the border (falloff visible).

## Self-review (coverage)

Round-1 blockers SP1-01…13 → resolved as in v2 (see review record). Round-2 (F):
F01 Task 1 embedded. F02 `HasOceans` → Task 5 Step 3. F03 local-dir overload +
world-rotation emit → Task 5 Step 1/2. F04 zero-fade cutoff → Task 4. F05 derived
`MaxLevel` + fail-loud level + Pack asserts → Task 1/3. F06 real `verify` +
`profile` (GatherCore) → Task 6. F07 Unity-import build caveat → Global + Task
build steps. F08 DTO `Validate` → Task 2. F09 surface-anchor ROI → Task 5. F10
dead field/param removed → Task 4/5. Round-3 (R): R3-01 Configure past last await
→ Task 5 Step 3. R3-02 grass area formula + `CeilToInt` → Task 3. R3-03 corner =
expected gap → Task 6 + STOP. R3-04 unconditional Pack validation → Task 1. R3-05
import-only builds → Global + Tasks 2-4. R3-06 finite DTO + empty-library policy →
Task 2. R3-07 public `Gather` guards → Task 5. R3-08 audit tags / commit discipline
→ Global note.

## STOP conditions

- `FaceSpaceRangeResult.Count` / `FaceSpaceCell` field names, or
  `IBiomeProvider.EvaluateBiome`'s `(Vector3, float)` signature, differ from the
  plan's usage → stop, match the real symbols.
- The `dotnet build` reports success but the new scatter path is absent from
  `ProceduralPlanets.Planet.csproj` (Unity not yet imported) → stop; the build is
  inconclusive (F07).
- `scatter.verify` FAILs twice after focused fixes → stop; the determinism bug is
  in the hash/level/jitter/order path — re-examine before more edits.
- Any prototype's fixed level would exceed `ScatterId.MaxLevel` → `LevelForSpacing`
  throws by design; increase that prototype's spacing (don't raise the budget).
- `scatter.profile`/`verify` report corner straddle → **expected**: SP1 is
  deliberately corner-incomplete (design's shared-infra note; Bryan's scope call
  2026-07-22). Report it, don't fail — but never claim complete cube-corner coverage.

---

## Review record

v1 of this plan (the first task-by-task draft) and Codex's audit that BLOCKED it
were the prior contents of this file; writing v2 replaced them in place. v2
resolves every Codex finding — the mapping is in the Self-review above
(SP1-01…SP1-13 → task). The raw v1 code was superseded (it carried the 7
blockers) and is not re-pasted. Codex's full audit text lives in this
conversation's history and can be re-attached here on request if a standalone
record is wanted; the design-level fixes from it are already committed into
`docs/design/2026-07-20-scatter-placement-system.md` (2026-07-22 amendment).

---

## Codex Re-audit — 2026-07-22

### Audit Summary

**Verdict: BLOCKED pending the Critical and High findings below.** v2 resolves
several earlier architecture decisions, but it is not yet a self-contained or
safe implementation handoff. The most consequential remaining defects are the
missing Task 1 source, use of `OceanLevel` as an ocean-presence flag, two
local/world transform errors, the zero-slope-fade rejection bug, an ID-depth
overflow path, and a verification command that does not implement the proof
required by the amended design.

This re-audit compared the plan with
`docs/design/2026-07-20-scatter-placement-system.md` and the live tree at
`d39e50f`. No source-code changes were made. The graph was used only for initial
navigation because its scoped query returned stale, unrelated material; every
finding below was revalidated against current files.

Behavior-sensitive recommendations are called out explicitly. Fixing F02, F03,
F04, and F09 changes v2's proposed output only in cases where it currently
violates the stated contract (enabled oceans at non-positive levels, rotated
planets, zero fade, or an elevated observer). F08 replaces silent invalid
authoring with an intentional boot-time failure.

### Findings

#### F01 — Task 1's required implementation is absent

**Category:** Maintainability  
**Severity:** Critical  
**Description:** The plan cannot be executed from the repository. Task 1 says to
create four foundational files "exactly as in v1," but v1 is neither embedded
nor committed, and the review record explicitly says the raw code is not
re-pasted. Every later task depends on the missing ID and hash APIs. Conversation
history is not a durable implementation dependency.  
**Evidence:** Task 1 lines 70-78 reference "v1 §Task 1 (see review record below)";
the review record at lines 638-644 says that v1 was replaced and only exists in
conversation history. `rg` finds no `ScatterId`, `ScatterHash`, or
`ScatterInteraction` implementation elsewhere in the working tree.  
**Recommendation:** Embed the final, audited code for all four primitives in
Task 1, including masks, shifts, range checks, player-bit behavior, and every
hash salt used later. Do not begin implementation until the plan is
self-contained.  
**Refactor Option:** If the primitives are committed first, replace the missing
code block with exact repository paths and a verified commit anchor. Otherwise,
delete the misleading "see review record below" indirection and keep the code in
this plan.

#### F02 — Ocean presence is derived from the wrong setting

**Category:** Bug  
**Severity:** High  
**Description:** Task 5 configures water gating with `planet.OceanLevel > 0f`.
Ocean presence is independently controlled by `PlanetDto.HasOceans`, and valid
ocean levels include zero and negative values. The default settings have oceans
enabled at level zero, so v2 disables `MinWaterClearance` in the default world.  
**Evidence:** The proposed wiring is
`_scatter.Configure(..., planet.OceanLevel > 0f)` at plan line 531.
`PlanetDto` declares `bool HasOceans` separately from `float OceanLevel`
(`Assets/Scripts/Planet/PlanetDto.cs:13-14`), while the default is
`HasOceans = true`, `OceanLevel = 0f`
(`Assets/Scripts/Planet/PlanetSettings.cs:36-37`). The existing grass path uses
`planet.HasOceans` (`PlanetGrassCoordinator.cs:157-161`).  
**Recommendation:** Pass `planet.HasOceans` to `ScatterField.Configure`.
Continue using `OceanLevel` only to calculate the signed sea-level radius. This
is an intended behavior correction.  
**Refactor Option:** None needed; this is a one-token source-of-truth fix, not a
reason to pass the full `PlanetDto` into `ScatterField`.

#### F03 — The local-frame contract is broken at both query and emission

**Category:** Bug  
**Severity:** High  
**Description:** The proposed vector overload copies a range-builder body that
feeds a world-space radial direction directly into local cube-face mapping. On a
rotated planet it enumerates the wrong face and cells. Separately, `TryPlace`
returns a local rotation, but `Gather` emits that quaternion unchanged as a world
rotation while only transforming the position. Instances therefore orient
incorrectly when the planet transform is rotated.  
**Evidence:** Plan lines 355-365 say to reuse the existing body. That body sets
`dir = (cameraPos - planetCenter).normalized` and immediately calls
`DirectionToFaceUv(dir, ...)`
(`FaceSpaceCellRangeBuilder.cs:70-77`). Plan lines 453-470 correctly treat
candidate `dir` and `rot` as local, but line 473 stores `rot` directly after
transforming only `posLocal`. This contradicts the coordinate contract at plan
lines 48-66.  
**Recommendation:** In the sole vector overload, convert the radial direction
with `planetTransform.InverseTransformDirection(toCamera).normalized` before
`DirectionToFaceUv`; keep the `Camera` overload as a thin delegate. Emit
`planetTransform.rotation * rot` as the world rotation. Identity-rotation
behavior remains unchanged.  
**Refactor Option:** Keep exactly one range-building implementation—the vector
overload—and delegate the `Camera` overload to it. Do not duplicate the body.

#### F04 — A valid zero slope fade rejects every candidate

**Category:** Bug  
**Severity:** High  
**Description:** `SlopeFadeDegrees` legally permits zero. At zero fade,
`MaxSlopeCos == MinSlopeCos`; Unity's `Mathf.InverseLerp(a, a, value)` returns
zero, so the following `slopeKeep <= 0f` gate rejects flat ground along with
everything else. A hard cutoff must be handled explicitly.  
**Evidence:** The authoring range includes zero at plan line 113. The two cosine
thresholds are built at lines 486-487, then passed to `Mathf.InverseLerp` and
rejected at lines 319-320.  
**Recommendation:** Extract a small pure `SlopeKeep` function. When the fade
width is zero (or the cosine interval is within epsilon), return `1f` when
`slopeCos >= MinSlopeCos`, otherwise `0f`; use `InverseLerp` only for a positive
fade interval. Cap or validate `MaxSlopeDegrees + SlopeFadeDegrees <= 90f`.
This changes only the currently broken zero-fade behavior.  
**Refactor Option:** Keep this logic inside the sampler-free placement math so
the same branch can be mirrored exactly in SP3 HLSL.

#### F05 — The ID bit budget and quadtree level limit can diverge silently

**Category:** Bug  
**Severity:** High  
**Description:** The described ID uses 24 bits per cell coordinate but allows a
5-bit level value through `ScatterId.MaxLevel`. Levels above 24 require more than
24 coordinate bits and therefore cannot be packed uniquely. At level 31,
`1 << level` also overflows a signed `int`, yielding an invalid cell width.
`LevelForSpacing` silently clamps, so the STOP condition that says to catch an
excess level can never observe the raw overflow request.  
**Evidence:** The bit budget is stated at plan lines 72-75. `CellUvWidth` uses
`1 << Clamp(level, 0, ScatterId.MaxLevel)` at line 247;
`LevelForSpacing` clamps before returning at lines 251-255; the unreachable STOP
condition is at line 632.  
**Recommendation:** Define an operational maximum placement level of 24 (the
coordinate-bit limit), validate `x`/`y` in `ScatterId.Pack`, and compute the raw
requested level before any clamp. Fail configuration loudly when it exceeds the
operational maximum. Use a shift form that cannot cross the validated bound.
Do not silently truncate stable IDs.  
**Refactor Option:** Derive `MaxPlacementLevel` from the coordinate bit count in
`ScatterId`; do not maintain an unrelated magic number in `ScatterQuadtree`.

#### F06 — `scatter.verify` does not implement the amended proof contract

**Category:** Architecture  
**Severity:** High  
**Description:** Task 6 is labeled a "real proof," but it omits region
independence, center/edge/corner membership-bin reporting, candidate count and
timing, failure on corner straddle, and an explicit true player-bit round trip.
Its set comparison is also incomplete: only the forward list is checked for
duplicates. A reverse list with one duplicated ID and one omitted ID can retain
the same count and still pass because the code only proves every reverse ID
exists in the forward map.  
**Evidence:** The required checks are explicit in the amended design at lines
249-266. The proposed command at plan lines 567-600 checks only a forward map,
accepted count, base-instance round trip, and transform equality. `Gather` turns
`UncoveredCornerStraddle` into a debug log at plan lines 433-436, so the command
cannot fail on it. Yet the self-review claims full SP1-07/SP1-10/SP1-12 coverage
at lines 619-622.  
**Recommendation:** Return an allocation-free `ScatterGatherStats` value with
candidate count, accepted count, and `UncoveredCornerStraddle`; collect
membership bins in diagnostic mode. Build unique maps for both traversal orders
and compare both key sets. Compare a small ROI with a larger same-center ROI
filtered to the small boundary. Construct and round-trip an ID with
`playerBit: true`. Query explicit face-center, edge, and corner anchors; report
candidate/accepted bins and fail if any proof gather reports corner straddle.
Print candidate count and elapsed time while preserving the 200 m ceiling.  
**Refactor Option:** Implement one `GatherCore` with a traversal-order parameter
and optional stats output. Public `Gather` and diagnostic reverse traversal call
that same core, eliminating the proposed near-duplicate `GatherReversed`.

#### F07 — Per-task builds can report false success for new Unity scripts

**Category:** Maintainability  
**Severity:** High  
**Description:** Unity-generated project files enumerate source files with
explicit `<Compile Include>` entries. Newly created scatter files are absent
until Unity imports them and regenerates the project. Several task-end
`dotnet build` checks can therefore succeed without compiling the code just
written; later `Planet.cs` references can then fail for a stale-project reason.
The generated `.csproj` must not be edited by hand.  
**Evidence:** Every task requires a Core→Planet build (plan lines 31-46), but the
current `ProceduralPlanets.Planet.csproj` contains explicit entries such as
`Planet.cs` and `FaceSpaceCellRangeBuilder.cs` and contains no Scatter entry.
The project build guidance also states that `.csproj` files may be stale until
Unity regenerates them.  
**Recommendation:** Add a required Unity import/project-regeneration checkpoint
before each build intended to cover newly created scripts, then confirm the
scatter paths appear in `ProceduralPlanets.Planet.csproj` before trusting the
result. If the exact pinned editor is unavailable, mark the compile check
inconclusive and stop; never hand-edit the generated project. Only the Planet
assembly needs a dotnet build here because SP1 does not modify Core.  
**Refactor Option:** Replace the repeated Core→Planet pair with the smallest
valid check: Unity import/compile plus a serial Planet build after each coherent
Planet task.

#### F08 — DTO conversion permits invalid ranges and transforms

**Category:** Bug  
**Severity:** Medium  
**Description:** The DTO path says it "fails loud," but it validates only null
entries and slot IDs. It accepts `HasMinAltitude && HasMaxAltitude` with
`MinAltitudeMeters > MaxAltitudeMeters` (permanent rejection), inverted or
non-positive scale ranges (negative/mirrored or zero transforms), invalid enum
values, and slope fade beyond the physical 90-degree range. Inspector attributes
are editor hints, not runtime invariants.  
**Evidence:** `ScatterPrototypeDto.From` at plan lines 166-174 clamps a subset of
values but passes altitude bounds, `ScaleRange`, biome, and slope maximum
through. Library validation at lines 179-198 checks only prototypes and slots.  
**Recommendation:** Add one small `Validate(ScatterPrototype)` method called by
`From`. Throw actionable `InvalidOperationException`s for contradictory altitude
bounds, non-finite values, non-positive/inverted scales, undefined biome values,
and invalid slope/fade combinations. Decide explicitly whether an empty library
is allowed; the current runtime proof requires at least one prototype. This is
an intentional fail-fast behavior change for invalid authoring only.  
**Refactor Option:** Keep validation beside DTO conversion; do not add a generic
validation framework.

#### F09 — Exact ROI clipping uses observer altitude instead of the surface anchor

**Category:** Bug  
**Severity:** Medium  
**Description:** The shared range builder covers a disc around the camera's
surface anchor, but the plan's exact clip measures candidate distance from the
camera's world position. Raising the camera shrinks the surface footprint and
can make the default 60 m proof gather empty once observer altitude approaches
the requested radius. The same surface location therefore produces different
sets solely from observer altitude, undermining the stated disc and region-
independence semantics.  
**Evidence:** `FaceSpaceCellRangeBuilder` documents a disc around the camera's
surface anchor (`FaceSpaceCellRangeBuilder.cs:54-56`). Plan lines 457-459 compare
`worldPos` directly with `cameraPos`. The design calls this an exact circular ROI
at lines 195-208, not a 3D sphere around the observer.  
**Recommendation:** Resolve the observer's radial surface anchor once per
gather and use that world point for exact clipping and for small-vs-large proof
filtering. Continue passing the observer position to the range builder only to
select the radial direction. This preserves on-surface behavior and corrects
elevated-camera behavior.  
**Refactor Option:** Add a private `TryResolveSurfaceAnchor` helper; no new public
ROI abstraction is needed in SP1.

#### F10 — Dead fields and parameters obscure the parity boundary

**Category:** Complexity  
**Severity:** Low  
**Description:** `PlacementRules.BiomeBlendPower` is populated but never read
because membership power is folded into `areaKeep`. `SlopeCos` accepts
`worldDir` but never uses it. These leftovers make the supposedly small
CPU/HLSL parity surface harder to audit.  
**Evidence:** Plan lines 292-303 and 482-492 retain `BiomeBlendPower`, while
`TryPlace` uses no such field. `worldDir` is passed at line 464 and declared at
line 503 but is absent from the body.  
**Recommendation:** Delete the unused field and parameter. Name the combined
acceptance input for what it is—such as `densityKeep`—instead of overloading
`areaKeep` after biome membership has been multiplied into it.  
**Refactor Option:** Keep the sampler-free function's input list minimal and
explicit; do not introduce another rules object or abstraction.

### Refactoring Plan

1. Make Task 1 self-contained. Define the ID/hash code, derive the operational
   level limit from the coordinate bit count, and add fail-loud pack validation.
2. Strengthen DTO conversion with the focused prototype validation from F08;
   create the library assets only after they pass those invariants.
3. Correct Task 5 wiring to use `HasOceans`, then fix the range-builder local
   direction and convert emitted rotations to world space.
4. Resolve a surface anchor once per gather and use it consistently for the
   exact ROI. Preserve caller-owned buffers and main-thread semantics.
5. Put the zero-fade hard cutoff in the pure placement function and remove the
   dead placement inputs. This keeps the future HLSL mirror small.
6. Replace separate forward/reverse implementations with one `GatherCore` plus
   traversal order and value-type stats. Implement every proof listed in F06.
7. After each coherent script task, let Unity import and regenerate its project,
   confirm the new files are included, and run the Planet build serially. Run
   `graphify update .` after code changes.
8. Run `scatter.verify` and `scatter.count` in a fresh play session. Treat any
   corner-straddle result as a deliberate failure until the shared builder is
   fixed or the design is explicitly changed.

Functionality-preservation note: identity-transform, positive-fade, on-surface,
valid-settings output should remain unchanged. Ocean gating, rotated transforms,
zero-fade slope behavior, elevated-observer ROI behavior, and invalid authoring
are the explicitly behavior-changing corrections described above.

### Questions for the User

None required to revise the plan. The current design and live settings APIs
resolve the remaining behavior choices.

---

## Codex Re-review — 2026-07-22 (v3)

### Audit Summary

**Findings only — no product code changed.** This pass reconciles v3 against the
amended scatter design, the live tree at `d39e50f`, and the preceding F01-F10
audit. The prior BLOCKED verdict is superseded by this section.

**Verdict: BLOCKED.** v3 fully resolves F01-F04, F09, and F10, and partially
resolves F05-F08. Four High, three Medium, and one Low finding remain. The
highest risks are a configured scatter service surviving a canceled generation,
density math that does not mirror the existing grass area correction, a corner
proof that cannot satisfy its own exit check, and release builds silently masking
invalid stable IDs.

The current Graphify query returned unrelated older design nodes, so it was used
only as a stale navigation signal. Every claim below was checked against the
current plan and source. The consolidated repository audit adds no conflicting
scatter requirement; its relevant F09 independently recommends deleting the
disabled `GrassClumpScatter`, which SP1 does not consume.

### What Came Back Clean

- Task 1 is now self-contained; the bit layout, hash, player bit, and instance
  data are embedded.
- Ocean presence now uses `PlanetDto.HasOceans`.
- The range query converts the observer direction to planet-local space, and
  accepted rotations are converted back to world space.
- Zero slope fade has an explicit hard cutoff.
- Forward/reverse traversal now uses one gather core and compares unique maps;
  region filtering and a true player-bit round trip are present.
- The elevated-observer ROI now clips around a resolved surface anchor.
- The unused biome-power rules field and slope `worldDir` parameter are gone.

### Findings

#### R3-01 — Configure scatter only after the last cancellable generation step

**Category:** Bug  
**Severity:** High  
**Description:** Task 5 inserts `_scatter.Configure` immediately after the
generated radii are assigned, but `GeneratePlanetAsync` still performs a
cancellable next-frame await afterward. Cancellation in that window leaves
`_configured = true` even though generation did not reach its success event,
contradicting `ScatterField.Configure`'s own “successful generation” contract.  
**Evidence:** Plan lines 547-561 say configuration represents a successful
generation; lines 698-705 place the call after `_lastSeaLevelRadius` assignment.
The live sequence continues with `await Awaitable.NextFrameAsync(ct)` before
`PlanetGeneratedEvent` (`Assets/Scripts/Planet/Planet.cs:320-328`).  
**Impact:** After a late cancellation, console commands or future renderers can
query a scatter field associated with a generation that officially failed and
never published readiness.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Keep `Reset` at initialization start, but move
`_scatter.Configure(...)` to immediately after the final cancellable await and
immediately before `PlanetGeneratedEvent`. If cancellable work is later added
after configuration, reset the field in the generation failure path.  
**Refactor Option:** None. Moving one call fixes the ownership boundary.  
**Behavior note:** Preserving for successful generations; canceled generations
correctly remain unavailable.

#### R3-02 — Mirror the grass area formula and choose a level that can meet spacing

**Category:** Bug  
**Severity:** High  
**Description:** The design requires the existing grass cube-face area-keep
probability, but Task 3 squares the smaller of two finite-difference linear
scales. That is not the cube-to-sphere area Jacobian. It keeps about `0.25` at a
face-edge midpoint where the grass formula keeps about `0.354`. Separately,
rounding the logarithmic level downward can create cells larger than the target
spacing; one-candidate-per-cell placement cannot recover that missing density
because probability caps at one.  
**Evidence:** Plan lines 391-400 use `RoundToInt`; lines 413-418 use
`ComputeMetersPerUV(...); cellWorld * cellWorld`. The existing grass authority is
`CubeFaceAreaKeep` at
`Assets/Resources/GrassNearFieldPlace.compute:112-118`, which evaluates
`(1 + dot(signedUv, signedUv))^-1.5`. The design explicitly calls for that grass
area probability at `docs/design/2026-07-20-scatter-placement-system.md:73-81,
323-326`.  
**Impact:** Requested spacing can be under-dense by nearly half for ratios just
below a rounding boundary, and density varies incorrectly across face centers,
edges, and corners. A future SP3 mirror based on the existing grass formula would
also disagree with the SP1 CPU authority.  
**Effort:** S  
**Fix Risk:** MED  
**Confidence:** HIGH  
**Recommendation:** Use `CeilToInt(log2(span / spacing))` so the selected cell is
never larger than the target. Compute a center-level keep factor from
`(2 * planetWorldRadius * cellUvWidth / spacing)^2`, then multiply it by the
exact grass distortion term
`Pow(1 + (faceUv * 2 - Vector2.one).sqrMagnitude, -1.5f)`. Delete the
finite-difference approximation from `ScatterQuadtree.AreaKeep`.  
**Refactor Option:** Keep this as one small pure C# function whose expression can
be copied verbatim to the later HLSL mirror; no shared cross-language framework
is warranted.  
**Behavior note:** Changes proposed instance counts to satisfy the documented
spacing and uniform-density contract.

#### R3-03 — Resolve the corner contradiction before calling Task 6 a proof

**Category:** Architecture  
**Severity:** High  
**Description:** `scatter.profile` is required to report membership bins at a
true corner and pass without a corner straddle, while the shared builder
deliberately does not cover three-face corners. The chosen UV of `(0.985,
0.985)` is merely near a corner: on small/default planets the 60 m ROI reaches
the corner and must fail; on sufficiently large planets it can pass without
testing the corner at all. The command also reports only aggregate candidates
and accepted instances—not counts by membership bin—and `scatter.verify` still
does not report elapsed time.  
**Evidence:** Plan lines 796-818 define the near-corner anchors and aggregate
output; lines 825-829 require `scatter.profile` with no straddle. STOP line
853-854 blocks on the same flag. The design requires membership bins and a
corner failure at
`docs/design/2026-07-20-scatter-placement-system.md:249-266`. The live builder
explicitly says corner coverage is not handled and sets the flag at
`FaceSpaceCellRangeBuilder.cs:116-120,277-284`.  
**Impact:** The runtime gate either fails by design or passes without exercising
the promised geometry. It cannot prove uniform density or complete corner
coverage, so SP1 cannot honestly become the reference authority for later GPU
placement.  
**Effort:** M if the shared corner gap is fixed; S only to make the plan admit an
expected incomplete result  
**Fix Risk:** MED  
**Confidence:** HIGH  
**Recommendation:** Choose one explicit path before implementation. Preferred:
make three-face corner coverage in `FaceSpaceCellRangeBuilder` a prerequisite,
use an actual corner anchor, and then require a passing profile. Otherwise mark
corner coverage as an expected SP1 failure and do not claim the slice complete.
Add fixed membership buckets to diagnostic stats and print candidate/accepted
counts per bucket at each anchor; time `scatter.verify` as the design requires.  
**Refactor Option:** Extend the shared builder once so grass and scatter use the
same corner topology. Do not add a scatter-only seam workaround.  
**Behavior note:** A shared corner fix changes grass/scatter coverage near cube
corners and therefore requires same-seed numeric checks plus grass capture review.

#### R3-04 — Stable-ID validation disappears from release builds

**Category:** Bug  
**Severity:** High  
**Description:** `ScatterId.Pack` claims to fail loudly, but all checks are
inside `#if UNITY_ASSERTIONS`. Release builds silently mask invalid level,
coordinate, and slot values into another ID. Face values are not checked even in
assert-enabled builds, so faces 6 and 7 are accepted despite a six-face planet.  
**Evidence:** Plan lines 116-153 define the contract and assertion-only checks;
the return expression masks every field. The self-review at lines 833-838 marks
F05 resolved by “Pack asserts,” but persistence correctness is needed in shipped
players, not only development builds.  
**Impact:** An invalid caller can create an ID collision that rebinds future
chop/collect persistence to the wrong object—the exact failure stable slot IDs
are intended to prevent.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Replace conditional assertions with unconditional argument
range checks that throw `ArgumentOutOfRangeException`, including `face` in
`0..5`. Keep the masks as packing mechanics, not validation.  
**Refactor Option:** None. A few guards in the one public pack function are the
smallest safe boundary.  
**Behavior note:** Valid placement is unchanged; invalid calls fail instead of
silently corrupting identity.

#### R3-05 — Tasks 2-4 still build before their new scripts are imported

**Category:** Maintainability  
**Severity:** Medium  
**Description:** The global F07 caveat is correct, but the executable task steps
do not consistently apply it. Tasks 2, 3, and 4 create scripts and then request
an old “Core→Planet” build without an explicit Unity import or project-entry
check. The plan header also still says every task builds Core even though the
global gate correctly says SP1 touches only Planet.  
**Evidence:** Plan lines 3-4 conflict with lines 43-53. Task 2 builds at line 368
before its editor-asset step; Tasks 3 and 4 build at lines 423 and 489. Only Tasks
1, 5, and 6 explicitly require Unity import.  
**Impact:** An executor can commit uncompiled source after a false-green build,
or stop on a stale-project failure that is unrelated to the code.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Either add “Unity import → confirm `.csproj` entry → Planet
build” to every new-script task, or—preferably—combine the small Tasks 1-4 script
creation into one import/build checkpoint and avoid committing uncompiled
intermediate slices. Remove all remaining Core-build wording.  
**Refactor Option:** The combined checkpoint is the Ponytail option: fewer editor
round trips and one meaningful compile gate.  
**Behavior note:** Preserving; this changes only execution discipline.

#### R3-06 — DTO validation still permits non-finite runtime values

**Category:** Bug  
**Severity:** Medium  
**Description:** F08 added useful range checks, but only spacing is tested for
NaN/infinity. Infinite scale, altitude, water clearance, weight, or blend power
can still enter the immutable DTO; NaN slope and altitude values can bypass
comparisons. The factory also silently converts a null prototype array to an
empty library while the proof treats empty output as inconclusive.  
**Evidence:** Plan lines 286-314 validate biome, range ordering, scale signs,
slope sum, and spacing finiteness. The remaining float fields are copied or
clamped at lines 289-297 without finite checks. Lines 319-323 turn a null array
into an empty DTO.  
**Impact:** Bad serialized or overridden settings can yield non-finite transforms,
permanent rejection, or acceptance math that cannot be reasoned about, instead
of the promised boot-time failure.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Add one local `Finite(float)` helper and validate every
stored float before constructing the DTO. Validate the actual authoring bounds
instead of relying on `Mathf.Max` to sanitize them. State explicitly that an
empty library is valid, or throw for null/empty input if SP1 requires populated
proof assets.  
**Refactor Option:** Keep validation in `ScatterPrototypeDto.From`; no validation
framework is needed.  
**Behavior note:** Valid settings are unchanged; invalid authoring fails earlier.

#### R3-07 — The public gather API accepts contradictory geometry inputs

**Category:** Bug  
**Severity:** Medium  
**Description:** Console commands clamp their arguments, but public `Gather`
forwards arbitrary radius, level, and buffer values. A negative radius is
squared into a positive exact-clip radius while the range builder receives the
negative value, producing a range and clip that describe different regions. A
null buffer fails only if an instance is eventually accepted.  
**Evidence:** Plan lines 568-583 expose and forward the public arguments without
validation. Command-only clamping appears later at lines 726-727 and 748.  
**Impact:** Future SP2/SP5 callers can get silent under-coverage or a late,
data-dependent null exception from the service boundary.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** At the public boundary, throw for a null buffer, require a
finite positive radius, and require `maxLevel` in `0..ScatterId.MaxLevel`.
Diagnostic commands already satisfy those preconditions.  
**Refactor Option:** None; three guards are enough.  
**Behavior note:** Valid callers are unchanged; invalid input fails predictably.

#### R3-08 — Audit-history comments and commit instructions conflict with project rules

**Category:** Style  
**Severity:** Low  
**Description:** The source snippets carry `// F03`, `// F04`, `// F05`, `// F08`,
`// F09`, and `// F12` change-history labels even though the plan bans
change-history comments. Every task also mandates a conventional-commit subject,
while project change control says commits occur only when Bryan asks and uses
imperative project subjects rather than `feat(scope): ...`.  
**Evidence:** The comment rule is at plan line 42; tagged source comments occur
throughout Tasks 2-6 (for example lines 300, 396, 451, 571, 580, 619, 634, and
726). Commit commands occur at lines 200, 370, 424, 490, 710, and 824.  
**Impact:** Audit IDs sediment into production source and the handoff can perform
repository mutations outside the execution turn's explicit authority.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Remove audit IDs from code comments while retaining only the
non-obvious invariant. Change each commit step to a checkpoint that says “commit
only if Bryan explicitly authorizes it in the execution turn,” using the
repository's imperative subject style when authorized.  
**Refactor Option:** None. Delete prose rather than adding a commit abstraction.  
**Behavior note:** Product behavior is unchanged.

### Refactoring Plan

1. Fix the plan's correctness foundation first: unconditional ID validation
   (R3-04), `CeilToInt` level selection, and the exact grass area formula
   (R3-02).
2. Move scatter configuration past the final cancellable await (R3-01), then add
   the public gather guards and complete finite DTO validation (R3-06/R3-07).
3. Decide the corner scope (R3-03). If it joins SP1, fix the shared builder before
   implementing `scatter.profile`, and capture same-seed grass evidence because
   shared corner coverage can move pixels.
4. Complete the proof: exact corner anchor after the shared fix, membership
   buckets per anchor, candidate/accepted counts, elapsed time, exact ID sets,
   region independence, and base/player round trips.
5. Collapse or correct the import/build checkpoints and strip audit-history
   comments. Do not commit unless the execution request explicitly authorizes it.
6. After implementation, let Unity regenerate the Planet project, run the serial
   Planet build, then run a fresh play session with `scatter.count`,
   `scatter.verify`, and `scatter.profile`. Any shared grass change also requires
   the project's before/after grass capture gate and Bryan's visual review.

### Prior Audit Reconciliation

| Prior finding | Status | v3 evidence / remaining destination |
|---|---|---|
| F01 missing Task 1 source | RESOLVED | Task 1 embeds all four primitives. |
| F02 wrong ocean flag | RESOLVED | Task 5 passes `planet.HasOceans`. |
| F03 local/world transform mismatch | RESOLVED | Local face query and world rotation are both specified. |
| F04 zero slope fade | RESOLVED | `SlopeKeep` has an explicit hard cutoff. |
| F05 ID/level budget | PARTIAL | Level 24 and raw-level failure landed; release-safe Pack validation remains R3-04. |
| F06 incomplete proof | PARTIAL | Exact maps, region check, and player bit landed; membership bins, timing, and achievable corner proof remain R3-03. |
| F07 false-green builds | PARTIAL | The global caveat is correct; Tasks 2-4 remain inconsistent in R3-05. |
| F08 DTO validation | PARTIAL | Basic ranges landed; non-finite inputs and empty-library policy remain R3-06. |
| F09 elevated-observer ROI | RESOLVED | Gather clips against a sampled surface anchor. |
| F10 dead placement inputs | RESOLVED | Both unused inputs were removed. |

### Questions for the User

1. Should SP1 absorb the shared three-face corner fix as a prerequisite, or
   deliberately remain incomplete at cube corners? **Recommendation:** include the
   shared fix; otherwise SP1 cannot satisfy its own reference-authority and profile
   exit checks.

---

## Codex Re-review — 2026-07-22 (v4)

### Audit Summary

**Findings only — no code changed.** This appendix re-reviews v4 against the
live tree at `d39e50f`, the current scatter design, the consolidated repository
audit, and R3-01 through R3-08. Only this plan appendix was added; product source
remains untouched.

**Verdict: BLOCKED.** v4 resolves R3-01, R3-02, R3-04, R3-05, and R3-07.
R3-03, R3-06, and R3-08 remain partial. Three High, one Medium, and two Low
findings remain. The blockers are now narrower: the chosen corner-gap policy is
not reconciled with the design or its own exit checks, the synchronous proof
commands can still schedule hundreds of millions of candidates, and settings
overrides bypass the stable-slot/numeric validation performed by the SO factory.

Graphify was used for initial navigation, but its query returned unrelated older
design nodes, so every claim below was checked in the live files. There is still
no `Assets/Scripts/Planet/Scatter/` source directory to compile or run; no build,
Unity import, or runtime proof was attempted during this findings-only review.

### What Came Back Clean

- `ScatterField.Configure` now sits after the last cancellable generation await
  and before `PlanetGeneratedEvent`.
- Level selection uses `CeilToInt`, and `AreaKeep` mirrors the grass
  `(1 + dot(signedUv, signedUv))^-1.5` expression.
- `ScatterId.Pack` now validates face, level, coordinates, and slot in every
  build instead of relying on `UNITY_ASSERTIONS`.
- Every new-script task now requires Unity import and confirmation that the
  regenerated Planet project includes the new file before trusting its build.
- The DTO factory now rejects non-finite floats and explicitly treats an empty
  library as valid.
- Public `Gather` rejects null buffers, non-finite/non-positive radii, and invalid
  levels.
- The consolidated repository audit adds no competing scatter implementation;
  its only direct overlap remains deletion of disabled `GrassClumpScatter`.

### Findings

#### R4-01 — Reconcile the chosen corner gap with the proof contract

**Category:** Architecture  
**Severity:** High  
**Description:** v4 records Bryan's decision that SP1 remains corner-incomplete,
but the design and the executable acceptance steps still require corner straddle
to fail or disappear. The proposed `profile` command describes itself as failing,
returns a non-failure message instead, and the runtime checklist then requires
`no CORNER STRADDLE`. It also calls aggregate anchor totals “density bins” while
never recording the required membership buckets.  
**Evidence:** The chosen scope is recorded at
`docs/plans/2026-07-20-scatter-placement-sp1.md:11-18` and the STOP rule correctly
treats the gap as expected at `:903-905`. The command description says it fails at
`:841`, its implementation reports “not a failure” at `:861-863`, and the exit
check requires no straddle at `:870-874`. The current design still requires a
failure and membership-bin counts at
`docs/design/2026-07-20-scatter-placement-system.md:204-207,261-265,327-330`.
`ProfileCmd` prints only aggregate candidate/accepted totals (`plan:847-863`).  
**Impact:** Two executors can follow the same plan and reach opposite completion
verdicts. A plain `PASS` can certify determinism only for the covered subset while
appearing to certify full ROI coverage, and the biome-border density claim has no
membership-distribution evidence.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Honor the recorded scope decision consistently. Amend the
design's three stale corner statements, change the command description and exit
check to expect/report the known gap, and return a status such as
`PASS_WITH_KNOWN_CORNER_GAP` rather than an unqualified `PASS` when the flag is
set. Add fixed membership buckets with candidate/accepted counts at each anchor;
that proof remains useful even though full corner coverage is deferred.  
**Refactor Option:** Keep one `ScatterGatherStats` value and add fixed-size bin
counters to it. No diagnostic framework or scatter-only topology workaround is
needed.  
**Behavior note:** Preserves the chosen SP1 corner behavior; it makes the proof
and documentation honest about that behavior.

#### R4-02 — Bound diagnostic work by candidates, not radius alone

**Category:** Bug  
**Severity:** High  
**Description:** The synchronous console commands claim a radius ceiling prevents
hitches, but prototype spacing is allowed down to `0.05 m`. Since the chosen cell
is no larger than spacing, a single `400 m` count can enumerate at least roughly
`(2 × 400 / 0.05)^2 = 256 million` square-range cells near a face center, before
neighbor ranges or additional prototypes. Exact ROI clipping occurs only after a
surface sample. `CountCmd` also calls `GatherCore` directly, bypassing the public
level validation; `Mathf.Clamp` does not reject a NaN radius.  
**Evidence:** The authoring minimum is
`docs/plans/2026-07-20-scatter-placement-sp1.md:234`; `LevelForSpacing` guarantees
cell width is never larger than spacing at `:411-426`. The inner loop visits every
range cell and samples the surface before clipping (`:631-661`). Diagnostic caps
and direct core calls are at `:763-771,784-796,842-859`; `maxLevel` is accepted
without validation at `:768`. The design's no-hitch claim is
`docs/design/2026-07-20-scatter-placement-system.md:263-265`.  
**Impact:** A valid prototype or mistyped console value can freeze or exhaust the
Unity editor on the main thread, so the proof tool can prevent the proof run from
finishing.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Preflight diagnostic work with `long` candidate arithmetic
over all selected prototype ranges and return `INCONCLUSIVE: candidate budget
exceeded` before allocating output lists or sampling. Validate diagnostic radius
and level with the same guards as public `Gather`. Keep public gather exact; do
not silently truncate its results.  
**Refactor Option:** Extract one small argument validator and one candidate-count
preflight shared by `count`, `verify`, and `profile`. Add no jobs/Burst path until
measured normal prop settings require it.  
**Behavior note:** Extreme diagnostic requests return early instead of hanging;
normal placement and valid proof runs are unchanged.

#### R4-03 — Validate the final DTO after world overrides

**Category:** Bug  
**Severity:** High  
**Description:** All scatter invariants are enforced only while converting the
default `ScriptableObject`. World settings overrides are applied after that
conversion and replace the DTO without domain validation. `ScatterField.Configure`
then trusts the overridden array, prototypes, slots, and floats. Even the SO path
still silently normalizes finite out-of-range values despite its “fail loud”
contract: sub-`0.05` spacing, non-positive blend power/weight, negative slope fade,
and negative water clearance are clamped rather than rejected.  
**Evidence:** Conversion and clamping are at
`docs/plans/2026-07-20-scatter-placement-sp1.md:302-335`; library uniqueness checks
exist only in `ScatterLibraryDto.From` at `:338-365`. `Configure` reads and uses the
final DTO without validating it at `:576-590`. The live boot sequence applies
overrides before required-type validation/freeze at
`Assets/Scripts/Core/Services/SceneBootstrap.cs:91-95`, while
`Assets/Scripts/Core/Services/SettingsService.cs:43-57,59-75` only replaces the
object and checks that its type exists.  
**Impact:** A saved-world override can introduce duplicate persistence slots,
null prototypes, invalid enums, or non-finite placement values. Failures then
surface late during generation/gather—or duplicate stable identities—rather than
as the promised boot-time validation error.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Put all prototype and library invariants in one DTO-level
validation routine. Call it from `From` and again in `Configure` after overrides
have been applied. Validate the actual authored ranges instead of clamping invalid
finite values; retain clamping only inside runtime math as numerical defense.  
**Refactor Option:** One `ScatterLibraryDto.Validate()` that delegates to
`ScatterPrototypeDto.Validate()` is sufficient. Do not add a general settings
validation framework for one domain.  
**Behavior note:** Valid assets/overrides are unchanged; invalid settings fail
earlier and more clearly.

#### R4-04 — Treat the shared rotated-grass correction as a visual behavior change

**Category:** Architecture  
**Severity:** Medium  
**Description:** Task 5 changes the existing `Camera` overload used by grass to
route through the new local-frame implementation. This is a correct fix for a
rotated planet, but it is not true that grass is unaffected: identity transforms
are unchanged, while non-identity rotations enumerate different—and now correct—
faces. The validation plan contains only scatter commands.  
**Evidence:** The shared replacement and “grass is unaffected” claim are at
`docs/plans/2026-07-20-scatter-placement-sp1.md:530-542`. The live builder identifies
itself as the grass dispatch helper and currently maps the world radial direction
directly (`Assets/Scripts/Planet/Grass/FaceSpaceCellRangeBuilder.cs:3-9,58-77`).
The runtime checks at `plan:870-874` contain no existing-grass evidence.  
**Impact:** SP1 can move visible grass on rotated planets without the before/after
evidence required for a shared visual behavior change.  
**Effort:** S  
**Fix Risk:** MED  
**Confidence:** HIGH  
**Recommendation:** Keep the shared correction, explicitly label it behavior
changing for rotated planets, and capture a same-seed/same-pose Grass before/after
pair with a non-identity planet rotation before Task 5 lands. Bryan's visual
review is the completion gate.  
**Refactor Option:** The single delegated implementation remains the cleanest
design; duplicating an old world-frame path only to preserve its bug is worse.  
**Behavior note:** Changes grass coverage on rotated planets and therefore needs
explicit approval/evidence.

#### R4-05 — Finish removing audit-history and conflicting commit instructions

**Category:** Style  
**Severity:** Low  
**Description:** The global rule now says audit tags are not source comments and
commit subjects must use imperative project style, but executable task snippets
still contain an `(F06)` source comment and every checkpoint still supplies a
`feat(scatter): ...` subject. Task 1 also says “editor asserts” although `Pack`
now uses unconditional exceptions.  
**Evidence:** The stale text appears at
`docs/plans/2026-07-20-scatter-placement-sp1.md:127,216,393,453,519,542,607-608,751,869`;
the controlling rule is at `:45-58`.  
**Impact:** An executor copying the plan literally violates the same source and
commit conventions the plan says it resolved, and the review ledger overstates
R3-08's status.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Remove audit IDs from code snippets, replace “editor asserts”
with “unconditional argument checks,” and either delete the commit commands or
show conditional imperative examples without conventional prefixes.  
**Refactor Option:** None; delete contradictory prose.  
**Behavior note:** Preserving.

#### R4-06 — Remove the unused logger dependency

**Category:** Complexity  
**Severity:** Low  
**Description:** `ScatterField` stores an injected `ILogger` but never reads it.
This is speculative coupling and a dead field in a new service.  
**Evidence:** The only logger references in the proposed class are declaration,
constructor parameter, and assignment at
`docs/plans/2026-07-20-scatter-placement-sp1.md:556,567-572`; Planet passes it at
`:736`.  
**Impact:** The constructor advertises a dependency the class does not have and
adds noise to lifecycle wiring.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Remove `_logger`, the constructor parameter, and the `Logger`
argument at construction. Add logging later only when a real message exists.  
**Refactor Option:** None. Deletion is the refactor.  
**Behavior note:** Preserving.

### Refactoring Plan

1. Reconcile the selected corner-incomplete scope across the design, command
   descriptions, status text, STOP rules, and runtime checklist. Add membership
   buckets without attempting the deferred topology fix (R4-01).
2. Centralize DTO invariant validation and run it on the final post-override DTO
   before calculating levels (R4-03).
3. Add finite/range checks plus a `long` candidate-budget preflight to every
   synchronous diagnostic command (R4-02).
4. Preserve the one-implementation range-builder refactor, but add the rotated-
   planet Grass capture gate before changing its existing `Camera` caller
   (R4-04).
5. Delete the unused logger dependency and stale audit/commit prose (R4-05/R4-06).
6. After implementation, let Unity import/regenerate the project, confirm every
   scatter source is in `ProceduralPlanets.Planet.csproj`, build Planet, then run
   bounded `scatter.count`, `scatter.verify`, and `scatter.profile`. Record the
   known corner status explicitly; do not claim complete cube-corner coverage.

### Prior Audit Reconciliation

| Prior finding | Status | v4 evidence / remaining destination |
|---|---|---|
| R3-01 late-cancellation configuration | RESOLVED | Task 5 configures after the final await and before readiness publication. |
| R3-02 level/area density math | RESOLVED | Task 3 uses `CeilToInt` and the exact grass area expression. |
| R3-03 corner/proof contradiction | PARTIAL | The scope decision is recorded and timing landed; stale fail/pass requirements and missing membership bins remain R4-01. |
| R3-04 release-safe IDs | RESOLVED | `Pack` now uses unconditional face/level/coordinate/slot guards. |
| R3-05 false-green build steps | RESOLVED | Tasks 2-4 now require import, project-entry confirmation, and the Planet build. |
| R3-06 finite DTO validation | PARTIAL | Finite checks and empty-library policy landed; post-override and actual-range validation remain R4-03. |
| R3-07 public gather guards | RESOLVED | Public `Gather` validates buffer, radius, and level; diagnostic bypass is separately R4-02. |
| R3-08 comments/commit discipline | PARTIAL | The global rule landed; contradictory task text remains R4-05. |

Earlier F01-F10 statuses remain as reconciled by the v3 review, with F06 and F08
continuing through R4-01 and R4-03 respectively.

### Questions for the User

None. v4 records Bryan's corner-scope decision; the remaining work is to apply it
consistently and make the proof safe and truthful.

---

## Codex Re-review — 2026-07-22 (v5)

### Audit Summary

**Findings only — no code changed.** This pass reconciles v5 with the live tree
at `d39e50f`, the current scatter design, the consolidated repository audit, and
R4-01 through R4-06. Only this appendix was added; product source remains
untouched.

**Verdict: BLOCKED.** v5 resolves the unused logger and avoids the intended
rotated-grass behavior change. The post-override validator, diagnostic argument
guard, candidate preflight, and explicit `PASS_WITH_KNOWN_CORNER_GAP` status are
good direction, but two High, two Medium, and one Low finding remain. The current
budget is approximate and diagnostic-only, so valid public inputs can still
overflow range arithmetic while proof commands can allocate hundreds of MiB.
The corner policy also remains internally contradictory.

The Graphify query again returned unrelated older design nodes and was treated
only as stale navigation. Every finding below was checked against the live files.
There is still no `Assets/Scripts/Planet/Scatter/` source directory, so no build,
Unity import, or runtime command was available to run.

### What Came Back Clean

- `_logger` and its constructor argument are gone.
- Scatter now gets a local-frame range entry without deliberately changing the
  existing grass frame convention on rotated planets.
- `Configure` re-validates the final DTO after world overrides.
- Diagnostic radius finiteness and `maxLevel` are checked before gathering.
- Candidate arithmetic in the proposed preflight uses `long`.
- `scatter.verify` distinguishes an accepted corner gap from an unqualified pass.
- Per-membership histograms are now explicitly deferred in the design; aggregate
  center/edge/corner counts are the current accepted SP1 scope.

### Findings

#### R5-01 — Finish reconciling the accepted corner-gap policy

**Category:** Architecture  
**Severity:** High  
**Description:** The updated design now records the accepted corner gap in its
verification section, but its gather contract and risk section still say
`scatter.verify` fails loud. The plan has the same split: `VerifyCmd` returns
`PASS_WITH_KNOWN_CORNER_GAP`, while `ProfileCmd` describes itself as failing and
the runtime checklist requires no corner flag.  
**Evidence:** The accepted status is implemented at
`docs/plans/2026-07-20-scatter-placement-sp1.md:899-901` and the STOP rule treats
the flag as expected at `:966-968`. Contradictory plan text remains at `:904` and
`:933-937`. The design accepts/report the gap at
`docs/design/2026-07-20-scatter-placement-system.md:261-267` but still requires
failure at `:204-207,331-334`.  
**Impact:** The same runtime output is both a pass and a failure depending on
which paragraph an executor follows, so SP1 has no objective completion verdict.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Delete the stale fail-loud wording in both documents. Change
the profile description and checklist to “report `CORNER STRADDLE` if present;
do not fail.” Accept either `PASS` or `PASS_WITH_KNOWN_CORNER_GAP` as appropriate,
while retaining the prohibition on claiming complete corner coverage.  
**Refactor Option:** None; one policy stated once and cross-referenced is enough.  
**Behavior note:** Preserves Bryan's recorded corner-incomplete SP1 scope.

#### R5-02 — Replace the approximate diagnostic guard with one exact service budget

**Category:** Bug  
**Severity:** High  
**Description:** `TryPrepDiagnostic` estimates one center-metric square per
prototype. Actual range size depends on `ComputeMetersPerUV` at the query UV and
can include neighbor-face ranges; `verify` performs forward, reverse, and small
gathers, while `profile` performs three gathers. The fixed `2,000,000` threshold
therefore neither bounds actual work nor the lists/maps allocated by a command.
Public `Gather` bypasses the budget entirely and multiplies two grid dimensions
as `int`. With valid maximum planet radius and minimum spacing, a full-face
level-18 range has `262,144²` cells, which overflows that multiplication.  
**Evidence:** The approximate center calculation is at
`docs/plans/2026-07-20-scatter-placement-sp1.md:596,730-750`; actual enumeration
uses returned ranges and `int cells = width * height` at `:670-680`. Public
`Gather` calls the core directly at `:635-646`. The three verification passes are
at `:855-886`, and profile repeats the gather at `:913-925`. The live builder
derives range radius from location-dependent `ComputeMetersPerUV` and can return
up to five ranges
(`Assets/Scripts/Planet/Grass/FaceSpaceCellRangeBuilder.cs:45,58-83`). Valid
authoring permits `PlanetRadius = 5000` and `SpacingMeters = 0.05`
(`Assets/Scripts/Planet/PlanetSettings.cs:7`; `plan:246`).  
**Impact:** A supposedly guarded proof can still freeze or exhaust the editor;
two two-million-entry lists plus two dictionaries can consume hundreds of MiB.
A valid large public query can overflow the cell count and silently emit partial
or no results.  
**Effort:** M  
**Fix Risk:** MED  
**Confidence:** HIGH  
**Recommendation:** Add one exact preflight that calls the real range builder for
the actual observer/anchor, sums every returned `GridSize.x * GridSize.y` with
checked `long` arithmetic, and applies before any gather. Public `Gather` should
fail explicitly above the service budget and tell callers to tile the ROI; never
truncate. Diagnostics should budget the total number of passes and retained
containers, not one gather. Start with a conservative cap and raise it only from
measured elapsed time and memory at intended prop settings.  
**Refactor Option:** Reuse a single `CountCandidateCells` helper from public
`Gather`, `count`, `verify`, and each profile anchor. No jobs/Burst path is needed.  
**Behavior note:** Oversized requests fail predictably instead of hanging or
overflowing; in-budget placement remains unchanged.

#### R5-03 — Make DTO validation complete and actually single-source

**Category:** Bug  
**Severity:** Medium  
**Description:** `EnsureValid` closes the main override bypass but dereferences a
null `Prototypes` array and still accepts several invalid DTOs: undefined biome or
interaction enums, sub-minimum spacing/blend power, negative weight/fade/water
clearance, and slope ranges above 90 degrees. The SO factory separately checks
and clamps overlapping fields, and `From` repeats slot uniqueness before calling
`EnsureValid`, contradicting the “single source” comment.  
**Evidence:** SO validation/clamping is at
`docs/plans/2026-07-20-scatter-placement-sp1.md:306-339`; duplicate library checks
are at `:345-365`; final DTO validation is at `:369-394`. `Prototypes.Length` is
read without a null guard at `:376`, and the only positivity check in the compound
condition applies to spacing at `:384`.  
**Impact:** A saved override can still crash with an unhelpful null reference or
silently invert biome-border/slope behavior. Duplicated validators can drift as
new prototype fields arrive.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Construct the DTO from raw authored values, then run one
complete DTO validator for both assets and overrides. Guard the array first;
validate both enums, every documented range, slope sum, altitude ordering, slots,
and scales. Remove the duplicate slot/range checks and `Mathf.Max` sanitization
from `From`; runtime math may retain clamps only as numerical defense.  
**Refactor Option:** Keep `ScatterLibraryDto.EnsureValid()` as the sole entry and
use one private prototype-validation helper inside it. No general framework.  
**Behavior note:** Valid settings are unchanged; invalid settings fail earlier.

#### R5-04 — Prove the shared grass helper extraction is invariant

**Category:** Maintainability  
**Severity:** Medium  
**Description:** v5 avoids changing grass's coordinate convention, resolving the
behavior-change concern in R4-04. However, it still factors the existing grass
range body into a new private helper used by both overloads. That is a source
change on the live grass path, despite the claim that the overload is untouched,
and the acceptance section contains no grass invariance check.  
**Evidence:** The extraction is required at
`docs/plans/2026-07-20-scatter-placement-sp1.md:564-580`. The live class is the
grass dispatch range builder
(`Assets/Scripts/Planet/Grass/FaceSpaceCellRangeBuilder.cs:3-9,58-121`). Runtime
proof covers only scatter at `plan:933-937`.  
**Impact:** A transcription error in the helper extraction can change existing
grass candidate/range counts on every planet while all new scatter checks pass.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Capture same-seed, same-pose, same-tier Grass sidecar counts
before Task 5 and compare them after Unity import. Require exact candidate/range
count equality and zero overflow. If counts differ, stop; only then does the
visual-change gate become relevant.  
**Refactor Option:** The proposed private helper remains the correct DRY seam;
do not duplicate the range body merely to avoid validating the extraction.  
**Behavior note:** Intended to preserve grass behavior exactly.

#### R5-05 — Remove the remaining audit and commit-history residue

**Category:** Style  
**Severity:** Low  
**Description:** The file is titled v5 but its revision note begins `v4`; code
snippets still contain `R4-02`/`R4-03` history comments, all checkpoint commands
still use forbidden `feat(scatter):` subjects, and the self-review stops at R3
while still saying “Pack asserts.”  
**Evidence:** See
`docs/plans/2026-07-20-scatter-placement-sp1.md:1,11-22,220,369-371,427,487,553,596,622,730-731,814,932,939-953`.
The governing rule is at `:49-61`.  
**Impact:** Copying the plan literally creates comments and commits that violate
the plan's own conventions, and the coverage summary overstates closure.  
**Effort:** S  
**Fix Risk:** LOW  
**Confidence:** HIGH  
**Recommendation:** Rename the revision note to v5, remove audit IDs from source
snippets, replace “Pack asserts,” extend self-review through R4, and either remove
literal commit commands or use conditional imperative examples.  
**Refactor Option:** None; delete stale prose.  
**Behavior note:** Preserving.

### Refactoring Plan

1. Make the accepted corner policy identical in the gather contract, risk
   section, commands, checklist, and STOP rules (R5-01).
2. Replace the approximate diagnostic estimate with one exact, checked range
   preflight shared by every gather entry; budget total proof passes and container
   memory, and reject oversized public queries without truncation (R5-02).
3. Collapse asset and override validation into one complete DTO invariant path
   (R5-03).
4. Keep the shared post-direction range helper, but add same-seed Grass numeric
   invariance around Task 5 (R5-04).
5. Delete stale audit tags and conflicting commit/version prose (R5-05).
6. After implementation, let Unity regenerate the Planet project, build it, run
   bounded scatter commands, and record `PASS` or
   `PASS_WITH_KNOWN_CORNER_GAP` without claiming complete corner coverage.

### Prior Audit Reconciliation

| Prior finding | Status | v5 evidence / remaining destination |
|---|---|---|
| R4-01 corner/proof contract | PARTIAL | `PASS_WITH_KNOWN_CORNER_GAP` and the histogram deferral landed; stale fail/no-flag requirements remain R5-01. |
| R4-02 diagnostic work bound | PARTIAL | Finite/level guards and a `long` estimate landed; exact geometry, total proof work, memory, and public-range safety remain R5-02. |
| R4-03 final DTO validation | PARTIAL | Post-override `EnsureValid` landed; null/range/enum completeness and duplicate validation remain R5-03. |
| R4-04 rotated-grass behavior | RESOLVED | Scatter uses a separate local-frame entry; no rotated-grass behavior change is proposed. Helper-extraction invariance is separately R5-04. |
| R4-05 audit/commit residue | OPEN | Conventional subjects, audit-tag comments, and stale self-review text remain R5-05. |
| R4-06 unused logger | RESOLVED | The field, constructor argument, and Planet argument are gone. |

Earlier F- and R3-series statuses remain as reconciled by the v4 review, with
their open tails represented by the R5 findings above.

### Questions for the User

None. The remaining changes follow the decisions already recorded in v5.

---

## Codex Re-review — 2026-07-22 (v5 unchanged follow-up)

### Audit Summary

**Findings only — no code changed.**

The implementation body is still titled v5 and was still 2,052 lines before
this appendix, with the same snippets and contracts reviewed in R5. This is
therefore a regression/reconciliation pass, not a review of a new plan revision.
The review used repository HEAD `d39e50f`, the current scatter design, and the
live settings/range-builder contracts. The Graphify query did not return the
scatter scope, so every claim below was verified against the working tree.
`Assets/Scripts/Planet/Scatter/` still does not exist; no product source was
changed and no Unity import, build, or runtime proof was attempted.

**Verdict: BLOCKED — 2 High, 2 Medium, 1 Low.** No new finding was introduced,
but none of R5-01 through R5-05 has been addressed in the implementation body.

### What Came Back Clean

- The previously resolved local-frame scatter entry, post-cancellation
  configuration point, ocean-presence input, stable-ID checks, surface-anchor
  clipping, and logger removal remain intact in the plan.
- The empty-library and known-corner-gap runtime statuses remain explicit.
- No source implementation exists yet, so this pass found no source regression
  beyond the still-open plan defects below.

### Findings

#### R6-01 — The accepted corner gap still has mutually exclusive proof rules

**Category:** Architecture  
**Severity:** High  
**Description:** The selected SP1 policy is to report the known three-face
corner gap without failing, but the plan and design still also require failure
or absence of the same flag. An executor cannot satisfy both contracts, and the
runtime checklist can reject the exact accepted outcome produced by `VerifyCmd`.  
**Evidence:** The plan returns `PASS_WITH_KNOWN_CORNER_GAP` at `:899` and reports
the gap without failure at `:926`, while the profile command description says
“fails on corner straddle” at `:904` and the checklist requires no
`CORNER STRADDLE` at `:936`. The design accepts/report the gap at `:261-267` but
still says `scatter.verify` fails at `:204-207` and “fails loud” at `:331-334`.  
**Impact:** Release proof has no single pass criterion and may block a conforming
implementation or approve it under contradictory documentation.  
**Effort:** Low  
**Fix Risk:** Low  
**Confidence:** High  
**Recommendation:** Make every plan and design occurrence use the recorded
policy: report the flag, return `PASS_WITH_KNOWN_CORNER_GAP`, and never claim
complete corner coverage. Make the checklist accept either `PASS` or that
qualified status.  
**Refactor Option:** Keep one short “SP1 corner policy” paragraph and have the
commands, checklist, risks, and STOP rule refer to it.  
**Behavior note:** Documentation-only; preserves the chosen runtime behavior.

#### R6-02 — Gather work is still not bounded by the ranges it will enumerate

**Category:** Bug  
**Severity:** High  
**Description:** The diagnostic guard estimates a center-metric square, whereas
the service enumerates location-dependent primary and neighbor-face ranges.
It also budgets only one estimated traversal, not `verify`'s forward, reverse,
and smaller passes or its lists/maps/sets. Public `Gather` bypasses the guard,
and its `int` cell product can overflow for otherwise valid planet/spacing
settings and a sufficiently large finite ROI.  
**Evidence:** `CandidateBudget` and the approximation are at `:596` and
`:732-751`; actual ranges come from `BuildRangesLocal` at `:670`, followed by
`int cells = GridSize.x * GridSize.y` at `:677`. Public `Gather` calls
`GatherCore` directly at `:636-643`. The live settings allow radius 5,000
(`PlanetSettings.cs:7`), while the plan allows 0.05 m spacing (`:246`), which
can select level 18; a full-face `262144 * 262144` cell count overflows `int`.  
**Impact:** A console proof can still hitch or allocate heavily, and a public
query can silently enumerate the wrong number of cells or return incomplete
results after integer overflow.  
**Effort:** Medium  
**Fix Risk:** Medium  
**Confidence:** High  
**Recommendation:** Use the real `BuildRangesLocal` results to sum every
prototype range with checked `long` arithmetic before enumeration. Apply the
same preflight to public and diagnostic gathers, include all proof passes and
retained-container memory in the command budget, and reject oversized requests
explicitly rather than truncating them.  
**Refactor Option:** Add one small preflight helper shared by `Gather` and the
commands; no planner class or new abstraction layer is needed.  
**Behavior note:** Preserves valid bounded results; intentionally changes
oversized queries from overflow/hitch behavior to an explicit error.

#### R6-03 — Final DTO validation is still incomplete and duplicated

**Category:** Bug  
**Severity:** Medium  
**Description:** `EnsureValid()` is described as the single source of DTO
invariants, but it assumes `Prototypes` is non-null and omits several authored
limits and enum checks. `From()` separately validates and then clamps values,
so asset-created and override-created DTOs do not follow one invariant policy.  
**Evidence:** `From()` validates/clamps at `:306-338` and repeats library
slot checks at `:351-365`. `EnsureValid()` dereferences `Prototypes.Length` at
`:376` and checks only a subset at `:384-393`. It does not enforce the declared
spacing, blend-power, weight, slope/fade, water-clearance, or enum constraints
from `:246-268`, nor the slope-plus-fade ceiling enforced only by `Validate()`.  
**Impact:** A malformed override can null-reference during configuration or
enter runtime with values that authored assets would reject or silently clamp.  
**Effort:** Low  
**Fix Risk:** Low  
**Confidence:** High  
**Recommendation:** Make `EnsureValid()` null-safe and complete, including
slot uniqueness, both enums, all finite/range constraints, ordered scale and
altitude bounds, and the slope-plus-fade ceiling. Convert raw values without
clamping and call that validator once so invalid input always fails loud.  
**Refactor Option:** Delete `ScatterPrototypeDto.Validate()` and the duplicate
library checks after moving their rules into the DTO validation path.  
**Behavior note:** Preserves valid settings; invalid settings will consistently
throw instead of being clamped or accepted.

#### R6-04 — Shared grass range extraction still lacks an invariance gate

**Category:** Maintainability  
**Severity:** Medium  
**Description:** The plan says the grass overload is untouched while also
requiring its post-direction body to be extracted into a helper used by both
overloads. That is a sensible DRY change, but it edits grass's live range path
and the task has no numeric before/after proof for the “byte-identical” claim.  
**Evidence:** Task 5 promises to leave the existing grass overload untouched at
`:564`, then requires a shared private helper and byte-identical grass results at
`:576-578`. The Task 5 verification step at `:812-814` contains only import,
build, and commit instructions, not a same-input range comparison.  
**Impact:** A small extraction mistake can change grass coverage while scatter
tests continue to pass.  
**Effort:** Low  
**Fix Risk:** Low  
**Confidence:** High  
**Recommendation:** Capture the existing grass overload's ranges for fixed
camera/transform/radius/cell inputs before extraction and require exact
post-extraction equality, including range count, cells, and corner flag.  
**Refactor Option:** If that comparison is unavailable, duplicate the short
post-direction calculation for SP1 and defer shared extraction; prefer the
shared helper once invariance can be proven.  
**Behavior note:** The recommendation is specifically a no-behavior-change gate.

#### R6-05 — Execution text still contradicts its own history/commit rules

**Category:** Style  
**Severity:** Low  
**Description:** Stale revision and audit-history prose remains embedded in the
implementation instructions, and every checkpoint still uses the commit style
the global constraints forbid. This makes copy/paste execution unreliable.  
**Evidence:** The document title is v5 but the revision note is still `v4` at
`:11`. Snippets retain `R4-02`/`R4-03` comments at `:596`, `:622`, and `:730`.
The global rule requires imperative subjects at `:49-55`, while task checkpoints
still use `feat(scatter): ...` at `:220`, `:427`, `:487`, `:553`, `:814`, and
`:932`; self-review still stops at the R3 series at `:947-953`.  
**Impact:** Executors may copy forbidden source comments or create commits that
violate the plan's stated repository convention.  
**Effort:** Low  
**Fix Risk:** Low  
**Confidence:** High  
**Recommendation:** Rename the revision note, remove audit IDs from source
snippets, extend the self-review ledger, and delete literal commit commands or
replace their subjects with conditional imperative examples.  
**Refactor Option:** Delete stale prose rather than adding another process layer.  
**Behavior note:** Preserving.

### Refactoring Plan

1. Reconcile the corner policy in both documents and the runtime checklist.
2. Replace the estimate with one checked, exact range preflight shared by all
   gather entry points; include verification passes and retained memory.
3. Collapse validation into one null-safe, complete DTO invariant path.
4. Add exact grass-range invariance evidence around the shared helper extraction.
5. Remove stale audit/version tags and conflicting commit examples.
6. Only after implementation: Unity import/regenerate, build the Planet assembly,
   run bounded commands, and record `PASS` or the qualified corner-gap status.

### Prior Audit Reconciliation

| Prior finding | Status | Current evidence |
|---|---|---|
| R5-01 corner/proof contract | OPEN | The same fail/report/no-flag contradictions remain as R6-01. |
| R5-02 exact service budget | OPEN | The estimate, public bypass, and `int` product remain as R6-02. |
| R5-03 complete single-source DTO validation | OPEN | The same null/range/enum gaps and duplicate rules remain as R6-03. |
| R5-04 grass helper invariance | OPEN | The helper extraction still has no numeric gate; carried as R6-04. |
| R5-05 audit/commit cleanup | OPEN | No stale tag, revision, or commit example was removed; carried as R6-05. |

All R4 items previously marked RESOLVED remain resolved. Their open tails are
represented by the R6 findings above.

### Questions for the User

None. The plan already records the required behavior decisions; its
implementation body needs to apply them before another re-review can clear the
ledger.
