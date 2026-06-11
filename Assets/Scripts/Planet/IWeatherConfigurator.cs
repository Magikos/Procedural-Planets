using UnityEngine;

/// <summary>
/// Exposes the GPU weather textures produced by the weather simulation so cloud
/// rendering can sample them. Separate from <see cref="IWeatherProvider"/> so general
/// weather consumers don't take an unnecessary dependency on cloud-side wiring.
/// </summary>
public interface IWeatherConfigurator
{
    /// <summary>Current weather map texture (cube-sphere face array). Null until first grid generation.</summary>
    Texture WeatherTexture { get; }

    /// <summary>Secondary dynamics texture (fronts / evolution state). May be null.</summary>
    Texture WeatherDynamicsTexture { get; }

    /// <summary>Face resolution of the weather grid, or 0 if no grid exists yet.</summary>
    int WeatherResolution { get; }
}
