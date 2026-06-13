using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public sealed class WorldLoadRequest
{
    public const int CurrentSettingsSchemaVersion = 1;

    public string SceneName { get; }
    public int BuildIndex { get; }
    public int? WorldSeed { get; }
    public string SaveIdentity { get; }
    public int SettingsSchemaVersion { get; }
    public IReadOnlyList<IWorldSettingsOverride> SettingsOverrides { get; }

    WorldLoadRequest(
        string sceneName,
        int buildIndex,
        int? worldSeed,
        string saveIdentity,
        int settingsSchemaVersion,
        IReadOnlyList<IWorldSettingsOverride> settingsOverrides)
    {
        SceneName = sceneName;
        BuildIndex = buildIndex;
        WorldSeed = worldSeed;
        SaveIdentity = saveIdentity;
        SettingsSchemaVersion = settingsSchemaVersion;
        SettingsOverrides = CopyOverrides(settingsOverrides);
    }

    public static WorldLoadRequest ForScene(
        string sceneName,
        int? worldSeed = null,
        string saveIdentity = null,
        int settingsSchemaVersion = CurrentSettingsSchemaVersion,
        IReadOnlyList<IWorldSettingsOverride> settingsOverrides = null)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            throw new ArgumentException("A scene name is required.", nameof(sceneName));

        return new WorldLoadRequest(
            sceneName, -1, worldSeed, saveIdentity, settingsSchemaVersion, settingsOverrides);
    }

    public static WorldLoadRequest ForBuildIndex(
        int buildIndex,
        int? worldSeed = null,
        string saveIdentity = null,
        int settingsSchemaVersion = CurrentSettingsSchemaVersion,
        IReadOnlyList<IWorldSettingsOverride> settingsOverrides = null)
    {
        if (buildIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(buildIndex));

        return new WorldLoadRequest(
            null, buildIndex, worldSeed, saveIdentity, settingsSchemaVersion, settingsOverrides);
    }

    static IReadOnlyList<IWorldSettingsOverride> CopyOverrides(
        IReadOnlyList<IWorldSettingsOverride> settingsOverrides)
    {
        if (settingsOverrides == null || settingsOverrides.Count == 0)
            return Array.Empty<IWorldSettingsOverride>();

        var copy = new IWorldSettingsOverride[settingsOverrides.Count];
        var types = new HashSet<Type>();
        for (int i = 0; i < settingsOverrides.Count; i++)
        {
            IWorldSettingsOverride settingsOverride = settingsOverrides[i]
                ?? throw new ArgumentException($"Settings override at index {i} is null.", nameof(settingsOverrides));
            if (settingsOverride.DtoType == null)
                throw new ArgumentException($"Settings override at index {i} has no DTO type.", nameof(settingsOverrides));
            if (!types.Add(settingsOverride.DtoType))
                throw new ArgumentException(
                    $"Settings override for {settingsOverride.DtoType.Name} was provided more than once.",
                    nameof(settingsOverrides));

            copy[i] = settingsOverride;
        }

        return copy;
    }
}

public interface ILoadingManager
{
    Awaitable<bool> TransitionToWorldAsync(WorldLoadRequest request, bool useOverlay = true, CancellationToken cancellationToken = default);
    Awaitable<bool> TransitionToSceneAsync(string sceneName, bool useOverlay = true, CancellationToken cancellationToken = default);
    Awaitable<bool> TransitionToSceneAsync(int buildIndex, bool useOverlay = true, CancellationToken cancellationToken = default);
}
