using System.Threading;
using UnityEngine;

// FUTURE: Command pattern interface for undoable world actions.
// Implementations: TerrainDeformAction, BuildingPlaceAction, etc.
// Executed via WorldActionManager.ExecuteAsync().
public interface IWorldAction
{
    WorldActionType ActionType { get; }
    Awaitable ExecuteAsync(CancellationToken ct);
    Awaitable UndoAsync(CancellationToken ct);
    byte[] Serialize();
    void Deserialize(byte[] data);
}

public enum WorldActionType
{
    TerrainDeform,
    BuildingPlace,
    BuildingRemove,
    EntityHarvest,
    EntitySpawn
}
