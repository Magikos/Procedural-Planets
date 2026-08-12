# Asset Bench Implementation Plan

> ## ✅ STATUS — verified working end-to-end (2026-08-12)
>
> **Nothing is committed**, per instruction.
>
> ### How to run it
>
> ```
> play mode  →  fly to LAND  →  open console (`)  →  bench.load scatter-candidates
> Tab / Shift+Tab   next / previous pair
> F1 keep · F2 cut · F3 later
> bench.note "..."  ·  bench.rework true  ·  bench.biome Forest
> bench.report      writes docs/research/bench-scatter-candidates-<date>.md and despawns
> ```
>
> ⚠️ **Load over land.** Grounding floors at sea level, so an ocean load stands everything on water — it looks plausible and judges nothing. The load message now warns when this happens.
>
> ### Verified
>
> - **Compiles clean.** Full EditMode suite: **94 tests, 92 pass**, all **16 bench tests green**.
> - The 2 failures are **pre-existing and unrelated** — `ScatterIdTests`, caused by the Lake biome raising `SlotBits` 6→7. See "Pre-existing failure" below.
> - **Placement verified at real planet scale:** pair gap 2.58 m (target 2.60), stride 8.95 m (target 9.00).
> - **Play-mode end-to-end on land:** 2 pairs → 4 objects spawned, grounded at ~87 m above sea level with per-object variation tracking real terrain relief, `up·radial = 1.0000` on all four. Navigation, verdicts and auto-advance all work.
> - Every no-batch and bad-input path returns a clean message with no exception.
>
> ### What exists
>
> | File | Status |
> |---|---|
> | `.gitignore` (+3 lines) | ✅ `Assets/_Bench/` confirmed ignored |
> | `Assets/Scripts/Planet/AssetBench/` — Placement, Report, Manifest, Service, Host, Commands | ✅ compiled + verified |
> | `Assets/Editor/AssetBench/AssetBenchPromoter.cs` | ⚠️ compiles; **promotion path never exercised** |
> | `Assets/Resources/Settings/AssetBench/scatter-candidates.asset` | ✅ 2 entries, round-trips via `Resources.Load` |
> | `Assets/_Bench/` | ✅ 103 MB staged — Toon Fantasy Nature + Toon Enchanted Meadow |
>
> ### Batch 1 covers 2 of the 6 style questions
>
> Both toon-tree questions, against `SM_Gen_Env_Tree_01` (already in the project). **Still to stage:** Polyart Dreamscape tree, Corals, Quirky-vs-Polyperfect, KayKit-vs-Synty-hero. The first two need dependency-chasing (prefab → shared FBX → SharedResources shadergraphs); the last two need packs unpacked from cache.
>
> ### Pre-existing failure worth a decision
>
> `ScatterId` now uses **all 64 bits** — `SlotBits = 7` pushed the player bit to 63, and the two tests asserting bit 63 stays spare were not updated. The implementation change reads deliberate (the comment says "all 64 bits used"); the tests are stale. **But `ScatterId` now has zero headroom, and the test that existed to warn about exactly that is the one failing.** Not touched — your call.
>
> ### Bugs found and fixed during verification
>
> - `AssetBenchHost.Service` was built in `Awake`, but `AddComponent` does not run `Awake` in edit mode — any command issued outside play threw `NullReferenceException`. Now lazily created.
> - `Focus()` framed the *viewpoint* rather than the pair midpoint, pointing the camera at empty ground beside the assets.
> - Added the sea-level warning above.
> - Qualified `UnityEngine.Object`; removed a dead const.
>
> ### Design changes made during implementation
>
> - **The bench frames the camera, not the character.** `IFreeCameraService.FrameWorldTarget(Vector3)` already exists and does exactly what is needed. This means **no existing file was modified** except `.gitignore` — important with a shared worktree. Judging assets needs looking, not walking.
> - **Hotkeys are `F1`/`F2`/`F3`**, not `1`/`2`/`3` — resolves spec §12 and sidesteps any number-row collision. `Tab`/`Shift+Tab` navigate. Keys are live only while a batch is loaded.
> - **Console binding is `MonoTargetType.Static`**, not `Registry` as the spec said. `IWorldServiceRegistrar` does not exist in this codebase; `CharacterCommands` is the working precedent and this mirrors it exactly.
> - `AssetBenchService.Load` returns a `string` message with an `out bool ok`, rather than the spec's `bool` + `out string error` — commands print strings, so this matches the console's grain.
>
> ### Bugs found and fixed during self-review
>
> - `Focus()` framed the *viewpoint* direction instead of the pair midpoint, which would have pointed the camera at empty ground beside the assets. Now stores and frames `_pairCentres`.
> - Qualified `UnityEngine.Object` explicitly so a later `using System;` cannot make it ambiguous.
> - Removed a now-dead `ViewBackOffMeters` const.
>
> ### Known unknowns
>
> - **`bench.note some text` may only capture the first word**, depending on how the console tokenises string args. If so, quote it: `bench.note "some text"`.
> - `AssetBenchPlacement.ViewpointDir` is currently **unused by the service** but retained with test coverage, as the fallback if `FrameWorldTarget` frames from a poor angle. Delete it if the camera framing turns out fine.
> - Layout constants (`PairStrideMeters = 9`, `PairGapMeters = 2.6`, `ForwardOffsetMeters = 14`) are guesses and will want tuning once real assets are in.


> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A play-mode tool that spawns candidate assets beside Synty references on the planet surface, lets Bryan judge each pair with a hotkey, and writes a report that drives promotion out of a gitignored staging folder.

**Architecture:** Plain-class service (`AssetBenchService`) holding batch state, driven by a find-or-created `AssetBenchHost` MonoBehaviour, exposed through static `[CommandPrefix("bench")]` commands. Pure geometry (`AssetBenchPlacement`) and pure serialisation (`AssetBenchReport`) are split out as testable plain classes. Promotion is a separate editor-only step.

**Tech Stack:** Unity 6000.6 / URP 17.6, C#, Unity Test Framework (EditMode), existing console + debug-module infrastructure.

**Spec:** [2026-08-11-asset-bench-design.md](../specs/2026-08-11-asset-bench-design.md)

## Global Constraints

- **DO NOT COMMIT.** Bryan is asleep and another agent shares this worktree. Every task ends at verification. Leave changes uncommitted.
- **Only create new files.** The single exception is one append to `.gitignore`. Do not refactor or reformat existing files.
- **No git operations that mutate the tree** — no `checkout`, `stash`, `reset`, `clean`.
- Unity `Awaitable` only. No coroutines, no `async void`, no `Task.Run`.
- No `[DefaultExecutionOrder]`. No `RuntimeInitializeOnLoadMethod`.
- New code uses `ILogger` / `LoggerProvider`, not `UnityEngine.Debug.Log*`.
- Comments only where the WHY is non-obvious. No change-history comments.
- ~400 lines per file is the guardrail.
- Namespace: none (this codebase uses the global namespace for gameplay types — match `PlanetCharacterController`).

### Deviation from spec §4, recorded

The spec names `IWorldServiceRegistrar`. **That interface does not exist in the codebase** — it is aspirational in CLAUDE.md. The working precedent is `Assets/Scripts/Planet/Character/CharacterCommands.cs`: a static `MonoTargetType.Static` command class that find-or-creates a host MonoBehaviour. This plan follows that precedent rather than inventing registration infrastructure. Console binding therefore uses `MonoTargetType.Static`, not `MonoTargetType.Registry`.

### Verified API surface

```csharp
// Assets/Scripts/Core/Interfaces/IPlanetSurfaceSampler.cs
public interface IPlanetSurfaceSampler { bool TryGetSurfaceRadius(Vector3 worldUnitDirection, out float surfaceRadius); }
public struct PlanetSurfaceRaycastHit { public Vector3 Point; public Vector3 Normal; public float Distance; public float SurfaceRadius; }
public interface IPlanetSurfaceRaycaster { bool TryRaycastSurface(Ray worldRay, float maxDistance, out PlanetSurfaceRaycastHit hit); }

// Assets/Scripts/Planet/Character/IGroundingProvider.cs
public readonly struct GroundResult { public readonly Vector3 Position; public readonly Vector3 Normal; public GroundResult(Vector3 position, Vector3 normal); }
public interface IGroundingProvider { bool TryGround(Vector3 worldPos, Vector3 downDir, float footOffset, out GroundResult result); }

// Assets/Scripts/Planet/Character/PlanetRaycastGrounding.cs
public PlanetRaycastGrounding(IPlanetSurfaceRaycaster raycaster, Vector3 center, float seaLevelRadius, IGroundingProvider fallback)

// Console
[CommandPrefix("bench")]                                    // class attribute
[ConsoleCommand("name", "description", MonoTargetType.Static)]  // method attribute
```

---

### Task 1: Staging folder and gitignore

**Files:**
- Modify: `.gitignore` (append only)
- Create: `Assets/_Bench/.keep`

**Interfaces:**
- Produces: the staging path `Assets/_Bench/` that Task 7 imports into and Task 6 promotes out of.

- [ ] **Step 1: Append to `.gitignore`**

Append these three lines at the end of the file. Do not reorder or edit existing lines.

```gitignore

# Asset Bench staging — candidate assets under evaluation, never committed
/Assets/_Bench/
/Assets/_Bench.meta
```

- [ ] **Step 2: Create the folder with a placeholder**

Create `Assets/_Bench/.keep` containing:

```
Asset Bench staging folder. Gitignored.
Candidate assets are imported here for judging and promoted out on a "keep" verdict.
See docs/superpowers/specs/2026-08-11-asset-bench-design.md
```

- [ ] **Step 3: Verify git ignores it**

Run: `git -C . status --porcelain -- Assets/_Bench`
Expected: **no output** (the folder is ignored).

Run: `git -C . check-ignore -v Assets/_Bench/.keep`
Expected: a line naming `.gitignore` and the `/Assets/_Bench/` pattern.

---

### Task 2: Placement geometry (pure, tested)

**Files:**
- Create: `Assets/Scripts/Planet/AssetBench/AssetBenchPlacement.cs`
- Test: `Assets/Tests/EditMode/AssetBenchPlacementTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  ```csharp
  public readonly struct BenchSlot { public readonly Vector3 CandidateDir; public readonly Vector3 ReferenceDir; }
  public static class AssetBenchPlacement
  {
      public static void BuildTangentBasis(Vector3 radialUp, out Vector3 tangentRight, out Vector3 tangentForward);
      public static BenchSlot SlotDirection(Vector3 originDir, int index, float pairStrideRad, float pairGapRad, float forwardOffsetRad);
      public static Vector3 ViewpointDir(BenchSlot slot, float backOffRad);
  }
  ```

Directions are unit vectors from the planet centre. Working in *angular* offsets rather than world distances keeps the maths radius-independent and avoids drift over a curved surface.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/AssetBenchPlacementTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class AssetBenchPlacementTests
{
    const float Eps = 1e-4f;

    [Test]
    public void BuildTangentBasis_IsOrthonormalToUp()
    {
        Vector3 up = new Vector3(0.3f, 0.9f, -0.2f).normalized;
        AssetBenchPlacement.BuildTangentBasis(up, out Vector3 right, out Vector3 forward);

        Assert.AreEqual(1f, right.magnitude, Eps, "right not unit length");
        Assert.AreEqual(1f, forward.magnitude, Eps, "forward not unit length");
        Assert.AreEqual(0f, Vector3.Dot(right, up), Eps, "right not perpendicular to up");
        Assert.AreEqual(0f, Vector3.Dot(forward, up), Eps, "forward not perpendicular to up");
        Assert.AreEqual(0f, Vector3.Dot(right, forward), Eps, "basis not orthogonal");
    }

    [Test]
    public void BuildTangentBasis_HandlesPolarUp()
    {
        // A naive Cross(up, Vector3.up) degenerates at the pole.
        AssetBenchPlacement.BuildTangentBasis(Vector3.up, out Vector3 right, out Vector3 forward);

        Assert.AreEqual(1f, right.magnitude, Eps);
        Assert.AreEqual(1f, forward.magnitude, Eps);
        Assert.AreEqual(0f, Vector3.Dot(right, Vector3.up), Eps);
    }

    [Test]
    public void SlotDirection_ProducesUnitDirections()
    {
        Vector3 origin = Vector3.forward;
        BenchSlot slot = AssetBenchPlacement.SlotDirection(origin, 0, 0.01f, 0.003f, 0.005f);

        Assert.AreEqual(1f, slot.CandidateDir.magnitude, Eps);
        Assert.AreEqual(1f, slot.ReferenceDir.magnitude, Eps);
    }

    [Test]
    public void SlotDirection_SeparatesConsecutiveIndices()
    {
        Vector3 origin = Vector3.forward;
        BenchSlot a = AssetBenchPlacement.SlotDirection(origin, 0, 0.01f, 0.003f, 0.005f);
        BenchSlot b = AssetBenchPlacement.SlotDirection(origin, 1, 0.01f, 0.003f, 0.005f);

        float sep = Vector3.Angle(a.CandidateDir, b.CandidateDir);
        Assert.Greater(sep, 0.1f, "consecutive slots must not overlap");
    }

    [Test]
    public void SlotDirection_PairGapIsSmallerThanStride()
    {
        Vector3 origin = Vector3.forward;
        BenchSlot a = AssetBenchPlacement.SlotDirection(origin, 0, 0.01f, 0.003f, 0.005f);
        BenchSlot b = AssetBenchPlacement.SlotDirection(origin, 1, 0.01f, 0.003f, 0.005f);

        float withinPair = Vector3.Angle(a.CandidateDir, a.ReferenceDir);
        float betweenPairs = Vector3.Angle(a.CandidateDir, b.CandidateDir);
        Assert.Less(withinPair, betweenPairs, "a pair must read as closer together than two pairs");
    }

    [Test]
    public void ViewpointDir_SitsBackFromTheSlot()
    {
        Vector3 origin = Vector3.forward;
        BenchSlot slot = AssetBenchPlacement.SlotDirection(origin, 2, 0.01f, 0.003f, 0.005f);
        Vector3 view = AssetBenchPlacement.ViewpointDir(slot, 0.004f);

        Assert.AreEqual(1f, view.magnitude, Eps);
        Vector3 mid = (slot.CandidateDir + slot.ReferenceDir).normalized;
        Assert.Greater(Vector3.Angle(view, mid), 0.05f, "viewpoint must be offset from the pair");
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run the EditMode suite (Unity Test Runner, or via MCP `run_tests` with mode `EditMode`, filter `AssetBenchPlacementTests`).
Expected: FAIL — `AssetBenchPlacement` does not exist.

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Planet/AssetBench/AssetBenchPlacement.cs`:

```csharp
using UnityEngine;

/// <summary>One bench slot: a candidate direction and its reference direction, both unit vectors from the planet centre.</summary>
public readonly struct BenchSlot
{
    public readonly Vector3 CandidateDir;
    public readonly Vector3 ReferenceDir;

    public BenchSlot(Vector3 candidateDir, Vector3 referenceDir)
    {
        CandidateDir = candidateDir;
        ReferenceDir = referenceDir;
    }
}

/// <summary>
/// Bench layout maths. Offsets are angular (radians of arc) rather than world distances so the layout is
/// independent of planet radius and does not drift as it wraps around the curve.
/// </summary>
public static class AssetBenchPlacement
{
    public static void BuildTangentBasis(Vector3 radialUp, out Vector3 tangentRight, out Vector3 tangentForward)
    {
        Vector3 up = radialUp.normalized;

        // Cross with whichever world axis is least parallel to up, so the basis never degenerates at a pole.
        Vector3 seed = Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right;

        tangentRight = Vector3.Cross(seed, up).normalized;
        tangentForward = Vector3.Cross(up, tangentRight).normalized;
    }

    public static BenchSlot SlotDirection(
        Vector3 originDir, int index, float pairStrideRad, float pairGapRad, float forwardOffsetRad)
    {
        Vector3 origin = originDir.normalized;
        BuildTangentBasis(origin, out Vector3 right, out Vector3 forward);

        float alongArc = index * pairStrideRad;
        Vector3 slotCentre = Rotate(origin, right, forward, alongArc, forwardOffsetRad);

        Vector3 candidate = Rotate(slotCentre, right, forward, -pairGapRad * 0.5f, 0f);
        Vector3 reference = Rotate(slotCentre, right, forward, pairGapRad * 0.5f, 0f);
        return new BenchSlot(candidate, reference);
    }

    public static Vector3 ViewpointDir(BenchSlot slot, float backOffRad)
    {
        Vector3 mid = (slot.CandidateDir + slot.ReferenceDir).normalized;
        BuildTangentBasis(mid, out Vector3 right, out Vector3 forward);
        return Rotate(mid, right, forward, 0f, -backOffRad);
    }

    static Vector3 Rotate(Vector3 dir, Vector3 right, Vector3 forward, float rightRad, float forwardRad)
    {
        Vector3 result = dir;
        if (Mathf.Abs(rightRad) > 1e-8f)
            result = Quaternion.AngleAxis(rightRad * Mathf.Rad2Deg, Vector3.Cross(right, result).normalized == Vector3.zero ? forward : Vector3.Cross(result, right).normalized) * result;
        if (Mathf.Abs(forwardRad) > 1e-8f)
            result = Quaternion.AngleAxis(forwardRad * Mathf.Rad2Deg, Vector3.Cross(result, forward).normalized) * result;
        return result.normalized;
    }
}
```

⚠️ **Implementation note for the engineer:** the `Rotate` helper above is the fiddly part. If the guard expression proves awkward, the simpler equivalent is to rotate about the fixed basis axes captured *before* any rotation:
`result = Quaternion.AngleAxis(rightRad * Mathf.Rad2Deg, forward) * Quaternion.AngleAxis(forwardRad * Mathf.Rad2Deg, right) * dir`. Prefer whichever passes the tests; the tests define the contract, not this sketch.

- [ ] **Step 4: Run to verify pass**

Expected: all 6 `AssetBenchPlacementTests` PASS.

- [ ] **Step 5: Verify no regression**

Run the full EditMode suite. Expected: **84 passing** (78 existing + 6 new), 0 failures.
**Do not commit.**

---

### Task 3: Report writer (pure, tested)

**Files:**
- Create: `Assets/Scripts/Planet/AssetBench/AssetBenchReport.cs`
- Test: `Assets/Tests/EditMode/AssetBenchReportTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  ```csharp
  public enum BenchVerdict { Unjudged, Keep, Cut, Later, Error }
  public sealed class BenchRow
  {
      public int Index; public string Label; public string Question;
      public BenchVerdict Verdict; public bool NeedsRework;
      public string Biome; public string Note; public string CandidatePath;
  }
  public static class AssetBenchReport
  {
      public static string BuildMarkdown(string batchId, string isoTimestamp, IReadOnlyList<BenchRow> rows);
  }
  ```

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/AssetBenchReportTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class AssetBenchReportTests
{
    static BenchRow Row(int i, string label, BenchVerdict v, string note = "") => new BenchRow
    {
        Index = i, Label = label, Question = "q", Verdict = v,
        NeedsRework = false, Biome = "Grassland", Note = note,
        CandidatePath = "Assets/_Bench/Pack/Thing.prefab"
    };

    [Test]
    public void BuildMarkdown_IncludesBatchIdAndTimestamp()
    {
        string md = AssetBenchReport.BuildMarkdown("scatter-candidates", "2026-08-11T02:00:00Z",
            new List<BenchRow> { Row(0, "Quirky Fox", BenchVerdict.Keep) });

        StringAssert.Contains("scatter-candidates", md);
        StringAssert.Contains("2026-08-11T02:00:00Z", md);
    }

    [Test]
    public void BuildMarkdown_EmitsOneRowPerEntry()
    {
        var rows = new List<BenchRow> { Row(0, "A", BenchVerdict.Keep), Row(1, "B", BenchVerdict.Cut), Row(2, "C", BenchVerdict.Later) };
        string md = AssetBenchReport.BuildMarkdown("b", "t", rows);

        StringAssert.Contains("| A ", md);
        StringAssert.Contains("| B ", md);
        StringAssert.Contains("| C ", md);
    }

    [Test]
    public void BuildMarkdown_EscapesPipesInNotes()
    {
        string md = AssetBenchReport.BuildMarkdown("b", "t",
            new List<BenchRow> { Row(0, "A", BenchVerdict.Cut, "too dark | too shiny") });

        StringAssert.Contains(@"too dark \| too shiny", md);
    }

    [Test]
    public void BuildMarkdown_HandlesEmptyBatch()
    {
        string md = AssetBenchReport.BuildMarkdown("b", "t", new List<BenchRow>());

        Assert.IsNotNull(md);
        StringAssert.Contains("no entries", md.ToLowerInvariant());
    }

    [Test]
    public void BuildMarkdown_SummarisesVerdictCounts()
    {
        var rows = new List<BenchRow>
        {
            Row(0, "A", BenchVerdict.Keep), Row(1, "B", BenchVerdict.Keep),
            Row(2, "C", BenchVerdict.Cut), Row(3, "D", BenchVerdict.Unjudged)
        };
        string md = AssetBenchReport.BuildMarkdown("b", "t", rows);

        StringAssert.Contains("keep 2", md.ToLowerInvariant());
        StringAssert.Contains("cut 1", md.ToLowerInvariant());
        StringAssert.Contains("unjudged 1", md.ToLowerInvariant());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Expected: FAIL — `AssetBenchReport` does not exist.

- [ ] **Step 3: Implement**

Create `Assets/Scripts/Planet/AssetBench/AssetBenchReport.cs`:

```csharp
using System.Collections.Generic;
using System.Text;

public enum BenchVerdict { Unjudged, Keep, Cut, Later, Error }

public sealed class BenchRow
{
    public int Index;
    public string Label;
    public string Question;
    public BenchVerdict Verdict;
    public bool NeedsRework;
    public string Biome;
    public string Note;
    public string CandidatePath;
}

/// <summary>Serialises bench verdicts to markdown so they are diffable, readable in-repo, and parseable by the promoter.</summary>
public static class AssetBenchReport
{
    public static string BuildMarkdown(string batchId, string isoTimestamp, IReadOnlyList<BenchRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("# Asset bench — ").Append(batchId).AppendLine();
        sb.AppendLine();
        sb.Append("_Judged ").Append(isoTimestamp).Append("._").AppendLine();
        sb.AppendLine();

        if (rows == null || rows.Count == 0)
        {
            sb.AppendLine("No entries in this batch.");
            return sb.ToString();
        }

        int keep = 0, cut = 0, later = 0, unjudged = 0, error = 0;
        foreach (BenchRow r in rows)
        {
            switch (r.Verdict)
            {
                case BenchVerdict.Keep: keep++; break;
                case BenchVerdict.Cut: cut++; break;
                case BenchVerdict.Later: later++; break;
                case BenchVerdict.Error: error++; break;
                default: unjudged++; break;
            }
        }

        sb.Append("**Keep ").Append(keep)
          .Append(" · Cut ").Append(cut)
          .Append(" · Later ").Append(later)
          .Append(" · Unjudged ").Append(unjudged)
          .Append(" · Error ").Append(error).Append("**").AppendLine();
        sb.AppendLine();

        sb.AppendLine("| # | Label | Verdict | Needs rework | Biome | Note | Question | Path |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");

        foreach (BenchRow r in rows)
        {
            sb.Append("| ").Append(r.Index)
              .Append(" | ").Append(Escape(r.Label))
              .Append(" | ").Append(r.Verdict)
              .Append(" | ").Append(r.NeedsRework ? "yes" : "")
              .Append(" | ").Append(Escape(r.Biome))
              .Append(" | ").Append(Escape(r.Note))
              .Append(" | ").Append(Escape(r.Question))
              .Append(" | `").Append(Escape(r.CandidatePath)).Append("` |")
              .AppendLine();
        }

        return sb.ToString();
    }

    static string Escape(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("|", @"\|").Replace("\n", " ");
}
```

- [ ] **Step 4: Run to verify pass**

Expected: all 5 `AssetBenchReportTests` PASS.

- [ ] **Step 5: Verify no regression**

Full EditMode suite: **89 passing**, 0 failures. **Do not commit.**

---

### Task 4: Manifest ScriptableObject

**Files:**
- Create: `Assets/Scripts/Planet/AssetBench/AssetBenchManifest.cs`

**Interfaces:**
- Produces:
  ```csharp
  [System.Serializable] public sealed class AssetBenchEntry
  { public GameObject CandidatePrefab; public GameObject ReferencePrefab; public string Label; public string Question; }

  public sealed class AssetBenchManifest : ScriptableObject
  { public string BatchId; public AssetBenchEntry[] Entries; }
  ```

- [ ] **Step 1: Implement**

Create `Assets/Scripts/Planet/AssetBench/AssetBenchManifest.cs`:

```csharp
using UnityEngine;

[System.Serializable]
public sealed class AssetBenchEntry
{
    public GameObject CandidatePrefab;
    public GameObject ReferencePrefab;
    public string Label;

    [Tooltip("Why this is being judged, shown in the HUD. e.g. \"vs Polyperfect for wildlife\"")]
    public string Question;
}

/// <summary>
/// An authored batch of candidates for the asset bench. Content data, not a settings SO — read once on
/// <c>bench.load</c> rather than consumed per frame, so it needs no DTO snapshot.
/// </summary>
[CreateAssetMenu(menuName = "ProceduralPlanets/Asset Bench Manifest", fileName = "AssetBenchManifest")]
public sealed class AssetBenchManifest : ScriptableObject
{
    [Tooltip("Used in the report filename. e.g. \"scatter-candidates\"")]
    public string BatchId = "batch";

    public AssetBenchEntry[] Entries = System.Array.Empty<AssetBenchEntry>();
}
```

- [ ] **Step 2: Verify it compiles**

Trigger a Unity refresh and read the console. Expected: 0 errors. The menu item `Assets ▸ Create ▸ ProceduralPlanets ▸ Asset Bench Manifest` exists.
**Do not commit.**

---

### Task 5: Bench service, host and console commands

**Files:**
- Create: `Assets/Scripts/Planet/AssetBench/AssetBenchService.cs`
- Create: `Assets/Scripts/Planet/AssetBench/AssetBenchHost.cs`
- Create: `Assets/Scripts/Planet/AssetBench/AssetBenchCommands.cs`

**Interfaces:**
- Consumes: `AssetBenchPlacement`, `BenchSlot`, `AssetBenchReport`, `BenchRow`, `BenchVerdict`, `AssetBenchManifest`, `IGroundingProvider`, `IPlanetSurfaceSampler`.
- Produces:
  ```csharp
  public sealed class AssetBenchService
  {
      public bool IsLoaded { get; }
      public int FocusIndex { get; }
      public int Count { get; }
      public string StatusLine { get; }
      public bool Load(AssetBenchManifest manifest, Vector3 originDir, out string error);
      public void Focus(int index);
      public void FocusNext();
      public void FocusPrevious();
      public void SetVerdict(BenchVerdict verdict);
      public void SetNote(string note);
      public void SetNeedsRework(bool value);
      public void SetBiome(string biome);
      public string BuildReport(string isoTimestamp);
      public void Unload();
      public Vector3 CurrentViewpointDir { get; }
  }
  ```

Behaviour notes for the implementer:

- `Load` refuses if `IsLoaded` is already true — return `false` with an error message. This is the spec's "refuse a second load" rule.
- Grounding: use the same provider the character uses. Construct `PlanetRaycastGrounding` with the planet's raycaster, centre and sea-level radius, and an analytic fallback — mirror how `PlanetCharacterController` builds its grounding. If grounding fails for a slot, set that row's verdict to `BenchVerdict.Error` and skip spawning it, per spec §9.
- Spawned instances are parented to a single container GameObject so `Unload` is one `Destroy`.
- Each instance is oriented radially: `Quaternion.LookRotation(tangentForward, radialUp)`.
- **Do not** modify materials. Spec §5.
- `SetVerdict` auto-advances via `FocusNext`.
- Log through `ILogger` / `LoggerProvider`, never `Debug.Log`.

- [ ] **Step 1: Implement `AssetBenchService`**

Plain class, no MonoBehaviour. Holds `AssetBenchManifest`, `BenchRow[]`, spawned `GameObject[]`, focus index, container transform, and the grounding provider. Keep under ~250 lines; if it grows past the guardrail, split spawning into an `AssetBenchSpawner`.

- [ ] **Step 2: Implement `AssetBenchHost`**

```csharp
using UnityEngine;

/// <summary>
/// Scene host for the asset bench. Exists because the bench needs a per-frame hotkey poll and a place to parent
/// spawned candidates; all logic lives in the plain-class <see cref="AssetBenchService"/>.
/// </summary>
public sealed class AssetBenchHost : MonoBehaviour
{
    public AssetBenchService Service { get; private set; }

    void Awake() => Service = new AssetBenchService();

    void Update()
    {
        if (Service == null || !Service.IsLoaded)
            return;

        // Verdict hotkeys are only live while a batch is loaded, so they cannot fight normal play-mode input.
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) Service.FocusPrevious();
            else Service.FocusNext();
        }
        else if (Input.GetKeyDown(KeyCode.F1)) Service.SetVerdict(BenchVerdict.Keep);
        else if (Input.GetKeyDown(KeyCode.F2)) Service.SetVerdict(BenchVerdict.Cut);
        else if (Input.GetKeyDown(KeyCode.F3)) Service.SetVerdict(BenchVerdict.Later);
    }

    void OnDestroy() => Service?.Unload();
}
```

⚠️ **Hotkey decision, resolving spec §12.** Use `F1/F2/F3`, not `1/2/3`. The number row is a plausible collision with existing play-mode bindings and this avoids the question entirely. `Tab` is retained for navigation. If `Tab` also collides, fall back to `F5`/`Shift+F5`.

- [ ] **Step 3: Implement `AssetBenchCommands`**

Mirror `Assets/Scripts/Planet/Character/CharacterCommands.cs` exactly in shape: a static class with `[CommandPrefix("bench")]`, each command `MonoTargetType.Static`, and a private `FindOrCreateHost()` that does `Object.FindFirstObjectByType<AssetBenchHost>()` and creates a `new GameObject("AssetBenchHost")` with the component when absent.

Commands: `load <manifestName>`, `next`, `prev`, `keep`, `cut`, `later`, `note <text>`, `rework <true|false>`, `biome <name>`, `status`, `report`, `cancel`.

`load` resolves the manifest by name from `Resources/Settings/AssetBench/`. `report` writes to `docs/research/bench-<batchId>-<yyyy-MM-dd>.md` via `System.IO.File.WriteAllText` using an absolute path built from `Application.dataPath`, then calls `Unload`.

- [ ] **Step 4: Verify compile**

Unity refresh, read console. Expected: 0 errors, 0 new warnings.

- [ ] **Step 5: Verify commands register**

Enter play mode, open the console with `` ` ``, type `bench.` and confirm the twelve commands appear in completion. Type `bench.status` — expected: a "no batch loaded" message, no exception.
**Do not commit.**

---

### Task 6: Editor promoter

**Files:**
- Create: `Assets/Editor/AssetBench/AssetBenchPromoter.cs`

**Interfaces:**
- Consumes: a report markdown file produced by Task 5.
- Produces: a menu item `Tools ▸ Asset Bench ▸ Promote From Report…`.

- [ ] **Step 1: Implement**

An `EditorWindow` or a menu item that: opens a file picker defaulting to `docs/research/`; parses the markdown table; lists the `Keep` rows with their source paths and a destination field defaulting to `Assets/AssetPacks/<pack>/`; and on confirm calls `AssetDatabase.MoveAsset` per row, collecting failures.

Rules:
- **Refuse and report if the destination already exists.** Never overwrite. Spec §9.
- Use `AssetDatabase.MoveAsset` (carries the `.meta`, preserves the GUID). Never `System.IO.File.Move`.
- `AssetDatabase.StartAssetEditing()` / `StopAssetEditing()` around the batch.
- Print a summary: moved, skipped, failed.

- [ ] **Step 2: Verify**

With no report selected, the menu item opens without error. Point it at a hand-written two-row markdown file whose paths do not exist; expect both rows reported as failures and **no** exception.
**Do not commit.**

---

### Task 7: Import candidates and author the first manifest

**Files:**
- Create: `Assets/Resources/Settings/AssetBench/scatter-candidates.asset`
- Import into: `Assets/_Bench/…` (gitignored)

**Interfaces:**
- Consumes: everything above.

The first batch is the six open style questions from the adoption map's §M2.

| # | Candidate | Reference | Question |
|---|---|---|---|
| 1 | Quirky Series animal | Polyperfect equivalent species | Better Synty match than Polyperfect? |
| 2 | KayKit Adventurers character | Synty PolygonFantasyHeroCharacters preset | Does KayKit sit next to Synty? |
| 3 | Corals `Coral_13_G1` | — (judge underwater, no land reference) | Does 2K PBR read acceptably below the waterline? |
| 4 | Toon Fantasy Nature tree | Synty `SM_Gen_Env_Tree_01` | Toon outline vs Synty flat |
| 5 | Toon Enchanted Meadow tree | Synty `SM_Gen_Env_Tree_01` | Same question, second pack |
| 6 | Polyart Dreamscape tree | Synty `SM_Gen_Env_Tree_01` | Painterly PBR vs Synty flat |

- [ ] **Step 1: Import candidate packs into `Assets/_Bench/`**

Extract only the prefabs/meshes/textures needed for the six rows — **not** whole packs. Sources are cached `.unitypackage` files; reconstruct with the scratchpad `unpack.ps1` and copy the needed assets in, or import the package and immediately move the needed assets into `Assets/_Bench/` and delete the rest.

⚠️ This is the slow step and the one most likely to disturb the other agent working in this worktree (long import, domain reloads). Do it in one pass, not incrementally.

- [ ] **Step 2: Author the manifest**

Create the asset via the menu, set `BatchId = "scatter-candidates"`, and fill the six entries with the prefab references and questions from the table above.

- [ ] **Step 3: Verify end to end**

Enter play mode → `character.spawn` on land → `bench.load scatter-candidates`. Expected: six candidate/reference pairs grounded on the surface ahead, camera framed on pair 1, HUD showing `1/6 · <label> · <question>`.
Press `Tab` → advances to pair 2. Press `F1` → records Keep and advances. `bench.report` → writes the markdown and despawns.
**Do not commit.**

---

## Verification summary

| Task | How it is verified |
|---|---|
| 1 | `git check-ignore` confirms the staging path is ignored |
| 2 | 6 EditMode tests pass; full suite 84 green |
| 3 | 5 EditMode tests pass; full suite 89 green |
| 4 | Compiles; `CreateAssetMenu` entry present |
| 5 | Commands appear in console completion; `bench.status` is safe with no batch |
| 6 | Menu item opens; bad paths reported, no exception |
| 7 | Full loop: load → Tab → F1 → report |

## Known risks

1. **Import cost dominates.** Task 7 is the long pole; Tasks 1–6 are cheap.
2. **Shared worktree.** Another agent is active. Only new files plus one `.gitignore` append. **No commits.**
3. **`Rotate` in Task 2** is the only genuinely fiddly maths. The tests define the contract; if the sketched implementation is awkward, replace it with the fixed-axis form noted there.
4. **Grounding depends on a generated planet.** `bench.load` before a planet exists must fail cleanly, not throw.
