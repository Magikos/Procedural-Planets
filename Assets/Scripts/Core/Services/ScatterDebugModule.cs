using System.Text;

// Scatter's slice of the F10 capture sidecar. Every other subsystem had one — water, biome, terrain, grass,
// atmosphere, cloud — and scatter had none, so a screenshot of a prop that looked wrong could not answer the
// first question anybody asks: which prototype is that, and which LOD band was it in?
//
// Thin by design: the Planet assembly owns the knowledge of what is worth reporting and supplies it through
// IScatterDebugReport, exactly as grass does through IGrassDebugStatsProvider.
public sealed class ScatterDebugModule : IDebugModule, IDebugCaptureMetadataProvider
{
    public DebugModuleId Id => ScatterDebugIds.Module;

    public void Register(DebugRegistry registry) { }

    public void AppendMetadata(DebugCaptureContext context, StringBuilder sb)
    {
        sb.AppendLine("--- Scatter ---");
        if (ServiceLocator.TryGet(out IScatterDebugReport report)) report.Append(sb);
        else sb.AppendLine("Reporter: missing (no world generated)");
    }
}

public static class ScatterDebugIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("scatter");
}
