using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public interface IWorldActionManager
{
    Awaitable ExecuteAsync(IWorldAction action, CancellationToken ct);
    Awaitable UndoAsync(CancellationToken ct);
    Awaitable RedoAsync(CancellationToken ct);
    void Clear();

    /// <summary>Read-only access to the action history (oldest first). Index of the current
    /// position (the one a subsequent <see cref="UndoAsync"/> would target) is in <see cref="HistoryIndex"/>.
    /// Empty when no actions have been executed; index is -1 when at the start.</summary>
    IReadOnlyList<IWorldAction> History { get; }
    int HistoryIndex { get; }
}
