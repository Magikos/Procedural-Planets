using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Captures atmosphere diagnostic data. Press F12 to dump.
/// Add to any active GameObject (e.g. Camera).
/// No references needed — reads shader globals directly.
/// </summary>
public class AtmosphereDiagnostics : MonoBehaviour
{
    bool _captureRequested;
    Keyboard _keyboard;

    void Start()
    {
        _keyboard = Keyboard.current;
    }

    void Update()
    {
        if (_keyboard != null && _keyboard.f12Key.wasPressedThisFrame)
        {
            LoggerProvider.Log(LogLevel.Debug, "AtmosphereDiagnostics", "F12 pressed, capturing...");
            _captureRequested = true;
        }
    }

    void LateUpdate()
    {
        if (_captureRequested)
        {
            _captureRequested = false;
            _ = CaptureAsync();
        }
    }

    async Awaitable CaptureAsync()
    {
        await Awaitable.EndOfFrameAsync();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            DumpDiagnostics(tex);
        }
        finally
        {
            if (tex != null)
                Destroy(tex);
        }
    }

    void DumpDiagnostics(Texture2D tex)
    {
        int w = tex.width;
        int h = tex.height;
        var cam = Camera.main;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== ATMOSPHERE DIAGNOSTICS ===");
        sb.AppendLine($"Screen: {w}x{h}");
        if (cam != null)
        {
            sb.AppendLine($"Camera pos: {cam.transform.position}");
            sb.AppendLine($"Camera fwd: {cam.transform.forward}");
        }
        sb.AppendLine();

        // Read shader globals directly
        sb.AppendLine("--- Shader Globals ---");
        sb.AppendLine($"_PlanetRadius: {Shader.GetGlobalFloat("_PlanetRadius"):F1}");
        sb.AppendLine($"_DensityOriginRadius: {Shader.GetGlobalFloat("_DensityOriginRadius"):F1}");
        sb.AppendLine($"_AtmosphereRadius: {Shader.GetGlobalFloat("_AtmosphereRadius"):F1}");
        Vector4 rc = Shader.GetGlobalVector("_RayleighScattering");
        sb.AppendLine($"_RayleighScattering: ({rc.x:E3}, {rc.y:E3}, {rc.z:E3})");
        sb.AppendLine($"_RayleighScaleHeight: {Shader.GetGlobalFloat("_RayleighScaleHeight"):F2}");
        sb.AppendLine($"_MieScatteringCoeff: {Shader.GetGlobalFloat("_MieScatteringCoeff"):E3}");
        sb.AppendLine($"_MieScaleHeight: {Shader.GetGlobalFloat("_MieScaleHeight"):F2}");
        sb.AppendLine($"_MieAnisotropy: {Shader.GetGlobalFloat("_MieAnisotropy"):F3}");
        sb.AppendLine($"_SunIntensity: {Shader.GetGlobalFloat("_SunIntensity"):F2}");
        sb.AppendLine($"_ViewSteps: {Shader.GetGlobalFloat("_ViewSteps"):F0}");
        sb.AppendLine($"_SunSteps: {Shader.GetGlobalFloat("_SunSteps"):F0}");
        sb.AppendLine($"_DebugMode: {Shader.GetGlobalFloat("_DebugMode"):F0}");
        sb.AppendLine($"_StarSeed: {Shader.GetGlobalFloat("_StarSeed"):F2}");
        sb.AppendLine($"_StarDensity: {Shader.GetGlobalFloat("_StarDensity"):F0}");
        sb.AppendLine($"_StarBrightness: {Shader.GetGlobalFloat("_StarBrightness"):F2}");
        sb.AppendLine($"_NightAmbientIntensity: {Shader.GetGlobalFloat("_NightAmbientIntensity"):F3}");
        Vector4 sunDir = Shader.GetGlobalVector("_SunParams");
        sb.AppendLine($"_SunParams: ({sunDir.x:F3}, {sunDir.y:F3}, {sunDir.z:F3})");
        if (cam != null)
        {
            float sunAngle = Vector3.Angle(cam.transform.forward, new Vector3(sunDir.x, sunDir.y, sunDir.z));
            sb.AppendLine($"Angle camera->sun: {sunAngle:F1}° (0=looking at sun, 90=perpendicular, 180=away)");
            float camDist = Vector3.Distance(cam.transform.position, Vector3.zero);
            sb.AppendLine($"Camera distance from origin: {camDist:F1} (planetRadius={Shader.GetGlobalFloat("_PlanetRadius"):F1})");
        }
        sb.AppendLine();

        // Screen samples
        sb.AppendLine("--- Screen Samples ---");
        SamplePixel(sb, "Center", tex, w / 2, h / 2);
        SamplePixel(sb, "Top (zenith)", tex, w / 2, h - h / 8);
        SamplePixel(sb, "Bottom (ground)", tex, w / 2, h / 8);
        SamplePixel(sb, "Left", tex, w / 8, h / 2);
        SamplePixel(sb, "Right", tex, w - w / 8, h / 2);
        sb.AppendLine();

        // Vertical strip
        sb.AppendLine("--- Vertical Strip (center, top to bottom) ---");
        for (int i = 0; i <= 10; i++)
        {
            int y = Mathf.Clamp((int)(h * (1f - i / 10f)), 0, h - 1);
            Color c = tex.GetPixel(w / 2, y);
            sb.AppendLine($"  {i * 10,3}%: R={c.r:F4} G={c.g:F4} B={c.b:F4}");
        }
        sb.AppendLine();

        // Region averages
        sb.AppendLine("--- Region Averages ---");
        AnalyzeRegion(sb, "Sky (top quarter)", tex, w / 4, 3 * h / 4, w / 2, h / 4);
        AnalyzeRegion(sb, "Horizon (mid strip)", tex, 0, h / 2 - h / 16, w, h / 8);
        AnalyzeRegion(sb, "Ground (bottom qtr)", tex, w / 4, 0, w / 2, h / 4);
        sb.AppendLine();

        // Assessment
        Color center = tex.GetPixel(w / 2, h / 2);
        sb.AppendLine("--- Assessment ---");
        float maxC = Mathf.Max(center.r, center.g, center.b);
        float minC = Mathf.Min(center.r, center.g, center.b);
        if (maxC > 0.9f && minC > 0.8f)
            sb.AppendLine("WHITE — all channels blown out");
        else if (maxC < 0.05f)
            sb.AppendLine("BLACK — no light reaching camera");
        else if (maxC - minC < 0.03f)
            sb.AppendLine($"GRAY ({center.r:F3},{center.g:F3},{center.b:F3}) — no wavelength differentiation");
        else if (center.b > center.r * 1.3f)
            sb.AppendLine($"BLUE TINT — B/R ratio: {center.b / Mathf.Max(0.001f, center.r):F2}x");
        else if (center.r > center.b * 1.3f)
            sb.AppendLine($"RED/ORANGE TINT — R/B ratio: {center.r / Mathf.Max(0.001f, center.b):F2}x");
        else
            sb.AppendLine($"NEUTRAL — R={center.r:F3} G={center.g:F3} B={center.b:F3}");

        string output = sb.ToString();
        LoggerProvider.Log(LogLevel.Info, "AtmosphereDiagnostics", output);

        string path = System.IO.Path.Combine(Application.dataPath, "..", "atmosphere_diagnostics.txt");
        System.IO.File.WriteAllText(path, output);
        LoggerProvider.Log(LogLevel.Info, "AtmosphereDiagnostics", $"Saved to: {path}");
    }

    void SamplePixel(System.Text.StringBuilder sb, string label, Texture2D tex, int x, int y)
    {
        x = Mathf.Clamp(x, 0, tex.width - 1);
        y = Mathf.Clamp(y, 0, tex.height - 1);
        Color c = tex.GetPixel(x, y);
        sb.AppendLine($"  {label,-20}: R={c.r:F4} G={c.g:F4} B={c.b:F4}");
    }

    void AnalyzeRegion(System.Text.StringBuilder sb, string label, Texture2D tex, int startX, int startY, int width, int height)
    {
        float sumR = 0, sumG = 0, sumB = 0;
        int samples = 0;
        int step = Mathf.Max(1, Mathf.Min(width, height) / 10);

        for (int x = startX; x < startX + width && x < tex.width; x += step)
        {
            for (int y = startY; y < startY + height && y < tex.height; y += step)
            {
                Color c = tex.GetPixel(Mathf.Clamp(x, 0, tex.width - 1), Mathf.Clamp(y, 0, tex.height - 1));
                sumR += c.r; sumG += c.g; sumB += c.b;
                samples++;
            }
        }

        if (samples > 0)
            sb.AppendLine($"  {label}: Avg R={sumR / samples:F4} G={sumG / samples:F4} B={sumB / samples:F4}");
    }
}
