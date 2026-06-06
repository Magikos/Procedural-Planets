using System.Text;

/// <summary>
/// F10 capture sidecar contributor for the debug console — appends a <c>--- DebugConsole ---</c>
/// block with open/anchor/scrollback/history/pending-async stats. Registered with the global
/// <see cref="DebugRegistry"/> by <see cref="DebugCaptureController"/> at bootstrap.
/// </summary>
public sealed class ConsoleDebugModule : IDebugModule, IDebugCaptureMetadataProvider
{
    static readonly DebugModuleId _id = new DebugModuleId("console");

    public DebugModuleId Id => _id;

    public void Register(DebugRegistry registry)
    {
        registry.RegisterMetadataProvider(this);
    }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        sb.AppendLine("--- DebugConsole ---");

        if (!ServiceLocator.TryGet<IConsoleService>(out var console))
        {
            sb.AppendLine("Status: disabled (release build without --allowDebug, or pre-bootstrap capture)");
            return;
        }

        var d = console.GetDiagnostics();
        sb.AppendLine($"Open: {d.IsOpen}, Anchor: {d.Anchor}");
        sb.AppendLine($"Scrollback: {d.ScrollbackCount}/{d.ScrollbackCapacity}");
        sb.AppendLine($"History: {d.HistoryCount} entries");
        sb.AppendLine($"Commands registered: {d.CommandsRegistered}");
        if (!string.IsNullOrEmpty(d.PendingAlias))
            sb.AppendLine($"Pending: '{d.PendingAlias}' running {d.PendingElapsedSeconds:F2}s, cancellable={d.IsPendingCancellable}");
        else
            sb.AppendLine("Pending: none");
    }
}
