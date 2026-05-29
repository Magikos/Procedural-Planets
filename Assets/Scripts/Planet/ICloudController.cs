/// <summary>
/// Allows cloud systems to push render settings into the weather simulation.
/// Separate from <see cref="IWeatherProvider"/> so general weather consumers don't
/// take an unnecessary dependency on cloud configuration.
/// </summary>
public interface ICloudController
{
    /// <summary>The active cloud render settings asset.</summary>
    CloudSettings Settings { get; }
}
