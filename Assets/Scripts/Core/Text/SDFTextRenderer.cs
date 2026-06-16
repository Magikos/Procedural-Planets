using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Manages a text mesh + SDFText material for rendering a single text string
/// via a <see cref="CommandBuffer"/> (e.g. inside an endCameraRendering callback).
///
/// Usage example (inside a MonoBehaviour or manager class):
/// <code>
///     var font = Resources.Load&lt;SDFFontAsset&gt;("DefaultFont");
///     _textRenderer = new SDFTextRenderer(font);
///     _textRenderer.SetText("Loading…", 0.5f, 0.17f, 0.035f, Color.white, centerX: true);
///
///     // inside endCameraRendering callback:
///     _textRenderer.SetAlpha(_overlayAlpha);
///     _textRenderer.Draw(cmd);
///
///     // when done:
///     _textRenderer.Dispose();
/// </code>
///
/// The renderer is intentionally not a MonoBehaviour so it can be owned inline
/// by any manager class and tied to that manager's lifecycle.
/// </summary>
public sealed class SDFTextRenderer
{
    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private readonly SDFFontAsset _font;
    private Material _material;
    private Mesh _mesh;

    // Cached params that require a mesh rebuild when they change.
    private string _lastText;
    private float _lastX;
    private float _lastY;
    private float _lastEmSize;
    private TextAnchor _lastAlignment;

    // -------------------------------------------------------------------------
    // Construction / destruction
    // -------------------------------------------------------------------------

    /// <param name="font">
    ///   The font asset to use.  If <c>null</c>, all calls become no-ops (graceful
    ///   degradation when the asset hasn't been set up yet).
    /// </param>
    public SDFTextRenderer(SDFFontAsset font)
    {
        _font = font;
        if (font == null)
        {
            LoggerProvider.Get().Log(LogLevel.Warning, "SDFTextRenderer", "No font asset supplied — text rendering disabled.");
            return;
        }

        var shader = Shader.Find("Hidden/SDFText");
        if (shader == null)
        {
            LoggerProvider.Get().Log(LogLevel.Error, "SDFTextRenderer", "Shader 'Hidden/SDFText' not found. " +
                           "Ensure it is in Assets/Graphics/Shaders/ and listed under " +
                           "Project Settings → Graphics → Always Included Shaders.");
            return;
        }

        _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        if (font.Atlas != null)
            _material.SetTexture("_MainTex", font.Atlas);

        _material.SetFloat("_PxRange", font.PixelRange);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Updates the rendered string and layout.
    /// Rebuilds the underlying mesh only when the text or layout parameters change.
    /// </summary>
    /// <param name="text">String to render ('\n' supported).</param>
    /// <param name="screenX">Baseline start X in normalised screen space [0..1].</param>
    /// <param name="screenY">Baseline Y in normalised screen space [0..1].</param>
    /// <param name="emSize">Em height in normalised screen units (e.g. 0.035 = 3.5 % of screen).</param>
    /// <param name="alignment">Horizontal alignment relative to <paramref name="screenX"/>.</param>
    public void SetText(
        string text,
        float screenX,
        float screenY,
        float emSize,
        TextAnchor alignment = TextAnchor.UpperLeft)
    {
        if (_font == null || _material == null) return;

        bool same = text == _lastText
                 && Mathf.Approximately(screenX, _lastX)
                 && Mathf.Approximately(screenY, _lastY)
                 && Mathf.Approximately(emSize, _lastEmSize)
                 && alignment == _lastAlignment;

        if (same) return;

        _lastText = text;
        _lastX = screenX;
        _lastY = screenY;
        _lastEmSize = emSize;
        _lastAlignment = alignment;

        if (_mesh != null) { Object.Destroy(_mesh); _mesh = null; }
        if (!string.IsNullOrEmpty(text))
            _mesh = SDFTextMeshBuilder.BuildScreen(text, _font, screenX, screenY, emSize,
                                                   Color.white, alignment);
    }

    /// <summary>
    /// Updates the rendered string from a list of colour spans (multi-colour text).
    /// Uses <see cref="TextAnchor.UpperLeft"/> layout only.
    /// The mesh is rebuilt on every call; call only when suggestions change.
    /// </summary>
    public void SetSpans(
        IList<TextSpan> spans,
        float screenX,
        float screenY,
        float emSize)
    {
        if (_font == null || _material == null) return;

        if (_mesh != null) { Object.Destroy(_mesh); _mesh = null; }

        if (spans != null && spans.Count > 0)
            _mesh = SDFTextMeshBuilder.BuildScreenSpans(spans, _font, screenX, screenY, emSize);

        // Invalidate the SetText dirty-tracking cache so a subsequent SetText call rebuilds.
        _lastText = null;
    }

    /// <summary>
    /// Sets the face colour alpha on the material.  Use this for fading without
    /// rebuilding the mesh (e.g. during the loading screen fade-in / fade-out).
    /// </summary>
    public void SetAlpha(float alpha)
    {
        if (_material == null) return;
        var c = _material.GetColor("_FaceColor");
        c.a = alpha;
        _material.SetColor("_FaceColor", c);
    }

    /// <summary>
    /// Sets the full face colour (RGB + alpha) on the material.
    /// </summary>
    public void SetColor(Color color)
    {
        _material?.SetColor("_FaceColor", color);
    }

    /// <summary>
    /// Sets a screen-space clip rectangle on the material. Fragments outside the rect are discarded.
    /// Coordinates are in normalised screen space (0=bottom-left, 1=top-right).
    /// Default (no clip) is roughly (-100, -100, 100, 100).
    /// </summary>
    public void SetClipRect(Vector4 rect)
    {
        _material?.SetVector("_ClipRect", rect);
    }

    /// <summary>
    /// Emits a <c>DrawMesh</c> call into <paramref name="cmd"/>.
    /// Call this after drawing any background overlay so the text composites on top.
    /// </summary>
    public void Draw(CommandBuffer cmd)
    {
        if (_mesh == null || _material == null) return;
        cmd.DrawMesh(_mesh, Matrix4x4.identity, _material, 0, 0);
    }

    /// <summary>
    /// Destroys the managed mesh and material.  Call this in the owning class's
    /// OnDestroy (or equivalent disposal point).
    /// </summary>
    public void Dispose()
    {
        if (_mesh != null) { Object.Destroy(_mesh); _mesh = null; }
        if (_material != null) { Object.Destroy(_material); _material = null; }
    }
}
