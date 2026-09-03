# Memory Index

One line per memory. The hook says when to open the file — the detail lives in the file,
including a verbatim "Index digest" of the longer entry this line replaced (2026-08-26).
Keep entries under ~200 chars; this file loads into every session.

## Feedback — how Bryan wants work done

- [Never discard uncommitted work](feedback_never_discard_uncommitted_work.md) — `git checkout`/`restore`/`reset` destroyed prior-session work in this permanently-dirty tree. Park with a scratchpad copy or a path-scoped stash FIRST. A restore verified against a session summary MISSED a line for 9h — verify against `git diff`.
- [Response brevity](feedback_response_brevity.md) — Default reply = two sections: **Done** (one line each) and **You** (numbered action items, or "nothing"). No evidence, code or rationale unless asked. Governs the REPORT, never the work.
- [Quality over cheap](feedback_quality_over_cheap.md) — "Do it properly, always my pick." Take the root-cause fix; never offer cheap-vs-proper as a question. Overrides lazy/minimal-diff defaults.
- [Goal-first scoping](feedback_goal_first_scoping.md) — Prerequisites become tasks, never reasons to defer a goal. Scope docs discover work; they don't trim goals.
- [Placeholder art while building](feedback_placeholder_art_while_building.md) — Placeholder ART yes, placeholder MECHANICS no. Never gate a mechanic on an art import.
- [Identify before fixing](feedback_identify_before_fixing.md) — Colour the artifact and agree the target before more fixes. Escalate after ONE failed fix; suspect your own recent commits first.
- [Adversarial review verification](feedback_adversarial_review_verification.md) — Verify a reviewer's claims against the tree with parallel agents before accepting. Don't take review on faith.
- [Audit & review workflow](feedback_audit_workflow.md) — How audits are run and reviewed before any fixing starts.
- [Camera teleport wedges the Editor](feedback_camera_teleport_wedges_editor.md) — A long HORIZONTAL jump re-plans ~77k scatter tiles and hangs Unity. A vertical descent over the same spot is cheap; test the arc, not the distance. Unattended play verification IS viable.
- [No coroutines, use Awaitable](feedback_async_no_coroutines.md) — All async work uses Awaitable, never Unity coroutines.
- [Settings DTO pattern](feedback_settings_dto_pattern.md) — Settings SOs are editor-only; runtime reads immutable snapshot DTOs, never the SO.
- [Subsystem decomposition wiring](feedback_subsystem_decomposition_wiring.md) — Split god-class internals into interfaces + orchestrator injection. ServiceLocator/EventBus are cross-subsystem only.

## Direction and vision

- [Game vision](project_game_vision.md) — The planet is substrate for a Valheim-inspired wizard RPG. Asset rule: HARVEST-ONLY, no vendor runtime C# ever ships.
- [Magikos game architecture](project_magikos_architecture.md) — Doc of record for the game arc. 8-player multiplayer is a HARD constraint; authority belongs to the SERVER ROLE, so no authoritative code touches camera, input or GPU.
- [Gameplay roadmap](project_gameplay_roadmap.md) — Pivot from look-polish to gameplay. NOTE: the old "SlotBits=6 aliases slots 64-67" warning is RESOLVED — do not re-raise it.
- [All-generated props direction](project_all_generated_props.md) — Replace every Synty scatter prop with generated geometry. FINAL GOAL NOT BUILT: bake generated meshes at build time.
- [M0 foundations](project_m0_foundations.md) — 5-lane foundation slice. `Magikos.Game` ships with ZERO asmdef references, so "authority touches no camera/input" is a compile error.
- [World delta log](project_world_delta_log.md) — Append-only save log + EntityId. GOTCHA: a ScatterId and an EntityId can be the SAME number; the keyspace must be split.
- [Creature residency spine](project_creature_residency.md) — Residency, hunting, carcasses, swarms, birds. Life support is owned by OBSERVATION. **A slot key carries no species — species need disjoint slot runs or one silently never spawns.** Also: a point light lights NOTHING here. `creature.ambience` says why a swarm is absent where you stand; fireflies want sun < 0.02 and are NOT in LakeShore.
- [Current work focus](project_current_focus.md) — Code-refactor arc complete; audit backlog closed; biome arc paused.
- [Code refactor arc](project_code_refactor_arc.md) — Codebase-wide audit-first refactor on branch `code-refactor`; arc complete.
- [Skill library](project_skill_library.md) — 16-skill library at .agent-skills/ with a README router. Load skills before project work.

## Rendering — solved defects and their traps

- [Scatter dither grain](project_scatter_dither_grain.md) — SOLVED: `_Bayer4x4` started at `0.0`, killing 1 px in 16 at zero fade. Traps: brightness metrics aren't exposure-invariant; RenderMeshIndirect snapshots the MPB at submit.
- [Face-UV inverse defect](project_face_uv_inverse_defect.md) — `UnitSphereToCubeFace` looked like an inverse and wasn't. Three cube-face UV conventions exist in the tree and are NOT interchangeable — always round-trip-test a new one.
- [Water shore + horizon rendering](project_water_shore_rendering.md) — Lake "blocks" were the GRASS water-fade, not biomes. "See through the water" = Ocean SURFACE renders sky over a lake volume it hides. Horizon band = `FarTerrainWaterlineMask` keyed on the GLOBAL sea sphere, so it is dead above any perched lake.
- [Water architecture build](project_water_architecture_build.md) — ~92 lakes at their own spill heights. One rule explains every bug: a consumer asking where sea level is globally when it should ask where water is here. Latest instance (2026-08-29): "lake under the lake" was character GROUNDING, not water.
- [Water tech research](project_water_tech_research.md) — Our water is stronger than prior docs claim; "no waves on the sphere" is STALE. Lists dead ends so nobody re-searches.
- [Ocean scatter — SHIPPED](project_ocean_scatter.md) — DO NOT repeat "scatter cannot place below the waterline"; it is FALSE. Altitude is signed, so a depth band is an ordinary altitude gate.
- [Ocean wave approach](project_ocean_wave_approach.md) — Displace the existing mesh, NOT a camera-following patch.
- [Scatter LOD + impostor](project_scatter_lod_impostor.md) — Canopy "tiny holes" were the mesh-LOD crossfade; BARE distant canopies were sub-pixel alpha test, not LOD. SEE-THROUGH horizon trees: mesh and card can NEVER cross-dither — silhouettes disagree per pixel. Atlas bakes must run in PLAY MODE. Mesh-to-card handover is a SIZE (36 px), not a distance. ParallelAlign is the crown-width lever. Cards SELF-SHADOWED through their own light-facing caster quad; fixed 2026-09-03. SOLVED 2026-09-03: one atlas per PROTOTYPE at OctGridN 4, not one per species — the old share key drove grove grouping AND atlas identity, so 135 of 176 props drew a card baked from a different mesh. Fewer angles did NOT hurt (IoU 0.627→0.655, cov 1.108→0.973, clean rows 48→123). AtlasCellPx must never move; gridN can.
- [Scatter prop lighting](project_scatter_dusk_lighting.md) - "Black dots" are stage-specific. SOLVED 2026-08-27: props CAST shadow but never RECEIVED it. Form shading and cast shadow must be SEPARATE multiplies.
- [Scatter clumping](project_scatter_clumping_direction.md) — Clumping authored on all 79 prototypes. GOTCHA: clumping COSTS ~30% headcount, so weights carry a compensation factor. Re-author ⇒ redo it.
- [ShadePreference siting](project_shade_preference_siting.md) — Props can be sited relative to tree cover. The crossover is at −0.5, NOT 0. Measure against the shared openness field, not one prototype.
- [Distant grass carpet](project_distant_grass_carpet.md) — The far grass "blanket" already exists but is disabled by a hard biome-edge gate. Lake1 is ARID, so the carpet won't green it.
- [Grass layering arc](project_grass_layering_arc.md) — Far-field blanket revived; textured cards reverted; verify grass on a REAL planet, not the grid test scene.
- [Grass/terrain lighting arc](project_grass_terrain_lighting_arc.md) — Bright-green biome-edge line was the terrain grass-surface overlay. GOTCHA: shader edits don't hot-reload; force AssetDatabase.ImportAsset.
- [Chunk biome seam](project_chunk_biome_seam.md) — Known polish issue: faint chunk-boundary seams; the blend kernel can't see across chunk borders.
- [Normal mapping flat](project_normal_mapping_flat.md) — Terrain still looks flat; data pipeline confirmed working, lighting compression is the likely cause.
- [Cloud/weather visual arc](project_cloud_weather_arc.md) — Cloud profiles + rain-shaft fixes shipped; clouds parked needing polish. `weather.force` flattens the source map — use `weather.regenerate`.
- [Planet look-dev](project_planet_look_dev.md) — Synty-look pass: post enabled and graded, ambient lifted, scatter densified.
- [Valheim look pass](project_valheim_look_pass.md) — The gap is mostly NOT tree geometry; it is fog + grass + light + wind. NEGATIVE RESULT: raising grass saturation/brightness stripped the ground to bare dirt.
- [Surface props + lighting](project_surface_props_lighting.md) — `Planet/PropLit` is the reusable planet-aware prop shader for ALL surface objects and NPCs, not URP Lit. Floating scatter is ORIENTATION, not height.
- [Scatter biome buildout](project_scatter_biome_buildout.md) — All 14 land biomes have scatter. FoliageLit rules and the per-biome slot convention live here.
- [Lake biome](project_lake_biome.md) — Lakes are their own biome (Lake/LakeShore) vs ocean, flood-filled at generation.
- [Custom tree generator](project_tree_generator.md) — Procedural trees with per-instance variety; variants share one impostor atlas. Also: `Scatter/FoliageLit` has NO `_BaseColor` — its leaf tint is `_SeasonColor`.

## Performance

- [Runtime hitch profile](project_runtime_hitch_profile.md) — SOLVED: travel stutter was tile re-plan + matrix re-copy. CLEARED, don't re-investigate: GC, chunk mesh page-in, gather job wait. Editor overhead was most of the apparent problem.
- [Startup generation perf](project_startup_generation_perf.md) — Planet gen 76.3 s → ~40.3 s. Wins came from DELETING redundant work, not from Burst. Two Codex plans rejected on evidence.
- [Scatter gather perf](project_scatter_gather_perf.md) — Fly-feedback round: gather now completes inline on main (off-main Complete throws). GOTCHA: a DTO/compile-time const needs a clean stop→play.
- [Grass + chunks research](project_grass_chunks_research.md) — Phase 8 source of truth; Phase A done, Phase B design drafted.

## Tooling and environment

- [Unity MCP — you can drive the editor](reference_unity_mcp.md) — MCP IS CONNECTED; don't ask Bryan to run what you can run. Auto-refresh is OFF, and HotReload WEDGES compilation when you add new .cs files — the fix is in this file.
- [ffmpeg / watching video](reference_ffmpeg_video.md) — ffmpeg turns a recording into frames. GOTCHA: frame-diff CANNOT find pop-in — it ranks the NEAREST objects. Judge a pop only from a wide FULL-res crop.
- [Test harness](project_test_harness.md) — EditMode tests exist and are used. Bryan asked for TDD, which overrides the old CLAUDE.md "no test framework" rule.
- [Testing stance](project_testing_stance.md) — SUPERSEDED by CLAUDE.md: a test has to earn its place. Kept for history.
- [Human-readable console params](project_human_readable_console_params.md) — Cloud/atmo/precip/weather commands converted to 0-1; convention promoted into pp-change-control §4.
- [Console arc](project_console_arc.md) — Debug console shipped (~60 cmds, 13 prefixes); audit findings must be reviewed before fixing.
- [Collision strategy](reference_collision_strategy.md) — Analytic raycast for cheap ground queries + streamed per-chunk MeshColliders in a bubble for real physics. ONE ground-truth surface.
- [Character + terrain-relief plans](project_character_terrain_plans.md) — plans/001 character MVP + plans/002 terrain-relief diagnosis, both revised after review.
- [External asset library](reference_external_asset_library.md) — 50 unimported packs; ~7000 humanoid clips exist (round 2 reversed round 1). Lists build-breaking traps and prior art for sphere locomotion.
- [State Machine project (prior art)](reference_state_machine_project.md) — External unfinished controller; harvest PATTERNS not code. Its hierarchical FSM is the Phase-10 backbone, not MVP.
- [Planet Architect reference](reference_planet_architect.md) — External biome/climate/vegetation reference; analysis paper in docs/research/.
- [local-only reference material](reference_local_only.md) — External projects and papers that source the key features.
- [Agent conversation](reference_agent_conversation.md) — `docs/agent-conversation/` is the shared cross-agent scratchpad.
