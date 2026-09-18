using UnityEngine;

public enum ConsoleReleasePolicy
{
    Unreviewed,
    DevelopmentOnly,
    Player,
    Administrator,
}

public static class ConsoleCommandPolicy
{
    public static bool CanExecute(CommandData command) =>
        CanExecute(command.ReleasePolicy, Application.isEditor || Debug.isDebugBuild);

    // Administrator commands remain denied in release until a trusted authorization path exists.
    public static bool CanExecute(ConsoleReleasePolicy policy, bool development) =>
        development || policy == ConsoleReleasePolicy.Player;
}
