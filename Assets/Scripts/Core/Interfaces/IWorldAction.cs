using System.Threading;
using UnityEngine;

// planned: command pattern for undoable world actions — TerrainDeformAction, BuildingPlaceAction and
// the rest arrive with the player interaction systems; executed via WorldActionManager.ExecuteAsync().
// docs/design/2026-06-13-world-lifecycle.md, docs/phases/00-code-architecture.md.
// Zero implementors today is expected; this is scaffolding ahead of the feature, not dead code.
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
