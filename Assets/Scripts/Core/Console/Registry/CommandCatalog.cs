using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Shared command discovery for help, completion, and catalog export.</summary>
public static class CommandCatalog
{
    public static IEnumerable<CommandData> All => ConsoleRegistry.Commands.Values.Distinct()
        .OrderBy(command => command.Alias, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<CommandData> Visible => All.Where(ConsoleCommandPolicy.CanExecute);

    public static IEnumerable<IGrouping<string, CommandData>> Families => Visible
        .GroupBy(command => command.Family).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<CommandData> Search(string query)
    {
        query = (query ?? "").Trim();
        bool policyFilter = Enum.TryParse<ConsoleReleasePolicy>(query, true, out var policy)
            && Enum.IsDefined(typeof(ConsoleReleasePolicy), policy);
        return Visible.Where(command => query.Length == 0 || query.Equals("all", StringComparison.OrdinalIgnoreCase)
            || (policyFilter ? command.ReleasePolicy == policy
                : Contains(command.Alias, query.TrimEnd('.')) || Contains(command.Description, query)
                  || Contains(command.Group, query) || command.Aliases.Any(alias => Contains(alias, query))));
    }

    public static IEnumerable<(string alias, CommandData cmd)> MatchNames(string partial)
    {
        partial ??= "";
        return ConsoleRegistry.Commands.Where(entry => ConsoleCommandPolicy.CanExecute(entry.Value))
            .Select(entry => (alias: entry.Key, cmd: entry.Value, rank:
                entry.Key.Equals(partial, StringComparison.OrdinalIgnoreCase) ? 0 :
                entry.Key.StartsWith(partial, StringComparison.OrdinalIgnoreCase) ? 1 :
                Contains(entry.Key, partial) ? 2 : Contains(entry.Value.Description, partial) ? 3 : -1))
            .Where(entry => entry.rank >= 0).OrderBy(entry => entry.rank)
            .ThenBy(entry => entry.alias, StringComparer.OrdinalIgnoreCase)
            .Select(entry => (entry.alias, entry.cmd));
    }

    public static IEnumerable<string> HelpCompletions(string partial) => CompletionRanker.Rank(
        Visible.SelectMany(command => command.Aliases.Prepend(command.Alias)
            .Append(command.Family).Append(command.Group))
            .Concat(Enum.GetNames(typeof(ConsoleReleasePolicy))).Append("all")
            .Distinct(StringComparer.OrdinalIgnoreCase), partial);

    static bool Contains(string value, string query) =>
        value?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
}
