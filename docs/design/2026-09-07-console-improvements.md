# Console improvements

Bryan approved implementation on 2026-09-07 after the console review and Unity baseline probes.

## Using the console

- Tab on empty input lists command families. A second Tab opens the selected family. Enter on empty input does nothing.
- Completion works at the cursor. Accepting a value preserves later arguments and places the cursor after the inserted value.
- Quoted names, final string arguments, and vector arguments use the same token and binding context as execution.
- Suggestions include matching command descriptions after name matches. The popup scrolls through all matches without a 200-entry cap.
- The selected command shows its description and release policy. The input hint identifies the active argument.
- Water float and color fields and tree species have value completion. Existing script, camera, capture, enum, and boolean providers remain available.
- Tab characters paste as spaces. Multiline pastes are rejected with a script hint instead of joining separate commands.

## Help and organization

| Command | Result |
|---|---|
| `help` | Purpose groups and their command families |
| `help camera` | Matching camera commands |
| `help water.set` | Signature, parameter descriptions, aliases, and release policy |
| `help Player` | Commands permitted in a release console |
| `help Unreviewed` | Commands that still require a production decision |
| `help all` | Complete permitted command listing |
| `help <text>` | Search names, descriptions, groups, and aliases |
| `console.catalog` | Tab-separated catalog with owner and release policy |
| `console.catalog true` | Export `local-only/console-dumps/command-catalog.tsv` |

Help completion includes families, groups, release policies, and command names.
The catalog comes from the runtime registry. It does not duplicate command definitions in documentation.

Structured names are now canonical. Previous names remain aliases with the same descriptor and access policy:

| Canonical command | Compatibility alias |
|---|---|
| `camera.location.go` | `camera.teleport` |
| `camera.location.save` | `camera.save-teleport` |
| `camera.location.remove` | `camera.remove-teleport` |
| `camera.location.list` | `camera.teleports` |
| `path.cache.clear` | `path.clear` |
| `path.saved.clear` | `path.clear-saved` |
| `console.help` | `help` |
| `console.echo` | `echo` |
| `console.clear` | `clear` |
| `console.quit` | `quit` |

The existing `run-script` alias now shares the `script.run` descriptor.
The earlier review incorrectly called that alias `script.run-script`; the source declares the unprefixed `run-script`.

Command adapters stay beside their owning systems. No centralized domain-command module was introduced.

## Release policy

`ConsoleCommandAttribute.ReleasePolicy` defines an individual policy.
`CommandPrefixAttribute.ReleasePolicy` supplies a class default.
An unspecified policy is `Unreviewed`. The command contract now rejects this policy during registration and build validation.

| Policy | Editor/development | Release |
|---|---|---|
| Unreviewed | Declaration rejected | Declaration rejected |
| DevelopmentOnly | Allowed | Denied |
| Player | Allowed | Allowed |
| Administrator | Allowed | Denied until trusted authorization exists |

Eight commands are player-eligible: `help`, `echo`, `clear`, `quit`, `console.anchor`, `console.scrollback-size`, `console.cancel`, and `console.abandon`.
Aliases do not create additional permissions.
Script execution, console tests, camera locations, debug capture commands, and tree previews are explicitly development-only.
All other current domain commands are development-only. Production promotion requires an explicit review. These policies restrict execution; they do not strip code from the player build.

The existing bootstrap gate still controls whether the console opens.
`--allowDebug` may open a release console, but it no longer permits unreviewed or development commands to execute.
The shared executor checks policy before invocation, including script commands, defers, and sidecars.
Help and completion use the same policy to filter discovery.

This change does not add multiplayer authorization, remote administration, or build-time command stripping.
Authoritative domain services must still validate future network requests.
Runtime denial does not remove command implementations from the shipping binary.

## Execution corrections

- Immediate sidecar execution rejects async methods before invoking them.
- The executor and async runner accept explicit `ConsoleCommandResult` failures, including awaitable results.
- Water setters return explicit outcomes. Camera location, path/scorch validation, and invalid script inputs now signal failure.
- A failed script throws after registered defers execute, so nested script callers also observe failure.
- Existing string and void command methods remain supported. Other domain adapters still need review for error text returned as success.
- Console dump names reject paths and verify the resolved output directory before writing.
- Tree species validation no longer overwrites the current species when parsing fails.

## Validation

Core and Planet builds pass with existing analyzer and unrelated domain warnings.
Unity 6000.7.0a5 imported the changes. The focused `ConsoleRegressionTests` suite covers parsing, completion, aliases, policy, dump names, clipboard handling, and script failure/defer behavior.
The first suite passed 23 tests. The expanded suite passed 26 tests.
The final expanded run was job `e8a40085617846d0a100bb210d73a80c`: 26 passed, zero failed, zero skipped, in 1.040 seconds.

Live checks in `WolfDeerEncounter` confirmed:

- Mid-line completion changed `water.set Wave 0.5` to `water.set WaveAmplitude 0.5` without losing the value.
- The cursor remained between the field and value.
- Opening the console disabled gameplay input; closing restored it.
- Missing path services now produce a failed command result.
- Popup descriptions and release labels rendered in a saved screenshot.

Evidence: `local-only/console-validation/2026-09-07/intellisense-midline.png`.
Final grouped-help evidence: `local-only/console-validation/2026-09-07/grouped-help-final.png`.
The final live check confirmed zero idle suggestions after help, so the popup does not obscure output.
Async success returned success. The intentional async failure returned failure with `test.console.async-fail: test async failure (this is expected)`.
Temporary console and input services were removed after inspection. No scene was saved.
Actual release-player builds and multiplayer behavior were not exercised; release-policy branches were checked by the focused tests.

The initial runtime catalog contained 264 canonical commands and 275 accepted names.
The restructuring assigns DevelopmentOnly to the previous Unreviewed commands. The eight Player commands retain their policy.
See [command authoring rules](console-command-authoring.md) for the current contract and build gate.
Family browsing opens explicitly with Tab so it does not obscure help output after execution.
Unity was left outside Play mode. Graphify updated successfully to 11,657 nodes and 16,776 edges.
HTML graph generation was skipped because the graph exceeds its 5,000-node visualization limit.

## Command restructuring

`CommandCatalog` now supplies discovery for help, IntelliSense, and export. Domain attributes declare purpose groups.
Canonical names use `camera.location.*`, `path.stroke.*`, `path.mouse.*`, `path.saved.*`, `debug.capture.*`, and `tree.preview.*`.
The short built-in names remain aliases of `console.*`. Existing scripts keep working.
`WaterCommands` owns water command parsing and output. `PlanetWaterSurface.ApplySettings` accepts typed settings.
Water query and settings failures return `ConsoleCommandResult.Fail`. Partial `water.at` coordinates now fail validation.
`CommandContract` rejects invalid declarations during registration. A failed scan preserves the previous catalog.
`ConsoleCommandBuildValidator` runs this scan before every player build.

### Restructuring validation

The final catalog contains 264 canonical commands and 292 accepted names: 8 Player and 256 DevelopmentOnly.
Unity EditMode job `a43ad6f248a54756927db8891e8f6687` passed all 43 tests, with no failures or skips.
An earlier test used broad text search to select water commands. It also matched a Planet description.
That test failed with `Expected: <WaterCommands> But was: <Planet>`. The corrected test filters by family.
The build validator callback passed when invoked in Unity. Core and Planet builds passed with existing warnings.
No release player build was produced. Release-policy behavior was checked through the policy tests.
`graphify update .` completed. The exported catalog is `local-only/console-dumps/command-catalog.tsv`.
