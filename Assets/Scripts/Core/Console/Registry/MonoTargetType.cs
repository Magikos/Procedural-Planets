public enum MonoTargetType
{
    /// <summary>Method is static. No instance resolution.</summary>
    Static = 0,

    /// <summary>
    /// Find the first active MonoBehaviour of the declaring type via
    /// <see cref="UnityEngine.Object.FindAnyObjectByType(System.Type, UnityEngine.FindObjectsInactive)"/>.
    /// </summary>
    Single = 1,

    /// <summary>
    /// Explicit instance registered via <c>ConsoleRegistry.RegisterInstance&lt;T&gt;(instance)</c>.
    /// </summary>
    Registry = 2,
}
