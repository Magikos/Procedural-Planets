using System.Collections.Generic;

/// <summary>The one write path for persistent world change.</summary>
/// <remarks>
/// Primitives only and no planet types: the game domain consumes this across an assembly boundary, and the
/// same records travel to clients that have never loaded the planet layer.
/// </remarks>
public interface IWorldDeltaLog
{
    /// <summary>Appends a change. The log assigns the sequence; the caller's value is ignored.</summary>
    /// <returns>The record as stored, with its assigned sequence.</returns>
    WorldDelta Append(in WorldDelta delta);

    /// <summary>Latest record for a key, or false when the key has no history.</summary>
    bool TryGet(ulong key, out WorldDelta delta);

    /// <summary>The current state: one record per key, holding the latest value for that key.</summary>
    IReadOnlyList<WorldDelta> Snapshot();
}
