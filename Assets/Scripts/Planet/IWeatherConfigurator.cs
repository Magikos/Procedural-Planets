using UnityEngine;

/// <summary>
/// Allows cloud systems to push render settings into the weather simulation and
/// read back the GPU textures it produces. Separate from <see cref="IWeatherProvider"/>
/// so general weather consumers don't take an unnecessary dependency on cloud configuration.
/// </summary>
public interface IWeatherConfigurator
{
    /// <summary>The cloud settings currently driving this weather simulation.</summary>
    CloudSettings Settings { get; }

    /// <summary>Apply new cloud settings to the weather simulation.</summary>
    void Configure(CloudSettings settings);

    /// <summary>Current weather map texture (cube-sphere face array). Null until first grid generation.</summary>
    Texture WeatherTexture { get; }

    /// <summary>Secondary dynamics texture (fronts / evolution state). May be null.</summary>
    Texture WeatherDynamicsTexture { get; }

    /// <summary>Face resolution of the weather grid, or 0 if no grid exists yet.</summary>
    int WeatherResolution { get; }
}
