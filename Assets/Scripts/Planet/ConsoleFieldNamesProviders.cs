using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class WaterFloatFieldNamesProvider : IConsoleCompletionProvider
{
    public IEnumerable<string> GetCompletions(string partialValue) => CompletionRanker.Rank(
        typeof(WaterDto).GetProperties().Where(p => p.PropertyType == typeof(float)).Select(p => p.Name), partialValue);
}

public sealed class WaterColorFieldNamesProvider : IConsoleCompletionProvider
{
    public IEnumerable<string> GetCompletions(string partialValue) => CompletionRanker.Rank(
        typeof(WaterDto).GetProperties().Where(p => p.PropertyType == typeof(Color)).Select(p => p.Name), partialValue);
}

public sealed class TreeSpeciesNamesProvider : IConsoleCompletionProvider
{
    public IEnumerable<string> GetCompletions(string partialValue) => CompletionRanker.Rank(
        TreeDefLibrary.AllSpecies.Select(species => species.ToString()), partialValue);
}
