using UnityEngine;

/// <summary>
/// Color + styling palette for the debug console. Loaded by <see cref="ConsoleRenderer"/>
/// at construction via <c>Resources.Load&lt;ConsoleTheme&gt;("ConsoleTheme")</c>; if the
/// asset does not exist the renderer falls back to <see cref="CreateDefault"/>, which
/// returns an in-memory instance with the same defaults shipped before the SO existed.
///
/// To customize the palette, right-click in the Project window →
/// <i>Create → ProceduralPlanets → Console → Theme</i>, save the asset as
/// <c>Assets/Resources/ConsoleTheme.asset</c>, and tweak values in the inspector.
/// </summary>
[CreateAssetMenu(fileName = "ConsoleTheme", menuName = "ProceduralPlanets/Console/Theme", order = 100)]
public sealed class ConsoleTheme : ScriptableObject
{
    [Header("Backdrop / border")]
    public Color Backdrop = new(0.02f, 0.03f, 0.05f, 0.94f);
    public Color Border = new(0.4f, 0.8f, 1.0f, 0.6f);
    [Range(0f, 0.02f)] public float BorderThickness = 0.0025f;
    [Range(0f, 1f)] public float ScanlineStrength = 0f;
    public Color NewMessageBorderPulse = new(0.95f, 0.72f, 0.25f, 0.85f);

    [Header("Scrollback message colors")]
    public Color MsgNormal = new(0.72f, 0.72f, 0.74f, 1f);
    public Color MsgInput = new(0.38f, 0.38f, 0.40f, 1f);
    public Color MsgOutput = new(0.72f, 0.72f, 0.74f, 1f);
    public Color MsgWarning = new(0.95f, 0.78f, 0.30f, 1f);
    public Color MsgError = new(0.95f, 0.38f, 0.35f, 1f);
    public Color MsgException = new(1.00f, 0.25f, 0.20f, 1f);
    public Color MsgLog = new(0.50f, 0.50f, 0.54f, 1f);

    [Header("Scrollbar")]
    public Color ScrollbarBg = new(0.06f, 0.08f, 0.12f, 0.60f);
    public Color ScrollbarThumb = new(0.35f, 0.80f, 0.88f, 0.55f);

    [Header("Suggestion popup")]
    public Color PopupBackdrop = new(0.03f, 0.04f, 0.07f, 0.97f);
    public Color SuggActive = Color.white;
    public Color SuggInactive = new(0.58f, 0.58f, 0.60f, 1f);
    public Color SuggMatchActive = new(0.35f, 0.80f, 0.88f, 1f);
    public Color SuggMatchInactive = new(0.28f, 0.60f, 0.70f, 1f);
    public Color SuggHighlightBackdrop = new(0.10f, 0.25f, 0.42f, 0.90f);

    [Header("Confirm modal")]
    public Color ConfirmBackdrop = new(0.05f, 0.07f, 0.11f, 0.97f);
    public Color ConfirmQuestion = new(0.88f, 0.88f, 0.90f, 1f);
    public Color ConfirmActive = new(0.35f, 0.80f, 0.88f, 1f);
    public Color ConfirmInactive = new(0.46f, 0.46f, 0.48f, 1f);

    [Header("Input-line syntax highlight")]
    public Color InputPrompt = new(0.38f, 0.38f, 0.40f, 1f);
    public Color InputCommand = new(0.35f, 0.80f, 0.88f, 1f);
    public Color InputValue = new(0.85f, 0.78f, 0.62f, 1f);
    public Color InputString = new(0.58f, 0.85f, 0.48f, 1f);
    public Color InputGhost = new(0.30f, 0.30f, 0.32f, 0.85f);
    public Color InputHintRequired = new(0.58f, 0.43f, 0.24f, 0.72f);
    public Color InputHintOptional = new(0.35f, 0.42f, 0.52f, 0.65f);

    /// <summary>
    /// In-memory default palette used when no <c>Resources/ConsoleTheme</c> asset exists.
    /// Values match the constants the console shipped with before this SO was introduced,
    /// so absence of an asset is fully backward compatible.
    /// </summary>
    public static ConsoleTheme CreateDefault()
    {
        var t = CreateInstance<ConsoleTheme>();
        t.name = "ConsoleTheme (built-in default)";
        return t;
    }
}
