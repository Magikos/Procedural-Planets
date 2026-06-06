using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Console commands for the <see cref="IWorldActionManager"/> command-pattern history.
/// Undo/redo are async and respect the console's CancellationToken plumbing — they appear
/// as cancellable async commands in the console spinner UI.
/// </summary>
[CommandPrefix("action")]
public static class ActionCommands
{
    [ConsoleCommand("undo", "Undo the last world action (async, cancellable).")]
    public static async Awaitable Undo(CancellationToken ct)
    {
        if (!ServiceLocator.TryGet<IWorldActionManager>(out var mgr))
            throw new System.InvalidOperationException("no world action manager registered");
        await mgr.UndoAsync(ct);
    }

    [ConsoleCommand("redo", "Redo the next world action (async, cancellable).")]
    public static async Awaitable Redo(CancellationToken ct)
    {
        if (!ServiceLocator.TryGet<IWorldActionManager>(out var mgr))
            throw new System.InvalidOperationException("no world action manager registered");
        await mgr.RedoAsync(ct);
    }

    [ConsoleCommand("history", "List the world action history with the current cursor marked.")]
    public static string History()
    {
        if (!ServiceLocator.TryGet<IWorldActionManager>(out var mgr))
            return "no world action manager registered";

        var actions = mgr.History;
        if (actions == null || actions.Count == 0)
            return "no actions in history";

        var sb = new StringBuilder();
        sb.Append(actions.Count);
        sb.Append(" action(s), cursor at ");
        sb.Append(mgr.HistoryIndex);
        sb.Append(':');
        for (int i = 0; i < actions.Count; i++)
        {
            sb.Append('\n');
            sb.Append(i == mgr.HistoryIndex ? "  > " : "    ");
            sb.Append(i);
            sb.Append(": ");
            var action = actions[i];
            sb.Append(action == null ? "(null)" : action.ActionType.ToString());
        }
        return sb.ToString();
    }

    [ConsoleCommand("clear", "Clear the entire action history.")]
    public static string Clear()
    {
        if (!ServiceLocator.TryGet<IWorldActionManager>(out var mgr))
            return "no world action manager registered";
        mgr.Clear();
        return "action history cleared";
    }
}
