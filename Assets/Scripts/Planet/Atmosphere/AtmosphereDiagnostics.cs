using UnityEngine;

/// <summary>
/// Captures atmosphere shader diagnostic data at key screen positions.
/// Attach to the camera. Press F12 to dump diagnostics to console and file.
/// </summary>
public class AtmosphereDiagnostics : MonoBehaviour
{
    [Header("References")]
    public AtmosphereController AtmosphereController;

    bool _captureRequested;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F12))
            _captureRequested = true;
    }

    void OnEndOfFrame()
    {
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        DumpDiagnostics(tex);
        Destroy(tex);
    }

    System.Collections.IEnumerator CaptureCoroutine()
    {
        yield return new WaitForEndOfFrame();
        OnEndOfFrame();
    }

    void LateUpdate()
    {
        if (_captureRequested)
        {
            _captureRequested = false;
            StartCoroutine(CaptureCoroutine());
        }
    }

    void DumpDiagnostics(Texture2D tex)
    {
        int w = tex.width;
        int h = tex.height;
        var cam = GetComponent<Camera>();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== ATMOSPHERE DIAGNOSTICS ===");
        sb.AppendLine($"Screen: {w}x{h}");
        if (cam != null)
        {
            sb.AppendLine($"Camera pos: {cam.transform.position}");
            sb.AppendLine($"Camera fwd: {cam.transform.forward}");
        }
        sb.AppendLine();

        if (AtmosphereController != null)
        {
            var ac = AtmosphereController;
            sb.AppendLine("--- Controller Settings ---");
            sb.AppendLine($"PlanetRadius (shader): {Shader.GetGlobalFloat("_PlanetRadius"):F1}");
            sb.AppendLine($"AtmosphereRadius (shader): {Shader.GetGlobalFloat("_AtmosphereRadius"):F1}");
            sb.AppendLine($"AtmosphereScale: {ac.AtmosphereScale}");
            sb.AppendLine($"Intensity: {ac.Intensity}");
            sb.AppendLine($"RayleighScattering: {ac.RayleighScattering}");
            sb.AppendLine($"RayleighFalloff: {ac.RayleighFalloff}");
            sb.AppendLine($"MieStrength: {ac.MieStrength}");
            sb.AppendLine($"MieFalloff: {ac.MieFalloff}");
            sb.AppendLine($"MieAnisotropy: {ac.MieAnisotropy}");
            sb.AppendLine($"HeightAbsorption: {ac.HeightAbsorption}");
            sb.AppendLine($"AbsorptionBeta: {ac.AbsorptionBeta}");
            sb.AppendLine($"InScatteringPoints: {ac.InScatteringPoints}");
            sb.AppendLine($"BakeSteps: {ac.BakeSteps}");
            sb.AppendLine();
        }

        sb.AppendLine("--- Screen Samples ---");
        SamplePixel(sb, "Center", tex, w / 2, h / 2);
        SamplePixel(sb, "Top (zenith)", tex, w / 2, h - h / 8);
        SamplePixel(sb, "Bottom (ground)", tex, w / 2, h / 8);
        SamplePixel(sb, "Left horizon", tex, w / 8, h / 2);
        SamplePixel(sb, "Right horizon", tex, w - w / 8, h / 2);
        sb.AppendLine();

        sb.AppendLine("--- Vertical Strip (center column, top to bottom) ---");
        for (int i = 0; i <= 10; i++)
        {
            int y = (int)(h * (1f - i / 10f));
            y = Mathf.Clamp(y, 0, h - 1);
            Color c = tex.GetPixel(w / 2, y);
            sb.AppendLine($"  {i * 10,3}% from top: R={c.r:F4} G={c.g:F4} B={c.b:F4}");
        }
        sb.AppendLine();

        sb.AppendLine("--- Region Averages ---");
        AnalyzeRegion(sb, "Sky (top quarter)", tex, w / 4, 3 * h / 4, w / 2, h / 4);
        AnalyzeRegion(sb, "Horizon (mid strip)", tex, 0, h / 2 - h / 16, w, h / 8);
        AnalyzeRegion(sb, "Ground (bottom quarter)", tex, w / 4, 0, w / 2, h / 4);
        sb.AppendLine();

        Color center = tex.GetPixel(w / 2, h / 2);
        sb.AppendLine("--- Quick Assessment ---");
        if (center.r > 0.9f && center.g > 0.9f && center.b > 0.9f)
            sb.AppendLine("CENTER IS WHITE - scattering blowing out all channels");
        else if (center.r < 0.05f && center.g < 0.05f && center.b < 0.05f)
            sb.AppendLine("CENTER IS BLACK - no scattering reaching camera");
        else if (Mathf.Abs(center.r - center.g) < 0.05f && Mathf.Abs(center.g - center.b) < 0.05f)
            sb.AppendLine($"CENTER IS GRAY ({center.r:F3}) - no wavelength differentiation");
        else if (center.b > center.r * 1.5f && center.b > center.g)
            sb.AppendLine("CENTER IS BLUE - Rayleigh scattering working!");
        else if (center.r > center.b * 1.5f)
            sb.AppendLine("CENTER IS RED/ORANGE - sunset scattering");
        else
            sb.AppendLine($"CENTER: R={center.r:F3} G={center.g:F3} B={center.b:F3}");

        string output = sb.ToString();
        Debug.Log(output);

        string path = System.IO.Path.Combine(Application.dataPath, "..", "atmosphere_diagnostics.txt");
        System.IO.File.WriteAllText(path, output);
        Debug.Log($"[AtmosphereDiagnostics] Saved to: {path}");
    }

    void SamplePixel(System.Text.StringBuilder sb, string label, Texture2D tex, int x, int y)
    {
        x = Mathf.Clamp(x, 0, tex.width - 1);
        y = Mathf.Clamp(y, 0, tex.height - 1);
        Color c = tex.GetPixel(x, y);
        sb.AppendLine($"  {label,-20} ({x,4},{y,4}): R={c.r:F4} G={c.g:F4} B={c.b:F4}");
    }

    void AnalyzeRegion(System.Text.StringBuilder sb, string label, Texture2D tex, int startX, int startY, int width, int height)
    {
        float sumR = 0, sumG = 0, sumB = 0;
        float maxR = 0, maxG = 0, maxB = 0;
        float minR = 1, minG = 1, minB = 1;
        int samples = 0;

        int step = Mathf.Max(1, Mathf.Min(width, height) / 10);
        for (int x = startX; x < startX + width && x < tex.width; x += step)
        {
            for (int y = startY; y < startY + height && y < tex.height; y += step)
            {
                Color c = tex.GetPixel(Mathf.Clamp(x, 0, tex.width - 1), Mathf.Clamp(y, 0, tex.height - 1));
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
}
