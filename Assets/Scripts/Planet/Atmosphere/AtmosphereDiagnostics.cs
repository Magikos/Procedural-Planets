using UnityEngine;

/// <summary>
/// Captures atmosphere shader diagnostic data at key screen positions.
/// Attach to the camera. Press F12 to dump diagnostics to console and file.
/// Shows real-time overlay with key values.
/// </summary>
public class AtmosphereDiagnostics : MonoBehaviour
{
    [Header("References")]
    public AtmosphereController AtmosphereController;

    Camera _cam;
    Texture2D _screenCapture;
    bool _captureRequested;

    void Start()
    {
        _cam = GetComponent<Camera>();
    }

    void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current.f12Key.wasPressedThisFrame)
            _captureRequested = true;
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        Graphics.Blit(src, dest);

        if (_captureRequested)
        {
            _captureRequested = false;
            CaptureAndDump(src);
        }
    }

    void CaptureAndDump(RenderTexture src)
    {
        int w = src.width;
        int h = src.height;

        if (_screenCapture == null || _screenCapture.width != w || _screenCapture.height != h)
            _screenCapture = new Texture2D(w, h, TextureFormat.RGBAFloat, false);

        RenderTexture.active = src;
        _screenCapture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        _screenCapture.Apply();
        RenderTexture.active = null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== ATMOSPHERE DIAGNOSTICS ===");
        sb.AppendLine($"Screen: {w}x{h}");
        sb.AppendLine($"Camera pos: {_cam.transform.position}");
        sb.AppendLine($"Camera fwd: {_cam.transform.forward}");
        sb.AppendLine();

        // Controller values
        if (AtmosphereController != null)
        {
            var ac = AtmosphereController;
            sb.AppendLine("--- Controller Settings ---");
            sb.AppendLine($"PlanetRadius (from shader): {Shader.GetGlobalFloat("_PlanetRadius"):F1}");
            sb.AppendLine($"AtmosphereRadius (from shader): {Shader.GetGlobalFloat("_AtmosphereRadius"):F1}");
            sb.AppendLine($"AtmosphereScale: {ac.AtmosphereScale}");
            sb.AppendLine($"Intensity: {ac.Intensity}");
            sb.AppendLine($"RayleighScattering: {ac.RayleighScattering}");
            sb.AppendLine($"RayleighFalloff: {ac.RayleighFalloff}");
            sb.AppendLine($"MieStrength: {ac.MieStrength}");
            sb.AppendLine($"MieFalloff: {ac.MieFalloff}");
            sb.AppendLine($"MieAnisotropy: {ac.MieAnisotropy}");
            sb.AppendLine($"HeightAbsorption: {ac.HeightAbsorption}");
            sb.AppendLine($"InScatteringPoints: {ac.InScatteringPoints}");
            sb.AppendLine($"BakeSteps: {ac.BakeSteps}");
            sb.AppendLine();
        }

        // Sample key screen positions
        sb.AppendLine("--- Screen Samples (RGB at key positions) ---");
        SamplePixel(sb, "Center", w / 2, h / 2);
        SamplePixel(sb, "Top (zenith)", w / 2, h - h / 8);
        SamplePixel(sb, "Bottom (ground)", w / 2, h / 8);
        SamplePixel(sb, "Left horizon", w / 8, h / 2);
        SamplePixel(sb, "Right horizon", w - w / 8, h / 2);
        SamplePixel(sb, "Top-left", w / 4, 3 * h / 4);
        SamplePixel(sb, "Top-right", 3 * w / 4, 3 * h / 4);
        SamplePixel(sb, "Bottom-left", w / 4, h / 4);
        SamplePixel(sb, "Bottom-right", 3 * w / 4, h / 4);
        sb.AppendLine();

        // Sample a vertical strip down the center (sky to ground)
        sb.AppendLine("--- Vertical Strip (center column, top to bottom) ---");
        for (int i = 0; i <= 10; i++)
        {
            int y = (int)(h * (1f - i / 10f));
            y = Mathf.Clamp(y, 0, h - 1);
            Color c = _screenCapture.GetPixel(w / 2, y);
            float pct = i * 10;
            sb.AppendLine($"  {pct,3:F0}% from top: R={c.r:F4} G={c.g:F4} B={c.b:F4} (luminance={c.r * 0.299f + c.g * 0.587f + c.b * 0.114f:F4})");
        }
        sb.AppendLine();

        // Color analysis
        sb.AppendLine("--- Color Analysis ---");
        AnalyzeRegion(sb, "Sky (top quarter)", w / 4, 3 * h / 4, w / 2, h / 4);
        AnalyzeRegion(sb, "Horizon (middle strip)", 0, h / 2 - h / 16, w, h / 8);
        AnalyzeRegion(sb, "Ground (bottom quarter)", w / 4, 0, w / 2, h / 4);
        sb.AppendLine();

        // Dominant color check
        Color center = _screenCapture.GetPixel(w / 2, h / 2);
        sb.AppendLine("--- Quick Assessment ---");
        if (center.r > 0.9f && center.g > 0.9f && center.b > 0.9f)
            sb.AppendLine("CENTER IS WHITE — in-scattered light is blowing out all channels");
        else if (center.r < 0.05f && center.g < 0.05f && center.b < 0.05f)
            sb.AppendLine("CENTER IS BLACK — no scattering reaching camera");
        else if (Mathf.Abs(center.r - center.g) < 0.05f && Mathf.Abs(center.g - center.b) < 0.05f)
            sb.AppendLine($"CENTER IS GRAY ({center.r:F3}) — coefficients may be too uniform");
        else if (center.b > center.r * 1.5f && center.b > center.g)
            sb.AppendLine("CENTER IS BLUE — Rayleigh scattering working correctly!");
        else if (center.r > center.b * 1.5f)
            sb.AppendLine("CENTER IS RED/ORANGE — sunset-like scattering");
        else
            sb.AppendLine($"CENTER: R={center.r:F3} G={center.g:F3} B={center.b:F3}");

        string output = sb.ToString();
        Debug.Log(output);

        string path = System.IO.Path.Combine(Application.dataPath, "..", "atmosphere_diagnostics.txt");
        System.IO.File.WriteAllText(path, output);
        Debug.Log($"[AtmosphereDiagnostics] Written to: {path}");
    }

    void SamplePixel(System.Text.StringBuilder sb, string label, int x, int y)
    {
        y = Mathf.Clamp(y, 0, _screenCapture.height - 1);
        x = Mathf.Clamp(x, 0, _screenCapture.width - 1);
        Color c = _screenCapture.GetPixel(x, y);
        sb.AppendLine($"  {label,-20} ({x,4},{y,4}): R={c.r:F4} G={c.g:F4} B={c.b:F4} A={c.a:F4}");
    }

    void AnalyzeRegion(System.Text.StringBuilder sb, string label, int startX, int startY, int width, int height)
    {
        float sumR = 0, sumG = 0, sumB = 0;
        float maxR = 0, maxG = 0, maxB = 0;
        float minR = 1, minG = 1, minB = 1;
        int samples = 0;

        int step = Mathf.Max(1, Mathf.Min(width, height) / 10);
        for (int x = startX; x < startX + width && x < _screenCapture.width; x += step)
        {
            for (int y = startY; y < startY + height && y < _screenCapture.height; y += step)
            {
                Color c = _screenCapture.GetPixel(x, Mathf.Clamp(y, 0, _screenCapture.height - 1));
                sumR += c.r; sumG += c.g; sumB += c.b;
                maxR = Mathf.Max(maxR, c.r); maxG = Mathf.Max(maxG, c.g); maxB = Mathf.Max(maxB, c.b);
                minR = Mathf.Min(minR, c.r); minG = Mathf.Min(minG, c.g); minB = Mathf.Min(minB, c.b);
                samples++;
            }
        }

        if (samples > 0)
        {
            sb.AppendLine($"  {label} ({samples} samples):");
            sb.AppendLine($"    Avg: R={sumR / samples:F4} G={sumG / samples:F4} B={sumB / samples:F4}");
            sb.AppendLine($"    Min: R={minR:F4} G={minG:F4} B={minB:F4}");
            sb.AppendLine($"    Max: R={maxR:F4} G={maxG:F4} B={maxB:F4}");
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 260, 10, 250, 120));
        GUILayout.Label("[F12] Dump Atmosphere Diagnostics");

        if (_screenCapture != null)
        {
            Color c = _screenCapture.GetPixel(Screen.width / 2, Screen.height / 2);
            GUILayout.Label($"Center: R={c.r:F3} G={c.g:F3} B={c.b:F3}");

            float pr = Shader.GetGlobalFloat("_PlanetRadius");
            float ar = Shader.GetGlobalFloat("_AtmosphereRadius");
            GUILayout.Label($"Radius: {pr:F0} Atmo: {ar:F0}");
        }
        GUILayout.EndArea();
    }
}
