using System;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Registration and build-time rules for every console command.</summary>
public static class CommandContract
{
    static readonly Regex NamePattern = new(@"^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant);

    public static void Validate(CommandData command)
    {
        void Require(bool condition, string rule)
        {
            if (!condition) throw new InvalidOperationException($"Console command '{command.Alias}': {rule}");
        }

        Require(command.Alias != null && NamePattern.IsMatch(command.Alias), "use lowercase dotted names with optional hyphens.");
        Require(command.Alias.Contains('.'), "canonical names must include a domain prefix; keep short names as aliases.");
        Require(!string.IsNullOrWhiteSpace(command.Description), "declare a description.");
        Require(!string.IsNullOrWhiteSpace(command.Group), "declare Group on CommandPrefix.");
        Require(Enum.IsDefined(typeof(ConsoleReleasePolicy), command.ReleasePolicy)
            && command.ReleasePolicy != ConsoleReleasePolicy.Unreviewed, "declare a reviewed release policy on the command or its owner.");
        Require(command.Aliases != null, "Aliases cannot be null.");
        var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { command.Alias };
        foreach (string alias in command.Aliases)
            Require(alias != null && NamePattern.IsMatch(alias) && names.Add(alias), "aliases must be valid and unique.");
        Require(!command.Method.ContainsGenericParameters, "generic command methods are unsupported.");
        Require(Enum.IsDefined(typeof(MonoTargetType), command.TargetType), "declare a supported target mode.");
        Require(command.Method.IsStatic == (command.TargetType == MonoTargetType.Static), "target mode must match the method.");
        Require(command.TargetType == MonoTargetType.Static || command.TargetType == MonoTargetType.Registry
            || typeof(MonoBehaviour).IsAssignableFrom(command.DeclaringType), "scene targets must derive from MonoBehaviour.");
        foreach (var parameter in command.Parameters)
        {
            Require(ConsoleArgumentParsers.Supports(parameter.Type), $"parameter '{parameter.Name}' has no argument parser.");
            var provider = parameter.CompletionProvider;
            Require(provider == null || (!provider.IsAbstract && !provider.ContainsGenericParameters
                && typeof(IConsoleCompletionProvider).IsAssignableFrom(provider)
                && provider.GetConstructor(Type.EmptyTypes) != null), $"parameter '{parameter.Name}' needs a constructible completion provider.");
        }
        Type result = command.ReturnType;
        if (result.IsGenericType && result.GetGenericTypeDefinition() == typeof(Awaitable<>))
            result = result.GetGenericArguments()[0];
        Require(result == typeof(void) || result == typeof(string) || result == typeof(ConsoleCommandResult)
            || result == typeof(Awaitable), "return void, string, ConsoleCommandResult, or a supported Awaitable.");
    }
}
