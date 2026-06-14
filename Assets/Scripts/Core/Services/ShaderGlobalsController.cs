using UnityEngine;

[DisallowMultipleComponent]
public sealed class ShaderGlobalsController : MonoBehaviour
{
    [Tooltip("Keeps shader time small enough to avoid long-session float precision loss.")]
    [Min(1f)] public float GameTimePeriodSeconds = 3600f;

    static readonly int _gameTimeId = Shader.PropertyToID(ShaderGlobalIds.GameTime);
    static readonly int _oceanDebugModeId = Shader.PropertyToID(ShaderGlobalIds.OceanDebugMode);
    static readonly int _debugSuppressWeatherPassesId = Shader.PropertyToID(ShaderGlobalIds.DebugSuppressWeatherPasses);
    static readonly int _waterFocusModeId = Shader.PropertyToID(ShaderGlobalIds.WaterFocusMode);
    static readonly int _oceanFocusModeId = Shader.PropertyToID(ShaderGlobalIds.OceanFocusMode);

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

    void LateUpdate()
    {
        ApplyFrameGlobals();
    }

    void ApplyFrameGlobals()
    {
        float period = Mathf.Max(1f, GameTimePeriodSeconds);
        Shader.SetGlobalFloat(_gameTimeId, Mathf.Repeat(Time.time, period));
    }

    static void ResetTransientDebugGlobals()
    {
        Shader.SetGlobalInt(_oceanDebugModeId, DebugModeConstants.Off);
        Shader.SetGlobalFloat(_debugSuppressWeatherPassesId, 0f);
        Shader.SetGlobalFloat(_waterFocusModeId, 0f);
        Shader.SetGlobalFloat(_oceanFocusModeId, 0f);
    }
}
