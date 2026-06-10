using UnityEngine;

public static class ShaderGlobalIds
{
    public static readonly int GameTime = Shader.PropertyToID("_GameTime");
    public static readonly int OceanDebugMode = Shader.PropertyToID("_OceanDebugMode");
    public static readonly int WaterFocusMode = Shader.PropertyToID("_WaterFocusMode");
    public static readonly int OceanFocusMode = Shader.PropertyToID("_OceanFocusMode");
}

[DisallowMultipleComponent]
public sealed class ShaderGlobalsController : MonoBehaviour
{
    [Tooltip("Keeps shader time small enough to avoid long-session float precision loss.")]
    [Min(1f)] public float GameTimePeriodSeconds = 3600f;

    void Awake()
    {
        ResetTransientDebugGlobals();
        GrassRenderDiagnostics.ApplyCurrent();
        ApplyFrameGlobals();
    }

    void OnEnable()
    {
        ResetTransientDebugGlobals();
        GrassRenderDiagnostics.ApplyCurrent();
        ApplyFrameGlobals();
    }

    void Update()
    {
        ApplyFrameGlobals();
    }

    void ApplyFrameGlobals()
    {
        float period = Mathf.Max(1f, GameTimePeriodSeconds);
        Shader.SetGlobalFloat(ShaderGlobalIds.GameTime, Mathf.Repeat(Time.time, period));
    }

    static void ResetTransientDebugGlobals()
    {
        Shader.SetGlobalInt(ShaderGlobalIds.OceanDebugMode, DebugModeConstants.Off);
        Shader.SetGlobalFloat(ShaderGlobalIds.WaterFocusMode, 0f);
        Shader.SetGlobalFloat(ShaderGlobalIds.OceanFocusMode, 0f);
    }
}
