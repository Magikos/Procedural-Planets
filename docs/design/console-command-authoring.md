# Console command authoring

The registry validates every command. `ConsoleCommandBuildValidator` runs the same scan before every player build.
`ConsoleRegressionTests` checks the catalog and rejects invalid declarations.

## Required declarations

- Declare a domain prefix and purpose group with `CommandPrefix`.
- Declare a release policy on the owner or method. Unspecified and `Unreviewed` policies fail validation.
- Use lowercase dotted canonical names: `domain.subject.action`. Hyphens may separate words within a segment.
- Provide a description. Provide examples and parameter descriptions when syntax needs explanation.
- Keep old names in `Aliases`. Aliases use full names, without automatic prefix expansion.
- Use unique canonical names and aliases. Collisions fail the scan and build.
- Match the target mode to the method. Registry targets need explicit registration and cleanup.
- Use supported argument parsers. Nullable parameters use the underlying parser.
- Supply a concrete completion provider with a public parameterless constructor for bounded string choices.
- Return `ConsoleCommandResult.Fail` for expected rejection. Never return an error message as successful output.
- Use `Awaitable<ConsoleCommandResult>` for asynchronous operations that can reject input. Place an optional injected `CancellationToken` last.

The validator checks metadata, names, target modes, argument support, provider construction, and supported return types.
It cannot infer whether an arbitrary string reports failure. Review and regression tests must verify rejection behavior.
`void`, `string`, `Awaitable`, and `Awaitable<string>` remain supported for existing commands and operations without expected rejection.

## Release policy

| Policy | Editor or development build | Release build |
|---|---|---|
| DevelopmentOnly | Allowed | Denied |
| Player | Allowed | Allowed locally |
| Administrator | Allowed | Denied until trusted authorization exists |
| Unreviewed | Declaration rejected | Declaration rejected |

Choose `DevelopmentOnly` for diagnostics, authoring, captures, and world mutation tools.
Choose `Player` only after reviewing the operation for player access. Add a test for its production behavior.
The current player allowlist contains `console.help`, `console.echo`, `console.clear`, `console.quit`,
`console.anchor`, `console.scrollback-size`, `console.cancel`, and `console.abandon`.
All other current commands are development-only. This classification limits execution; it does not strip code from builds.
Execution checks apply equally to direct input, aliases, immediate calls, and scripts.

## Boundaries

Keep parsing, completion, output formatting, and command attributes in domain command adapters.
Keep simulation and rendering operations in their existing owners. Pass typed data across that boundary.
`WaterCommands` is the reference adapter. `PlanetWaterSurface.ApplySettings` accepts a `WaterDto` and refreshes its material.
Use `CommandCatalog` for discovery. Do not add independent policy filters or domain maps to help or IntelliSense.

## Example

```csharp
[CommandPrefix("example", Group = "Diagnostics and authoring",
    ReleasePolicy = ConsoleReleasePolicy.DevelopmentOnly)]
public static class ExampleCommands
{
    [ConsoleCommand("sample.set", "Set the sample count.", Example = "example.sample.set 8")]
    public static ConsoleCommandResult Set(int count)
    {
        if (count < 1) return ConsoleCommandResult.Fail("Sample count must be positive.");
        // Call the existing domain operation here.
        return ConsoleCommandResult.Ok($"Sample count: {count}");
    }
}
```

## Validation before delivery

Accepting a command with Tab opens its parameter choices immediately. Boolean parameters offer `true` and `false`; enum parameters offer their names.
Use Up/Down to select a value, then Tab to insert it. Enter submits the completed command.
Accepting a value advances to the next parameter's choices when available.

Run `ConsoleRegressionTests` in Unity EditMode after adding or changing commands.
Test expected failures through `CommandExecutor`, including script stop/defer behavior where relevant.
Test aliases against the same descriptor. Test completion for every bounded string parameter.
Use `console.catalog true` to inspect owners, groups, aliases, and policies.
Do not weaken the validator to accommodate a new command. Extend a shared parser when a new type is required.
