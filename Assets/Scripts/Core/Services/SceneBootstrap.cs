using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public enum WorldServiceValidationProfile
{
    Full,
    TerrainGrassBiome,
}

public class SceneBootstrap : MonoBehaviour, IEarlyInitialize
{
    static readonly Type[] EarlyDeps = { typeof(GameBootstrap) };

    static readonly Type[] RequiredWorldServices =
    {
        typeof(ISettingsService),
        typeof(ISeedProvider),
        typeof(IWorldActionManager),
        typeof(IPlanet),
        typeof(IPlanetSurfaceSampler),
        typeof(IPlanetSurfaceRaycaster),
        typeof(IClimateSampler),
        typeof(IGrassRuntimeControl),
        typeof(IGrassNearFieldStatsProvider),
        typeof(ICloudRuntime),
        typeof(IAtmosphereRuntime),
        typeof(IWeatherProvider),
        typeof(IPrecipitationDebugControl),
        typeof(IRainParticleRenderer),
        typeof(ICelestialTimeController),
        typeof(ICameraRigContext),
        typeof(ICameraTeleportRegistry),
        typeof(IScaleReferenceDebugStatsProvider),
    };

    static readonly Type[] TerrainGrassBiomeWorldServices =
    {
        typeof(ISettingsService),
        typeof(ISeedProvider),
        typeof(IWorldActionManager),
        typeof(IPlanet),
        typeof(IPlanetSurfaceSampler),
        typeof(IPlanetSurfaceRaycaster),
        typeof(IClimateSampler),
        typeof(IGrassRuntimeControl),
        typeof(IGrassNearFieldStatsProvider),
        typeof(ICameraRigContext),
        typeof(ICameraTeleportRegistry),
        typeof(IScaleReferenceDebugStatsProvider),
    };

    [Header("Settings")]
    public int WorldSeed = 12345;

    [Header("Diagnostics")]
    [SerializeField] WorldServiceValidationProfile _validationProfile = WorldServiceValidationProfile.Full;

    ISeedProvider _seedProvider;
    IWorldActionManager _worldActionManager;
    readonly HashSet<Type> _requiredSettings = new();

    public int EarlyPriority => 50;
    public IReadOnlyList<Type> EarlyDependencies => EarlyDeps;

    void Awake()
    {
        RegisterSceneServices();
        RegisterSceneSettings();
        ServiceLocator.GetWorld().ApplySettingsOverrides();
    }

    public async Awaitable EarlyInitialize(CancellationToken cancellationToken)
    {
        var logger = ServiceLocator.Get<ILogger>();

        IWorldContext context = ServiceLocator.GetWorld();
        int worldSeed = context.LoadRequest?.WorldSeed ?? WorldSeed;
        _seedProvider = new SeedProvider(worldSeed);
        _worldActionManager = new WorldActionManager(logger);

        ServiceLocator.RegisterWorld<ISeedProvider>(_seedProvider);
        ServiceLocator.RegisterWorld<IWorldActionManager>(_worldActionManager);

        if (_validationProfile == WorldServiceValidationProfile.Full)
            EnsureComponent<WaterWakeController>();
        EnsureComponent<ScaleReferenceMarkers>();
        RegisterSceneServices();
        RegisterSceneSettings();
        context.ApplySettingsOverrides();
        context.ValidateRequired(GetRequiredWorldServices());
        ISettingsService settings = context.Settings;
        settings.ValidateRequired(_requiredSettings);
        settings.Freeze();

        logger.Log(LogLevel.Info, "Bootstrap",
            $"Scene services initialized. World seed: {worldSeed}; validation profile: {_validationProfile}");

        await Awaitable.NextFrameAsync(cancellationToken);
    }

    Type[] GetRequiredWorldServices()
    {
        return _validationProfile == WorldServiceValidationProfile.TerrainGrassBiome
            ? TerrainGrassBiomeWorldServices
            : RequiredWorldServices;
    }

    private void EnsureComponent<T>() where T : Component
    {
        var roots = gameObject.scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].GetComponentInChildren<T>(true) != null)
                return;
        }

        gameObject.AddComponent<T>();
    }

    void RegisterSceneServices()
    {
        IWorldContext context = ServiceLocator.GetWorld();
        var roots = gameObject.scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            var behaviours = roots[i].GetComponentsInChildren<MonoBehaviour>(true);
            for (int j = 0; j < behaviours.Length; j++)
            {
                if (behaviours[j] is IWorldServiceRegistrar registrar)
                    registrar.RegisterWorldServices(context);
            }
        }
    }

    void RegisterSceneSettings()
    {
        ISettingsService settings = ServiceLocator.GetWorld().Settings;
        var roots = gameObject.scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            var behaviours = roots[i].GetComponentsInChildren<MonoBehaviour>(true);
            for (int j = 0; j < behaviours.Length; j++)
            {
                if (behaviours[j] is not IWorldSettingsRegistrar registrar)
                    continue;

                IReadOnlyList<Type> requiredTypes = registrar.RequiredSettingsTypes;
                for (int k = 0; k < requiredTypes.Count; k++)
                    _requiredSettings.Add(requiredTypes[k]);
                registrar.RegisterWorldSettings(settings);
            }
        }
    }
}
