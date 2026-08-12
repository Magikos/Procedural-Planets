# Asset Bench — design

_Spec. 2026-08-11. Status: awaiting Bryan's review._

## 1. Problem

The adoption map ([2026-08-11-asset-adoption-map.md](../../research/2026-08-11-asset-adoption-map.md)) has ~6 open style-match questions and a **Maybe** list that cannot be closed by reading code. They all reduce to the same judgement: *"does this asset sit next to Synty POLYGON, under our planet lighting, at real scale, at the distance we'll normally see it?"*

That question is only answerable **in play mode, on the planet**. An editor window with default lighting answers a different question, and a markdown checklist makes you judge from memory while alt-tabbed away from the thing.

This recurs. Content breadth is a long-term arc — every new pack, every new scatter prototype, every impostor A/B needs the same call. So the bench is a reusable tool, not a one-off scene.

## 2. Non-goals

- **No editor window.** Judgement happens in play mode.
- **No numeric scoring rubric.** `keep` / `cut` / `later` is the whole vocabulary.
- **No screenshot automation.** F10 already exists; the report records the capture name if one was taken.
- **No undo history** beyond re-judging a pair.
- **No asset modification.** The bench never re-materials, re-scales or re-authors anything. It shows assets as imported and records a verdict.

## 3. Staging model

Candidates live in a **gitignored staging folder** and are only promoted into the project once they pass.

```gitignore
/Assets/_Bench/
/Assets/_Bench.meta
```

Rationale: the worktree is routinely dirty and `character-controller-mvp` receives commits from parallel sessions, so a "import → judge → hard-revert" workflow risks other people's work. A staging folder removes that failure mode, and `later` verdicts stay staged across sessions instead of being reverted and re-imported.

Consequences to be explicit about:

- **Unity still imports everything under `Assets/`.** This keeps *git* clean, not the import. Import time and Library bloat are unchanged.
- **`_Bench/` is allowed to stay messy.** A `cut` verdict costs nothing but disk; nothing needs cleaning up.
- **Promotion is selective and per-asset** — typically "take the 8 FBX we want out of the 200 the pack shipped", not "move the folder".

## 4. Architecture

Follows the existing debug-surface pattern. The closest precedent is **`Assets/Scripts/Core/Services/ScaleReferenceMarkers.cs`**, which already places markers on the planet surface via `IPlanetSurfaceSampler.TryGetSurfaceRadius` and pairs with a `ScaleReferenceDebugModule`. The bench is modelled on it.

| Unit | Type | Responsibility |
|---|---|---|
| `AssetBenchManifest` | `ScriptableObject` | Authored batch: ordered list of entries. Content data, not a settings SO — read once on `bench.load`, so no DTO snapshot is required (it is not a per-frame settings consumer). |
| `AssetBenchService` | plain class, world-scoped | Owns batch state, spawning, focus index, verdicts. Registered via `IWorldServiceRegistrar`; init through `ILateInitialize` (depends on the planet surface sampler and the character host). Owns its own `[ConsoleCommand]`s per CLAUDE.md — no separate `*Commands` companion unless it outgrows the service. **Console binding: `[CommandPrefix("bench")]` on the class, `MonoTargetType.Registry` on the commands, and `ConsoleRegistry.RegisterInstance<AssetBenchService>(this)` at init.** Note this is deliberately *more* rule-compliant than the `ScaleReferenceMarkers` precedent, which is a MonoBehaviour using `MonoTargetType.Single`; the bench needs no Unity message of its own, so per CLAUDE.md it stays a plain class and the orchestrator forwards `Update` for hotkey polling. |
| `AssetBenchPlacement` | plain class | Grounding and pose maths. Split out because it is the only part with non-obvious geometry and the only part worth testing. |
| `AssetBenchReport` | plain class | Serialises verdicts to markdown. |
| `AssetBenchDebugModule` | `IDebugCommandProvider` | Reports batch/focus/verdict counts into the debug surface, matching the other nine modules. |
| `AssetBenchPromoter` | **editor-only** | Reads a report, moves `keep` rows via `AssetDatabase.MoveAsset`. Lives in the editor assembly. |

Rules: `Awaitable` only, no coroutines; no `[DefaultExecutionOrder]`; no `RuntimeInitializeOnLoadMethod`; services resolved at init, never per frame.

### 4.1 Manifest schema

```
AssetBenchManifest : ScriptableObject
  string batchId                     // "scatter-candidates" — used in the report filename
  Entry[] entries
      GameObject candidatePrefab
      GameObject referencePrefab     // the Synty comparand; optional
      string     label               // human name shown in the HUD
      string     question            // why this is being judged, e.g. "vs Polyperfect for wildlife"
```

Authored by Claude, not by hand. One manifest per batch so batches never collide.

## 5. Placement

Grounding uses **the same path the character walks on** — `PlanetRaycastGrounding` (visible-mesh raycast with analytic `TryGetSurfaceRadius` fallback), not the analytic sampler alone. Rationale: the look-fixes backlog records that analytic and rendered surfaces disagree by ~3–24 units, and a prop grounded on the analytic surface floats above or sinks below the terrain the player is standing on. Judging a floating prop answers the wrong question.

For each entry `i`:

1. Take the player's position and radial up. Build a tangent basis.
2. Step `i * stride` along the tangent "right" axis, and a fixed offset along tangent "forward", to get a direction from the planet centre.
3. Ground that direction; place the candidate there, **oriented radially** (`up = radial`).
4. Place `referencePrefab` beside it at a fixed pair-gap on the same tangent line.

Both members of a pair are grounded independently, so a slope doesn't tilt one relative to the other.

**Materials are left exactly as imported.** The bench deliberately does *not* apply `Planet/PropLit`, because "how does this look as shipped" is part of the judgement — a pack that needs re-materialing to be acceptable is a different verdict from one that works out of the box. The report has a field for that.

## 6. Interaction

`Tab` **teleports the player to stand in front of pair N, facing it**, at a fixed distance and framing. This is the core interaction decision: it means every comparison is framed identically, the Synty reference is always in-frame beside the candidate, and it removes the need for world-space label rendering entirely.

```
bench.load <batchId>      spawn the batch, jump to pair 1
[Tab] / [Shift+Tab]       next / previous pair
[1] keep  [2] cut  [3] later    verdict + auto-advance
bench.note "<text>"       attach a note to the focused pair
bench.biome <Biome>       teleport the whole batch to another biome and re-ground
bench.dist <n>            change viewing distance (near/mid/far matter for impostors)
bench.report              write the report and despawn
bench.status              print progress
```

HUD shows `pair i/N · label · question · current verdict`.

`bench.biome` matters more than it looks: half these questions are *"does it work in this biome's palette and lighting"*, and re-grounding an existing batch beats reloading it.

⚠️ **Hotkey collision risk.** `1/2/3` may already be bound in play mode, and the console captures the keyboard on `` ` ``. Verify before wiring; fall back to `F1/F2/F3`. Verdict keys are only live while a batch is loaded.

## 7. Report

Written to `docs/research/bench-<batchId>-<date>.md`:

| # | label | verdict | needs rework | biome | distance | note | capture |
|---|---|---|---|---|---|---|---|

Markdown so it is diffable and readable in the repo; parsed by both the promoter and by Claude when folding results back into the adoption map's **Maybe** section. Each bench round should therefore move Maybe rows into either an import commit or a documented rejection — the list becomes self-clearing.

## 8. Promotion

Separate step, **after leaving play mode**, because `AssetDatabase.MoveAsset` is not safe during play.

1. Read the report, take the `keep` rows.
2. `AssetDatabase.MoveAsset` each asset from `Assets/_Bench/<pack>/…` to `Assets/AssetPacks/<pack>/…`. This carries the `.meta` and preserves the GUID, so anything already referencing the asset survives.
3. Re-material onto `Planet/PropLit`, build LODGroups, author scatter prototypes — normal import work, out of scope here.
4. Commit.

`cut` rows may be deleted or simply left in `_Bench/`. `later` rows stay staged for the next round.

## 9. Error handling

| Case | Behaviour |
|---|---|
| Manifest missing / empty | Command fails with a clear message; no state change. |
| Prefab reference null | Skip the entry, log once at `Info`, continue the batch. |
| Grounding fails for a direction | Skip that entry, record `verdict = error` in the report rather than silently dropping it. |
| `bench.load` while a batch is live | Refuse; require `bench.report` or an explicit `bench.cancel`. |
| Play mode exited with a live batch | Verdicts are lost. Acceptable — `bench.report` is one keystroke. Document it; do not build autosave. |
| Promotion target already exists | Refuse that row, report it. Never overwrite. |

## 10. Testing

Consistent with the existing 78 EditMode tests, the testable surface is the placement maths and the report writer — both plain classes with no Unity dependencies beyond `Vector3`.

- `AssetBenchPlacement`: entries are spaced along a tangent basis; pair members share a tangent line; placement is radial-up; a batch of N produces N distinct directions.
- `AssetBenchReport`: round-trips verdicts, escapes markdown in notes, handles empty batches.

The spawning, teleport and hotkey paths are play-mode-only and verified by using the tool.

## 11. Risks

1. **Import cost is the real bottleneck**, not the judging. Six packs is a substantial import before anything is lookable. Stage it and ping Bryan when ready rather than have him wait.
2. **Hotkey collisions** (§6).
3. **Scope creep toward a general asset browser.** The bench answers one question. If it starts growing filters, thumbnails or search, stop.

## 12. Open

- Exact hotkeys pending the collision check.
- Whether `bench.biome` teleports the player or relocates the batch around the player — decide during implementation; relocating the batch is likely simpler and keeps framing identical.
