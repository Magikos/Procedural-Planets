using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

// planned: command pattern for undoable world modifications (terrain deform, building place/remove),
// used once the player interaction systems land. docs/design/2026-06-13-world-lifecycle.md.
// Registered by SceneBootstrap in the active world context; no actions execute through it yet.
//
// Scope decided 2026-08-25 (B8a): undo is a LOCAL editing affordance. An IWorldAction never writes to
// WorldDeltaLog, because the log cannot answer "what was it before" — records carry no prior value,
// Remember collapses last-write-wins per (space, key), and compaction discards history at a size
// threshold, which would make undo depth vary with unrelated write volume. Once an action becomes a
// delta it is final, reversed only by a new forward action.
// This is also NOT M1's command layer: that choke point is HarvestService.TryHarvest.
public class WorldActionManager : IWorldActionManager
{
    readonly List<IWorldAction> _history = new();
    readonly ILogger _logger;
    int _historyIndex = -1;

    public WorldActionManager(ILogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<IWorldAction> History => _history;
    public int HistoryIndex => _historyIndex;

    public async Awaitable ExecuteAsync(IWorldAction action, CancellationToken ct)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        try
        {
            await action.ExecuteAsync(ct);

            if (_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

            _history.Add(action);
            _historyIndex = _history.Count - 1;

            _logger.Log(LogLevel.Debug, "WorldAction", $"Executed {action.ActionType}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogException("WorldAction", ex);
            throw;
        }
    }

    public async Awaitable UndoAsync(CancellationToken ct)
    {
        if (_historyIndex < 0) return;

        try
        {
            var action = _history[_historyIndex];
            await action.UndoAsync(ct);
            _historyIndex--;
            _logger.Log(LogLevel.Debug, "WorldAction", $"Undone {action.ActionType}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogException("WorldAction", ex);
            throw;
        }
    }

    public async Awaitable RedoAsync(CancellationToken ct)
    {
        if (_historyIndex >= _history.Count - 1) return;

        try
        {
            int nextIndex = _historyIndex + 1;
            var action = _history[nextIndex];
            await action.ExecuteAsync(ct);
            _historyIndex = nextIndex;
            _logger.Log(LogLevel.Debug, "WorldAction", $"Redone {action.ActionType}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogException("WorldAction", ex);
            throw;
        }
    }

    public void Clear()
    {
        _history.Clear();
        _historyIndex = -1;
    }
}
