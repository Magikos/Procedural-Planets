using System.Threading;
using UnityEngine;

public interface IWorldActionManager
{
    Awaitable ExecuteAsync(IWorldAction action, CancellationToken ct);
    Awaitable UndoAsync(CancellationToken ct);
    Awaitable RedoAsync(CancellationToken ct);
    void Clear();
}
