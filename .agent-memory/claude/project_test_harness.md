---
name: project-test-harness
description: 2026-07-27 first tests in the project (EditMode, 49 green) — Bryan requested TDD, which overrides the standing no-test-framework rule; CLAUDE.md + testing-stance memory need reconciling
metadata:
  type: project
---

First tests landed 2026-07-27 on branch `scatter-placement`, at Bryan's explicit request ("create
some tests so we can start validating our data structures with test driven development").

**Rule tension to resolve (flag for Bryan):** CLAUDE.md still says "No test framework is being added
near-term. Don't propose one," and [[project-testing-stance]] echoes it. Bryan's direct request
overrides both (user instruction > standing rule), and Unity Test Framework 1.8.0 was *already* in the
manifest — nothing new was added, just used. But CLAUDE.md and the testing-stance memory should be
updated to reflect that tests are now wanted, once Bryan confirms he's keeping this. Until then, treat
the standing "don't propose a framework" text as superseded by his request, not as a live prohibition.

**What exists**
- `Assets/Tests/EditMode/ProceduralPlanets.Tests.EditMode.asmdef` — references ProceduralPlanets.Planet
  + Core; `includePlatforms:["Editor"]`, `autoReferenced:false`, `defineConstraints:["UNITY_INCLUDE_TESTS"]`,
  `overrideReferences` + `nunit.framework.dll`. Editor-only, never ships in a build. Fully deletable.
- 49 EditMode tests, all green (run via MCP `run_tests` assembly `ProceduralPlanets.Tests.EditMode`,
  or the Unity Test Runner window). Commits 0a3a23e, fb472c7, de60683.
  - `ScatterIdTests` (12): u64 persistence-key pack/unpack roundtrip, boundaries, player bit, bit-63
    spare, field isolation, Pack range validation. See [[project-scatter-lod-impostor]] for SlotId.
  - `ScatterLibraryDtoValidationTests` (17): every `EnsureValid` invariant incl the duplicate-SlotId
    case that previously only surfaced in a play-test.
  - `ScatterHashTests` (9): **golden values** pin Mix/Node/Slot exactly — the persistence contract (a
    saved world's props are re-derived from these hashes; a changed mix constant silently moves every
    prop). Plus determinism, To01 [0,1) + low-24-bit masking, distinctness.
  - `ScatterPlacementMathTests` (11): the CPU/GPU-parity TryPlace gates.

**Approach that worked:** target pure/deterministic structures with no Unity-asset coupling. DTOs are
`record`s — build one valid instance, flip one field with `with { }` per test. Golden-value tests are
the right tool for anything whose output is a persistence contract. No ScriptableObjects or asset
loading needed for any of these.
