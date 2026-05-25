using UnityEngine;

/// <summary>
/// Applies Unity Quality Settings to the planet rendering system.
///
/// GameBootstrap ensures this component exists in play mode. A future settings
/// screen can call SetQualityLevel when the player changes quality at runtime.
/// </summary>
[DisallowMultipleComponent]
public class QualityController : MonoBehaviour
{
    const string KeywordCloudQualityLow = "CLOUD_QUALITY_LOW";
    const int FallbackLowQualityMaxLevel = -1;
    const int FallbackMediumQualityMaxLevel = -1;
    const float LowQualityStepMultiplier = 0.33f;
    const float MediumQualityStepMultiplier = 0.65f;
    const float HighQualityStepMultiplier = 1.0f;
    static readonly string[] LowQualityNameTokens = { "mobile", "low", "fastest" };
    static readonly string[] MediumQualityNameTokens = { "medium", "balanced" };

    /// <summary>
    /// Step-count multiplier for the current quality tier (0.1-1.0).
    /// CloudController and PrecipitationController apply this when computing view steps.
    /// Defaults to 1.0 so full quality is used before a QualityController is present.
    /// </summary>
    public static float CloudStepMultiplier { get; private set; } = 1f;
    public static bool IsCloudLowQualityEnabled { get; private set; }
    public static int AppliedQualityLevel { get; private set; } = -1;
    public static string AppliedQualityName { get; private set; } = "Unknown";
    public static string AppliedQualityTier { get; private set; } = "High";

    void Awake()
    {
        Refresh();
    }

    void OnEnable()
    {
        Refresh();
    }

    /// <summary>
    /// Re-reads the current Unity quality level and applies shader keywords / global properties.
    /// Call this whenever the quality setting changes at runtime.
    /// </summary>
    public void Refresh()
    {
        ApplyQualityLevel(QualitySettings.GetQualityLevel());
    }

    public void SetQualityLevel(int level)
    {
        int maxLevel = Mathf.Max(QualitySettings.names.Length - 1, 0);
        int clampedLevel = Mathf.Clamp(level, 0, maxLevel);
        QualitySettings.SetQualityLevel(clampedLevel, true);
        ApplyQualityLevel(clampedLevel);
    }

    /// <summary>
    /// Applies shader keywords and global properties for the given Unity quality level index.
    /// </summary>
    public void ApplyQualityLevel(int level)
    {
        AppliedQualityLevel = level;
        AppliedQualityName = GetQualityName(level);
        bool isLow = QualityNameContains(AppliedQualityName, LowQualityNameTokens);
        bool isMedium = !isLow && QualityNameContains(AppliedQualityName, MediumQualityNameTokens);

        // Unity filters QualitySettings.names by the active platform. In Standalone
        // this project exposes PC as runtime index 0, so name-based classification
        // is the primary path and index fallback stays disabled by default.
        if (!isLow && !isMedium && QualitySettings.names.Length > 1)
        {
            isLow = FallbackLowQualityMaxLevel >= 0 && level <= FallbackLowQualityMaxLevel;
            isMedium = !isLow && FallbackMediumQualityMaxLevel >= 0 && level <= FallbackMediumQualityMaxLevel;
        }

        IsCloudLowQualityEnabled = isLow;
        AppliedQualityTier = isLow ? "Low" : isMedium ? "Medium" : "High";

        // CLOUD_QUALITY_LOW caps raymarch steps and disables detail noise in Cloud.shader
        // and Precipitation.shader. Both shaders compile a variant for this keyword.
        if (isLow)
            Shader.EnableKeyword(KeywordCloudQualityLow);
        else
            Shader.DisableKeyword(KeywordCloudQualityLow);

        // Step multiplier is read by CloudController and PrecipitationController when
        // computing per-frame view step counts (altitude LOD already applied first).
        CloudStepMultiplier = isLow ? LowQualityStepMultiplier
                            : isMedium ? MediumQualityStepMultiplier
                            : HighQualityStepMultiplier;
    }

    static string GetQualityName(int level)
    {
        string[] names = QualitySettings.names;
        return level >= 0 && level < names.Length ? names[level] : "Unknown";
    }

    static bool QualityNameContains(string qualityName, string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(qualityName))
            return false;

        for (int i = 0; i < tokens.Length; i++)
        {
            if (qualityName.IndexOf(tokens[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying)
            Refresh();
    }
#endif
}
