using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class WorldActionManager
{
    readonly List<IWorldAction> _history = new();
    readonly ILogger _logger;
    int _historyIndex = -1;

    public WorldActionManager(ILogger logger)
    {
        _logger = logger;
    }

    public async Awaitable ExecuteAsync(IWorldAction action, CancellationToken ct)
    {
        try
        {
            await action.ExecuteAsync(ct);

            if (_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

            _history.Add(action);
            _historyIndex = _history.Count - 1;

            _logger.Log(LogLevel.Debug, "WorldAction", $"Executed {action.ActionType}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogException("WorldAction", ex);
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
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogException("WorldAction", ex);
        }
    }

    public async Awaitable RedoAsync(CancellationToken ct)
    {
        if (_historyIndex >= _history.Count - 1) return;

        try
        {
            _historyIndex++;
            var action = _history[_historyIndex];
            await action.ExecuteAsync(ct);
            _logger.Log(LogLevel.Debug, "WorldAction", $"Redone {action.ActionType}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogException("WorldAction", ex);
        }
    }

    public void Clear()
    {
        _history.Clear();
        _historyIndex = -1;
    }
}
