using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

// FUTURE: Command pattern for undoable world modifications (terrain deform, building place/remove).
// Will be used when player interaction systems are implemented.
// Registered in GameBootstrap, accessed via ServiceLocator.Get<IWorldActionManager>().
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
