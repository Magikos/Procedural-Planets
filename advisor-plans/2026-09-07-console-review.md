# Console and command review

Implementation was subsequently approved and completed on 2026-09-07.
See [the implementation record](../docs/design/2026-09-07-console-improvements.md) for delivered scope and validation.
The findings-only statements below describe the original review, not the later implementation.

Findings only — no code changed.

Reviewed 2026-09-07 on `harvest-vertical-slice`, HEAD `d1e0f62`, with other agents' changes present.
Source reads, rather than Unity execution, support these findings.
The inventory found 264 `[ConsoleCommand(` declarations in 35 source files and 30 console script assets.
These are source counts, not a measured runtime registry count.
Thirty distinct `CommandPrefix` values exist. Built-ins also declare names such as `console.dump` directly.

Scope: console registration, execution, parsing, completion, input, help, scripts, and selected domain commands.
Excluded: full renderer inspection, every domain setter, player-build stripping, and runtime performance measurements.
No Unity commands, tests, imports, builds, or scene changes ran.

## Main recommendation

Keep the existing registry and command names. Improve discovery through grouped help and consistent metadata.
Fix execution results before relying on scripts to validate later changes.
Keep command implementations beside their domain owners. A folder move alone would not improve command discovery.

## Findings

All seven findings have HIGH confidence from current source. Runtime reproduction remains pending.
Effort: S means a local change; M means coordinated changes across console components or domain commands.
Risk describes the proposed fix, not the current defect.

| ID | Finding | Category | Severity | Effort | Fix risk |
|---|---|---|---|---|---|
| C1 | Rejected domain operations report successful execution | Bug | High | M | Medium |
| C2 | Immediate execution rejects async commands after invoking them | Bug | High | S | Low |
| C3 | Dump names can escape the advertised output folder | Bug | Medium | S | Low |
| C4 | Completion does not follow the parser's argument rules | Bug | Medium | M | Medium |
| C5 | Help exposes a flat list without command groups or alias relationships | Maintainability | Medium | M | Low |
| C6 | Command conventions differ across domains | Maintainability | Medium | M | Medium |
| C7 | Clipboard filtering joins separate tokens and lines | Bug | Medium | S | Low |

### C1 — Make command failures machine-readable

Evidence:

- `Assets/Scripts/Planet/PlanetWaterSurface.cs:365`: `SetCmd` returns error text when a field is invalid.
- `Assets/Scripts/Core/Services/CameraTeleportStore.cs:66`: an unknown teleport returns error text.
- `Assets/Scripts/Core/Console/Registry/CommandExecutor.cs:171`: ordinary return values become successful results through `ToString()`.
- `Assets/Scripts/Core/Console/Scripting/ConsoleScriptRunner.cs:281`: script continuation depends on `result.Success`.

Trigger: a script runs `water.set MissingField 1`, then captures the scene.
The failed setting still returns success to the script runner. The capture can record unintended state.
An invalid camera teleport has the same problem.

Recommendation: extend the existing result path to accept explicit command failures.
Keep string and void command support. Convert validation failures in small domain batches.
Do not infer failure by matching words in output. Do not create a second script executor.
Validate synchronous and asynchronous results before expanding the migration.

Behavior change: scripts stop after rejected operations. Existing scripts that depended on continuation need review.
Refactor option: reuse `ConsoleCommandResult` or a small domain outcome mapped into it.

### C2 — Reject async sidecar commands before invocation

Evidence:

- `Assets/Scripts/Core/Console/Registry/CommandExecutor.cs:79`: `ExecuteImmediate` calls `Invoke` before checking `IsAsync`.
- `Assets/Scripts/Core/Console/Registry/CommandExecutor.cs:143`: `Invoke` executes the method.
- `Assets/Scripts/Core/Console/Scripting/ConsoleScriptRuntime.cs:126`: sidecar collection calls `ExecuteImmediate`.

An async command can start work before the sidecar reports that async execution is invalid.
The returned awaitable then has no normal execution owner.

Recommendation: resolve metadata and enforce immediate-execution restrictions before invoking the method.
Reuse command lookup and binding. Avoid duplicating the execution pipeline.
Behavior change: invalid async sidecars produce no command side effects.
Refactor option: one shared preparation step if necessary.

### C3 — Constrain console dump names

Evidence: `Assets/Scripts/Core/Console/Commands/ConsoleBuiltins.cs:81` combines the supplied name with the output folder.
Line 86 writes that path with overwrite behavior.
The parameter description promises a filename without a path, but the method does not enforce it.

Parent-relative and rooted inputs can write outside `local-only/console-dumps`.
This is a local debug-console file-write boundary, not evidence of a remote attack surface.

Recommendation: reject rooted paths, separators, parent-directory components, and invalid filenames.
Check the resolved destination remains inside the intended directory before writing.
Keep documented overwrite behavior for valid filenames.
Behavior change: path-shaped names become errors. Refactor option: None.

### C4 — Share argument interpretation with completion

Evidence:

- `Assets/Scripts/Core/Console/Registry/CommandParser.cs:98`: a final string consumes all remaining tokens.
- `Assets/Scripts/Core/Console/Intellisense/IntellisenseEngine.cs:72`: completion instead counts tokens as parameters.
- The same file, line 94, rebuilds preceding arguments from tokens without preserving their quotes.
- `Assets/Scripts/Core/Console/ConsoleInputController.cs:353`: accepting a suggestion replaces the whole input.

For example, `camera.teleport Grass Face` is one valid string argument for execution.
Completion counts two arguments and stops offering saved names.
A space inside an unfinished quoted name also advances the completion parameter slot incorrectly.
Token counts likewise cannot describe parsers that consume multiple tokens for one vector.

Recommendation: retain source spans and quote state when tokenizing.
Use the binding rules to identify the active argument, including final strings and vectors.
Replace only that argument when accepting completion.
Preserve the existing final-string convenience and Enter acceptance behavior.
Behavior change: completion stays valid while editing multiword values. Refactor option: shared token context.

### C5 — Add grouped discovery to the existing registry

Evidence:

- `Assets/Scripts/Core/Console/Commands/ConsoleBuiltins.cs:136`: bare `help` prints every alias alphabetically.
- `Assets/Scripts/Core/Console/Registry/CommandData.cs:4`: metadata contains no group, canonical alias, examples, or effects.
- `Assets/Scripts/Core/Console/Intellisense/IntellisenseEngine.cs:26`: suggestions stop at 200 entries.
- `Assets/Scripts/Core/Console/Scripting/ConsoleScriptRunner.cs:118` and `:133`: `script.run` and `script.run-script` appear as separate entries.

The command surface now exceeds the suggestion cap in source declarations.
Do not claim an exact truncated runtime count until the registry is measured.
The flat list gives users little help choosing between related systems or distinguishing aliases.

Recommendation: make bare help show groups, counts, and brief descriptions.
Keep `help <prefix>` and `help <command>`. Add explicit full-list and search views.
Search names and descriptions. Show when results are capped, or paginate them.
Store alias relationships once so help can collapse them while execution still accepts them.

Behavior change: bare help becomes an index. Existing command execution remains compatible.
Refactor option: extend `CommandData` and its attributes only where multiple consumers need the metadata.

### C6 — Standardize meanings before renaming commands

Evidence:

- `Assets/Scripts/Planet/SurfacePathDebugCommands.cs:120`: `path.clear` keeps saved stamps.
- The same file, line 376: `scorch.clear` deletes saved stamps.
- `Assets/Scripts/Planet/PlanetWaterSurface.cs:365`: `water.set` accepts a field name without a completion provider.
- `Assets/Scripts/Planet/Trees/TreePreview.cs:70`: species help tells users to enter a bad name to list choices.
- `Assets/Scripts/Planet/CelestialManager.cs:212`: `time.speed` means day length in seconds.
- `Assets/Scripts/Core/Services/DebugCaptureController.cs:276`: precipitation help still claims the P key.

The same verb can imply different persistence effects. Discoverable choices and units also vary.

Recommendation: document a small command contract and apply it to touched domains.
Use no-argument reads for settings, explicit units, completion for finite choices, and visible persistence effects.
Keep clear/reset/delete distinctions explicit. Avoid changing existing commands' effects under their current names.
Add clearer canonical names only when needed, retaining the old names as aliases.
Update keybinding descriptions from the current input bindings before publishing help.

Behavior change: any renamed canonical command needs compatibility aliases; existing meanings remain unchanged.
Refactor option: use existing completion providers as the pattern for water fields and tree species.

### C7 — Handle clipboard separators deliberately

Evidence: `Assets/Scripts/Core/Console/ConsoleInputController.cs:412` drops characters below U+0020 during paste.
Tabs and newlines disappear rather than separating tokens.
For example, clipboard text containing `water.set<TAB>MissingField<TAB>1` becomes `water.setMissingField1`.
Multiple command lines become one invalid or unintended command.

Recommendation: convert tabs to spaces. Reject multiline command pastes with a useful script hint initially.
Add a reviewed batch-paste preview only if users need it later.
Behavior change: multiline paste receives an explicit response. Refactor option: None.

## Proposed help groups

These groups organize discovery. They do not replace command prefixes.

| Help group | Existing command families |
|---|---|
| Console and scripts | `help`, `echo`, `clear`, `quit`, `console.*`, `script.*` |
| Camera and character | `camera.*`, `character.*`, `scale.*` |
| World and surface | `planet.*`, `biome.*`, `climate.*`, `water.*`, `path.*`, `scorch.*` |
| Sky and weather | `time.*`, `light.*`, `atmosphere.*`, `cloud.*`, `weather.*`, `precipitation.*`, `rain-particles.*`, `lightning.*` |
| Vegetation and wildlife | `grass.*`, `scatter.*`, `tree.*`, `plant.*`, `rock.*`, `creature.*`, `fish.*` |
| Diagnostics and authoring | `debug.*`, `quality.*`, `bench.*`, `test.console.*` |
| World action history | `action.*` |

Within each family, show queries first, settings second, actions third, and diagnostics last.
Derive the family from the prefix. Add explicit grouping only for exceptions or broader help groups.
Show usage, units, accepted values, examples, and regeneration or persistence effects in detailed help.
Retain test commands in an explicit group rather than removing them.

## Recommended sequence

1. Fix C1, C2, and C3. Establish reliable failure and file-write behavior.
2. Fix C4 and C7. Preserve existing input and script semantics except the listed corrections.
3. Implement C5 using current prefixes. Generate help and an exported catalog from the same registry metadata.
4. Apply C6 in small domain batches. Audit the 30 saved scripts before any command migration.

This sequence is a proposal. No implementation plans or product changes were started.

## Expanded direction — IntelliSense, structure, and production

Bryan requested these additions on 2026-09-07. They supersede the earlier deferral of per-command release classification.
Classification is now in scope. Selecting shipped commands and implementing access controls remain design work.

### IntelliSense behavior

The target is contextual assistance throughout command entry, including edits inside an existing line.
Keep the current Tab and Enter acceptance convention unless Bryan chooses another interaction.

| Priority | Improvement | Expected behavior |
|---|---|---|
| 1 | Cursor-aware completion | Editing an earlier argument preserves everything after it |
| 1 | Shared argument context | Execution, suggestions, syntax colors, and hints agree on quotes, vectors, and final strings |
| 1 | Active-argument help | Show the current argument's name, meaning, units, allowed range, and optional status |
| 1 | Domain value completion | Offer water fields, tree species, saved locations, scripts, capture sets, and enum values |
| 2 | Hierarchical command browsing | An empty input can show groups; `camera.` shows camera subgroups and commands |
| 2 | Better search ranking | Rank exact names, prefix matches, segment matches, then description matches |
| 2 | Command details | Show a short description, example, release classification, and material effects beside the selected suggestion |
| 2 | Explicit unavailable state | In development, explain missing world services or restricted access instead of suggesting an apparently runnable command |
| 2 | Stable results | Preserve the selected command while refreshing results; disclose additional pages |

Validation must never execute a command to discover its current value or availability.
Resolve dynamic choices through existing read-only domain APIs.
Add contextual completion inputs only when providers need earlier arguments, the cursor, or world context.
Do not introduce an independent parser for the UI.

Additional evidence:

- `Assets/Scripts/Core/Console/Intellisense/IntellisenseEngine.cs` accepts input text without a cursor position.
- `Assets/Scripts/Core/Console/ConsoleInputLineFormatter.cs` independently derives argument hints from token counts.
- The formatter recognizes double-quoted strings while `CommandParser` accepts both quote styles.
- `Assets/Scripts/Core/Console/Registry/ParameterData.cs` already supplies shared signature formatting and parameter descriptions.
- `Assets/Scripts/Core/Console/Intellisense/CompletionRanker.cs` and `IntellisenseEngine.SuggestAliases` duplicate prefix/substring ranking rules.

Extend these shared mechanisms. Recompute suggestions when input, cursor, registry, or relevant choices change.
The current input controller requests suggestions every normal update when they are not frozen or suppressed.
Treat reduced allocation as a hypothesis until profiling verifies the result.

### Command structure

Separate three concerns: the domain operation, its console adapter, and its discovery metadata.
Domain services retain behavior and validation. Console adapters handle text input and output.
Metadata supplies help groups, canonical names, aliases, and release classification.
Do not make gameplay depend on command strings or reflection.

Keep the current dot syntax. Introduce deeper names only where they make a family easier to navigate.
The following names are proposals, not registered commands:

| Current names | Proposed canonical family | Compatibility |
|---|---|---|
| `camera.teleports`, `camera.save-teleport`, `camera.remove-teleport`, `camera.teleport` | `camera.location.list/save/remove/go` | Preserve all current names as aliases |
| `debug.capture-set`, `debug.cycle-capture-set`, `debug.capture` | `debug.capture.set/next/run` | Preserve current capture scripts |
| `tree.gen` | `tree.preview.generate` | Keep `tree.gen` as an alias |
| `path.clear`, `path.clear-saved` | `path.cache.clear`, `path.saved.clear` | Preserve the distinct existing effects |

Do not rename every family for symmetry. First inventory names and identify actual ambiguity or crowded families.
One canonical descriptor should own its aliases. Help should display aliases under that descriptor.
Aliases must inherit access restrictions, effects, and parameter definitions.

Source restructuring should follow responsibilities, not prefixes alone.
Extract a domain-local adapter when parsing, formatting, and console metadata obscure the domain implementation.
Keep small wrappers inline where they remain clear. Avoid a central file containing every domain's commands.

### Production classification

Current evidence: `DebugConsoleBootstrap.Initialize` runs an unrestricted `ConsoleRegistry.Scan` when the console is enabled.
`IsConsoleAllowed` permits the Editor, development builds, or a release process with `--allowDebug`.
`CommandData` and `ConsoleCommandAttribute` contain no per-command release policy.
Therefore, hiding the console by default does not classify commands or constrain a release console enabled by that flag.
This describes current behavior, not a remote security finding.

Track purpose separately from release eligibility:

| Metadata | Proposed values | Meaning |
|---|---|---|
| Purpose | Query, Setting, Action, Diagnostic | What the command does; supports help ordering |
| Release policy | Unreviewed, DevelopmentOnly, Player, Administrator | Where the command is eligible to execute |

`Unreviewed` is useful during inventory. It must deny execution in production.
`Administrator` records intended privileged use. It grants no access until a trusted authorization mechanism exists.
Release policy is not inferred from a prefix, a public method, or whether a method returns a string.
Queries can expose privileged state; settings can affect the whole world.

| Existing family or example | Initial classification proposal | Reason or review required |
|---|---|---|
| `help`, `echo`, console layout and history utilities | Player candidates | Only if a player console ships; help must show only permitted commands |
| `quality.*` | Player candidates | Verify each command affects local presentation rather than shared world state |
| `console.dump` | Unreviewed | Review file destination and whether logs contain information unsuitable for players |
| `camera.speed`, `camera.teleport` | DevelopmentOnly initially | Current commands control a free camera; a future photo mode needs its own eligibility rules |
| `planet.generate`, weather forcing, creature kill, surface record deletion | DevelopmentOnly initially | Potential future admin operations require authority checks and defined effects |
| `test.console.*`, `bench.*`, preview galleries, shader proof controls | DevelopmentOnly | Development and authoring tools |
| `action.undo/redo`, creature loot, surface painting | Unreviewed | Domain operations may ship; current console entry points are not automatically player features |
| `script.run` | DevelopmentOnly initially | A production script facility needs policy checks for every command, defer, and sidecar |

These are provisional decisions, not a completed classification of all 264 declarations.
Export a registry-derived catalog with canonical name, aliases, owner, purpose, release policy, and review rationale.
Include persistence, regeneration, and world-mutation notes where applicable.
Use the catalog to make `Unreviewed` commands visible during development and release review.

Enforce release policy before invocation in the shared executor.
Cover interactive entry, programmatic entry, scripts, defers, and immediate sidecar execution.
Help and completion must use the same eligibility policy, but hiding a command is not enforcement.
`--allowDebug` must not grant production authority merely because it opens the UI.
If development implementations must be absent from a shipping binary, add separate build exclusion after classification.
Runtime denial alone does not prove code removal.

For multiplayer, authoritative domain services must validate mutations regardless of console policy.
The agent systems design already requires authority validation before mutation.
Do not invent a networking transport or an admin authentication system within this console improvement.

### Revised sequence and queued checks

1. Inventory canonical names, aliases, parameter semantics, and provisional release classifications.
2. Fix execution outcomes and immediate-execution restrictions; add shared release-policy enforcement when implementing classification.
3. Build shared argument context and cursor-aware IntelliSense.
4. Add grouped discovery, detailed hints, and registry-derived catalog export.
5. Migrate selected command families through aliases and extract only justified domain adapters.

Additional Unity and build checks remain queued, NOT RUN:

- Editing a middle argument preserves following arguments and the cursor position.
- Hints, completion, and execution agree for both quote styles, vectors, and final-string arguments.
- Legacy aliases execute the same implementation with the same access policy.
- Editor, development player, release player, and release-with-flag configurations obey their intended policies.
- Direct executor calls cannot bypass restrictions that hide commands in help.
- Scripts, defers, and sidecars cannot invoke forbidden commands through aliases.
- Unauthorized world mutations fail before changing state when an authority layer exists.

Open product decision: whether players receive a console at all.
Classification and improved development tooling can proceed before that decision.
No command becomes production-approved solely because it is listed as a candidate here.

## Manual validation queue — pending Unity availability

This is a documented queue, not a scheduler job. Do not acquire or operate Unity while another agent owns it.
All entries remain NOT RUN. No runtime pass is claimed.

| Order | Check after the relevant implementation | Required result |
|---|---|---|
| 1 | Compile affected assemblies after Unity ownership is released | No new compile errors |
| 2 | Run a script with an invalid field followed by `echo SHOULD_NOT_RUN` | Error result; echo does not run; registered defers run |
| 3 | Repeat with an unknown saved camera location | Same stop behavior |
| 4 | Exercise immediate execution with a harmless instrumented async command | Rejected before the invocation counter changes |
| 5 | Check valid and invalid dump names with temporary destinations | Valid output remains in dump folder; rejected names create nothing |
| 6 | Complete quoted and unquoted multiword camera names | Correct suggestions and executable accepted text |
| 7 | Check vector arguments, cursor edits, empty strings, and existing expression input | Arguments and following text remain intact |
| 8 | Paste tabs and multiple lines into the console | Tabs preserve boundaries; multiple lines receive the chosen explicit response |
| 9 | Open grouped help, exact help, search, and full listing | All runtime commands remain discoverable; aliases remain executable |
| 10 | Run existing console async success, failure, cancel, and abandon probes | Existing semantics and error reporting remain correct |

Use existing EditMode test infrastructure for regression checks where appropriate.
No new test framework is needed. Interactive checks must also cover console focus and gameplay input restoration.
Use harmless fixtures for async rejection; never use planet generation or quit as the rejection probe.

## Prior findings and rejected directions

- The historical per-domain consistency gap remains OPEN; C6 provides current examples.
- Human-readable cloud parameters were already corrected. `cloud.density` still documents 0–1 input; do not redo that migration.
- Async exception logging was marked resolved in the consolidated July audit. C1 concerns returned domain errors, not thrown exceptions.
- The completion cap was previously deferred. Reconsider it with grouped discovery because the source command count has grown.
- Keep the current debug-console bootstrap gate. Per-command release stripping remains outside this request.
- Do not remove world action commands or surface-edit wrappers. They have documented behavior and planned consumers.
- Do not move every command into a central Commands directory. Domain ownership is an established project convention.
- The historical console slice audit referenced by memory is absent at its recorded path. Its numbered findings could not be independently reconciled.
- No console-specific tests matched the `Console`, `Intellisense`, or `CommandParser` source search under `Assets/Tests`.

## Review limits

Graphify returned broad debugging references rather than the needed console implementation nodes.
Direct source reads supplied the evidence above.
The working tree may change while other agents continue. Recheck cited lines before implementation.
No graph update was necessary because this review changed no code.

## Unity baseline validation — 2026-09-07

Bryan released Unity for this review. The earlier NOT RUN statements describe the proposed post-implementation queue.
The following baseline probes have now run in Unity 6000.7.0a5 through in-memory C# execution.
These were targeted probes, not Unity Test Runner tests.

The active scene was `Assets/Scenes/Tests/WolfDeerEncounter.unity` in Play mode.
Its console registry was empty before testing. The probe temporarily scanned commands and restored the original registry afterward.
No scenes, product scripts, assets, saved settings, or world state were changed.

| Probe | Observed result | Conclusion |
|---|---|---|
| Registry scan and `help ` completion | 264 registered commands; 200 suggestions | C5 cap reproduced; 64 commands absent from this unfiltered popup |
| `script.run Grass` | 14 suggestions; binding succeeds | Initial completion works |
| `script.run Grass Baseline` | Zero suggestions; binding succeeds with one string argument | C4 final-string mismatch reproduced |
| Unfinished quoted `script.run "Grass ` | Zero suggestions; binding succeeds | C4 quoted-space mismatch reproduced |
| Unfinished quoted `script.run "Grass B` | Five suggestions | Completion resumes after a non-space character |
| `echo console-review-ok` | Success with expected output | Basic execution passes |
| Unknown alias | Failure with `unknown command: 'not-a-real-console-command' - try 'help'` | Unknown-command rejection passes |
| `path.paint` with no registered surface path service | `Success=true`; output `path paint requires an active surface path service` | C1 domain-error mismatch reproduced without painting |
| Instrumented harmless async command through immediate execution | Invocation counter reached 1 before rejection | C2 reproduced |
| Clipboard containing echo, tab, alpha, newline, beta | Input became `echoalphabeta` | C7 separator loss reproduced |

The async probe returned `__console_review_async_probe: async commands are not valid in immediate execution`.
Its zero-duration work completed immediately. The temporary alias and instance registration were removed in `finally`.
The clipboard probe used an isolated input controller and restored the original clipboard in `finally`.
Final inspection confirmed the original scene remained in Play mode and the registry returned to zero commands.

C3 remains source-verified; no file escape was attempted.
Full interactive rendering, focus restoration, async cancellation, script defer integration, and release-build policy checks remain pending.
Proposed new behavior cannot pass acceptance checks until implementation exists.
