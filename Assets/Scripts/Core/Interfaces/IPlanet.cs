using UnityEngine;

/// <summary>
/// Surface-level interface for the planet object. Exposes the data that other systems
/// (atmosphere, celestial, clouds) need without coupling them to the concrete Planet MonoBehaviour.
/// </summary>
public interface IPlanet
{
    /// <summary>The planet's Transform — used by celestial and atmosphere systems for position tracking.</summary>
    Transform Transform { get; }

    /// <summary>Seed used when this planet was last generated.</summary>
    int Seed { get; }

    /// <summary>Scaled radius including terrain elevation (planet radius × (1 + elevationMax)).</summary>
    float LastGeneratedRadius { get; }

    /// <summary>Sea-level radius (planet radius × (1 + oceanLevel)).</summary>
    float LastSeaLevelRadius { get; }

    /// <summary>True while async generation is running.</summary>
    bool IsGenerating { get; }
}
