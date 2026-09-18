using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>Reject invalid commands before any player build, including development builds.</summary>
public sealed class ConsoleCommandBuildValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        try { ConsoleRegistry.Scan(); }
        catch (System.InvalidOperationException error) { throw new BuildFailedException(error.Message); }
    }
}
