using System;
using UnityEngine;

sealed class DebugOverlayHud : IDisposable
{
    static readonly Color BackgroundColor = new(0f, 0f, 0f, 0.55f);

    bool _precipitationToggleFlashActive;
    float _precipitationToggleFlashUntil;
    string _precipitationToggleFlashMessage;
    Vector2 _scroll;
    GUIStyle _panelStyle;
    Texture2D _panelTexture;

    public void NotifyPrecipitationToggle(bool enabled)
    {
        _precipitationToggleFlashActive = true;
        _precipitationToggleFlashUntil = Time.unscaledTime + 1.2f;
        _precipitationToggleFlashMessage = $"Precipitation: {(enabled ? "ON" : "OFF")}";
    }

    public void Draw(
        DebugRegistry registry,
        DebugModeId currentModeId,
        DebugCaptureSetDefinition captureSet,
        ICameraRigContext cameraContext,
        ICelestialTimeController celestial,
        IPrecipitationDebugControl precipitation,
        bool showDetailed,
        DebugRuntimeState runtimeState)
    {
        if (cameraContext == null)
            return;

        GUILayout.BeginArea(GetOverlayRect(showDetailed), GetPanelStyle());
        _scroll = GUILayout.BeginScrollView(_scroll);

        GUILayout.Label("Camera");
        GUILayout.Label($"Position: {cameraContext.CameraTransform.position.x:F1}, {cameraContext.CameraTransform.position.y:F1}, {cameraContext.CameraTransform.position.z:F1}");
        if (cameraContext.TargetCenter != null)
        {
            Vector3 dirToSurface = (cameraContext.CameraTransform.position - cameraContext.TargetCenter.position).normalized;
            (float lat, float lon) = CoordinateConverter.UnitSphereToLatLong(dirToSurface);
            GUILayout.Label($"Lat: {lat * Mathf.Rad2Deg:F1}° Lon: {lon * Mathf.Rad2Deg:F1}°");
            float distToCenter = Vector3.Distance(cameraContext.CameraTransform.position, cameraContext.TargetCenter.position);
            GUILayout.Label($"Distance to center: {distToCenter:F1}");
        }

        GUILayout.Space(6);
        GUILayout.Label("Performance");
        GUILayout.Label($"FPS: {1f / Time.unscaledDeltaTime:F0}");
        GUILayout.Label($"Frame: CPU={FormatFrameMs(FrameTimingCounters.LastCpuFrameMs)} GPU={FormatFrameMs(FrameTimingCounters.LastGpuFrameMs)}");

        if (_precipitationToggleFlashActive)
        {
            if (Time.unscaledTime <= _precipitationToggleFlashUntil)
                GUILayout.Label(_precipitationToggleFlashMessage);
            else
                _precipitationToggleFlashActive = false;
        }

        if (!showDetailed)
            GUILayout.Label("F9 = detailed debug");

        if (showDetailed)
        {
            GUILayout.Space(6);
            GUILayout.Label("Controls");
            GUILayout.Label("RMB=Look, WASD=Move, Shift=Fast, QE=Up/Down, ZC=Roll");
            GUILayout.Label("Space=Toggle Orbit/Surface, Backspace=Face Sun, R=Frame Storm");
            GUILayout.Label("F6=Debug UI, F7=Cycle F10 Set, F8=Freeze Sun, F9=Detailed, F11=FPS Cap, P=Precip");
            GUILayout.Label("M=Drop scale markers @ look, Shift+M=Clear, T=Teleport to markers");

            GUILayout.Space(6);
            GUILayout.Label("State");
            GUILayout.Label($"F10={captureSet.Name} capture ({registry.GetCaptureModeIds(captureSet, currentModeId).Length} modes, current {registry.GetModeName(currentModeId)})");
            if (precipitation != null)
            {
                GUILayout.Label($"Precipitation render: {(precipitation.PrecipitationRenderingEnabled ? "ON" : "OFF")}");
                GUILayout.Label($"Precip local particles: {(precipitation.ShouldRenderLocalParticles(cameraContext.CameraComponent) ? "ON" : "OFF")}");
            }

            GUILayout.Label($"Frame target: {Application.targetFrameRate}, vSync: {QualitySettings.vSyncCount}");

            if (celestial != null)
                GUILayout.Label($"Sun frozen: {(celestial.IsTimeFrozen ? "yes" : "no")}");

            if (celestial != null && cameraContext.PlanetRadius > 0f)
            {
                float sunElevation = Vector3.Dot(celestial.SunDirection, (cameraContext.CameraTransform.position - cameraContext.PlanetCenter).normalized);
                GUILayout.Label($"Sun elevation: {Mathf.Asin(sunElevation) * Mathf.Rad2Deg:F1}°");
            }
        }

        for (int i = 0; i < registry.OverlayContributors.Count; i++)
            registry.OverlayContributors[i].DrawOverlay(runtimeState);

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    public void DrawHint()
    {
        GUILayout.BeginArea(new Rect(10f, 10f, 132f, 30f), GetPanelStyle());
        GUILayout.Label("F6: debug data");
        GUILayout.EndArea();
    }

    public void Dispose()
    {
        if (_panelTexture != null)
        {
            UnityEngine.Object.Destroy(_panelTexture);
            _panelTexture = null;
            _panelStyle = null;
        }
    }

    static Rect GetOverlayRect(bool showDetailed)
    {
        float width = Mathf.Min(820f, Mathf.Max(320f, Screen.width - 20f));
        float available = Screen.height - 20f;
        float height = showDetailed ? available : Mathf.Min(230f, available);
        return new Rect(10f, 10f, width, height);
    }

    GUIStyle GetPanelStyle()
    {
        if (_panelStyle != null)
            return _panelStyle;

        _panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        _panelTexture.SetPixel(0, 0, BackgroundColor);
        _panelTexture.Apply();

        _panelStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = _panelTexture },
            padding = new RectOffset(10, 10, 8, 8),
            border = new RectOffset(4, 4, 4, 4)
        };

        return _panelStyle;
    }

    static string FormatFrameMs(double ms) => ms < 0.0 ? "?" : $"{ms:F1}ms";
}
