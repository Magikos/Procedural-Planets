using System.Threading;
using UnityEngine;

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
