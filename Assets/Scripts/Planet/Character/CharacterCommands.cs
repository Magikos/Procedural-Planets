using UnityEngine;

/// <summary>
/// Console entry points for the walking-character spike. These are <see cref="MonoTargetType.Static"/> so
/// <c>character.spawn</c> works before any host instance exists: it find-or-creates the single
/// <see cref="PlanetCharacterController"/> host. (A MonoBehaviour cannot instantiate itself, and
/// <c>SceneBootstrap</c> lives in the lower Core assembly which cannot reference this Planet-assembly host —
/// so a static factory command is the clean creation path.)
/// </summary>
[CommandPrefix("character")]
public static class CharacterCommands
{
    static PlanetCharacterController _host;

    static PlanetCharacterController Host()
    {
        if (_host != null) // Unity's null override makes a destroyed host compare == null, so we re-find it.
            return _host;

        _host = Object.FindAnyObjectByType<PlanetCharacterController>();
        if (_host == null)
        {
            var go = new GameObject("Character Host");
            _host = go.AddComponent<PlanetCharacterController>();
        }
        return _host;
    }

    [ConsoleCommand("spawn", "Spawn the walking character at the camera's ground point (WASD to walk).", MonoTargetType.Static)]
    public static string SpawnCmd() => Host().Spawn();

    [ConsoleCommand("despawn", "Despawn the character and restore the free-fly camera.", MonoTargetType.Static)]
    public static string DespawnCmd() => Host().Despawn();
}
