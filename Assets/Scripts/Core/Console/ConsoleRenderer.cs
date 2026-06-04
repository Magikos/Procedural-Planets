using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class ConsoleRenderer
{
    static readonly int _boundsRectId = Shader.PropertyToID("_BoundsRect");
    static readonly int _backdropColorId = Shader.PropertyToID("_BackdropColor");
    static readonly int _borderColorId = Shader.PropertyToID("_BorderColor");
    static readonly int _borderThicknessId = Shader.PropertyToID("_BorderThickness");
    static readonly int _alphaId = Shader.PropertyToID("_Alpha");
    static readonly int _scanlineStrengthId = Shader.PropertyToID("_ScanlineStrength");

    static readonly Color DefaultBackdrop = new Color(0.02f, 0.03f, 0.05f, 0.94f);
    static readonly Color DefaultBorder = new Color(0.4f, 0.8f, 1.0f, 0.6f);
    const float DefaultBorderThickness = 0.0025f;
    const float TextEmSize = 0.022f;
    const float FallbackLineHeight = 1.2f;

    // Scrollback text colours by message type.
    static readonly Color MsgNormal = new Color(0.72f, 0.72f, 0.74f, 1f);
    static readonly Color MsgInput = new Color(0.38f, 0.38f, 0.40f, 1f);  // dim gray (echoed command)
    static readonly Color MsgOutput = new Color(0.72f, 0.72f, 0.74f, 1f);  // same as normal
    static readonly Color MsgWarning = new Color(0.95f, 0.78f, 0.30f, 1f);  // amber
    static readonly Color MsgError = new Color(0.95f, 0.38f, 0.35f, 1f);  // red
    static readonly Color MsgException = new Color(1.00f, 0.25f, 0.20f, 1f);  // bright red
    static readonly Color MsgLog = new Color(0.50f, 0.50f, 0.54f, 1f);  // dim

    // Scrollbar colours.
    static readonly Color ScrollbarBg = new Color(0.06f, 0.08f, 0.12f, 0.60f);
    static readonly Color ScrollbarThumb = new Color(0.35f, 0.80f, 0.88f, 0.55f);  // cyan, semi
    const float ScrollbarWidth = 0.004f;
    const float ScrollbarPadY = 0.006f;

    // Suggestion popup colours.
    static readonly Color SuggActive = Color.white;
    static readonly Color SuggInactive = new Color(0.58f, 0.58f, 0.60f, 1f);
    static readonly Color SuggMatchActive = new Color(0.35f, 0.80f, 0.88f, 1f);  // cyan accent
    static readonly Color SuggMatchInactive = new Color(0.28f, 0.60f, 0.70f, 1f);  // muted cyan
    static readonly Color PopupBackdrop = new Color(0.03f, 0.04f, 0.07f, 0.97f);
    static readonly Color HighlightBackdrop = new Color(0.10f, 0.25f, 0.42f, 0.90f);

    SDFFontAsset _font;
    Material _material;
    Material _popupMaterial;
    Material _highlightMaterial;
    SDFTextRenderer _inputLineRenderer;
    SDFTextRenderer _outputRenderer;
    SDFTextRenderer _suggestionsRenderer;
    readonly List<TextSpan> _suggestionSpans = new();
    readonly List<TextSpan> _inputLineSpans = new();
    readonly List<TextSpan> _outputSpans = new();
    Material _scrollbarBgMaterial;
    Material _scrollbarThumbMaterial;
    int _lastScrollbackVersion = -1;
    int _lastVisibleCount = -1;
    int _lastScrollOffset = -1;
    bool _materialMissing;

    public Color BackdropColor { get; set; } = DefaultBackdrop;
    public Color BorderColor { get; set; } = DefaultBorder;
    public float BorderThickness { get; set; } = DefaultBorderThickness;
    public float ScanlineStrength { get; set; } = 0f;

    public ConsoleRenderer()
    {
        var shader = Shader.Find("Hidden/ConsoleOverlay");
        if (shader == null)
        {
            _materialMissing = true;
            LoggerProvider.Get().Log(LogLevel.Error, "ConsoleRenderer",
                "Hidden/ConsoleOverlay shader not found. Ensure Assets/Graphics/Shaders/Hidden/ConsoleOverlay.shader is in the project and listed in Always Included Shaders.");
            return;
        }
        _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _popupMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _highlightMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _scrollbarBgMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _scrollbarThumbMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        _font = Resources.Load<SDFFontAsset>("DefaultFont");
        _inputLineRenderer = new SDFTextRenderer(_font);
        _outputRenderer = new SDFTextRenderer(_font);
        _suggestionsRenderer = new SDFTextRenderer(_font);
    }

    public void Render(
        CommandBuffer cmd,
        float alpha,
        ConsoleAnchor anchor,
        IList<TextSpan> inputSpans,
        ConsoleScrollback scrollback,
        int scrollOffset = 0,
        IReadOnlyList<Suggestion> suggestions = null,
        int activeSuggestion = 0)
    {
        if (_material == null || _materialMissing)
            return;

        Vector4 bounds = anchor.GetBoundsRect();

        _material.SetVector(_boundsRectId, bounds);
        _material.SetColor(_backdropColorId, BackdropColor);
        _material.SetColor(_borderColorId, BorderColor);
        _material.SetFloat(_borderThicknessId, BorderThickness);
        _material.SetFloat(_alphaId, alpha);
        _material.SetFloat(_scanlineStrengthId, ScanlineStrength);

        cmd.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);

        Vector2 inputOrigin = anchor.GetInputLineOrigin();
        float lineHeightUnits = (_font != null ? _font.LineHeight : FallbackLineHeight) * TextEmSize;

        // Top edge of the output area, just below the backdrop top edge so glyph caps don't kiss the border.
        float outputTopY = bounds.w - TextEmSize * 0.6f;

        // Max lines that fit between the input line and the output area's top edge.
        float available = outputTopY - inputOrigin.y - lineHeightUnits;
        int maxLines = Mathf.Max(1, Mathf.FloorToInt(available / lineHeightUnits) + 1);

        // Clip rect for text — anything outside the backdrop is discarded by the shader.
        Vector4 clipRect = bounds;
        _inputLineRenderer?.SetClipRect(clipRect);
        _outputRenderer?.SetClipRect(clipRect);

        if (_outputRenderer != null && scrollback != null && scrollback.Count > 0)
        {
            int visibleCount = Mathf.Min(maxLines, scrollback.Count);

            if (scrollback.Version != _lastScrollbackVersion
                || visibleCount != _lastVisibleCount
                || scrollOffset != _lastScrollOffset)
            {
                var window = scrollback.GetWindow(visibleCount, scrollOffset);
                BuildOutputSpans(window);
                float originY = inputOrigin.y + lineHeightUnits * window.Count;
                _outputRenderer.SetSpans(_outputSpans, inputOrigin.x, originY, TextEmSize);
                _lastScrollbackVersion = scrollback.Version;
                _lastVisibleCount = visibleCount;
                _lastScrollOffset = scrollOffset;
            }

            _outputRenderer.SetAlpha(alpha);
            _outputRenderer.Draw(cmd);

            // Scrollbar — only when there is more history than fits.
            if (scrollback.Count > maxLines)
                DrawScrollbar(cmd, alpha, bounds, inputOrigin.y, outputTopY,
                    scrollback.Count, maxLines, scrollOffset);
        }

        if (_inputLineRenderer != null && inputSpans != null && inputSpans.Count > 0)
        {
            _inputLineRenderer.SetSpans(inputSpans, inputOrigin.x, inputOrigin.y, TextEmSize);
            _inputLineRenderer.SetAlpha(alpha);
            _inputLineRenderer.Draw(cmd);
        }

        // ---- Intellisense popup ----------------------------------------
        if (suggestions != null && suggestions.Count > 0)
            DrawSuggestionsPopup(cmd, alpha, anchor, inputOrigin, lineHeightUnits, suggestions, activeSuggestion);
    }

    void DrawScrollbar(
        CommandBuffer cmd,
        float alpha,
        Vector4 consoleBounds,
        float outputBottomY,
        float outputTopY,
        int totalLines,
        int visibleLines,
        int scrollOffset)
    {
        float barRight = consoleBounds.z - ScrollbarWidth * 0.5f;
        float barLeft = consoleBounds.z - ScrollbarWidth * 1.5f;
        float barBottom = outputBottomY + ScrollbarPadY;
        float barTop = outputTopY - ScrollbarPadY;
        float barHeight = barTop - barBottom;
        if (barHeight <= 0f) return;

        // Background track.
        SetOverlayMaterial(_scrollbarBgMaterial,
            new Vector4(barLeft, barBottom, barRight, barTop),
            ScrollbarBg, ScrollbarBg, 0f, 0f, alpha);
        cmd.DrawProcedural(Matrix4x4.identity, _scrollbarBgMaterial, 0, MeshTopology.Triangles, 3);

        // Thumb size proportional to visible / total.
        float thumbFraction = Mathf.Clamp01((float)visibleLines / totalLines);
        float thumbHeight = barHeight * thumbFraction;

        // Thumb position: offset 0 = bottom, max offset = top.
        int maxOffset = totalLines - visibleLines;
        float thumbT = maxOffset > 0 ? Mathf.Clamp01((float)scrollOffset / maxOffset) : 0f;
        float thumbBottom = barBottom + thumbT * (barHeight - thumbHeight);
        float thumbTop = thumbBottom + thumbHeight;

        SetOverlayMaterial(_scrollbarThumbMaterial,
            new Vector4(barLeft, thumbBottom, barRight, thumbTop),
            ScrollbarThumb, ScrollbarThumb, 0f, 0f, alpha);
        cmd.DrawProcedural(Matrix4x4.identity, _scrollbarThumbMaterial, 0, MeshTopology.Triangles, 3);
    }

    void BuildOutputSpans(IReadOnlyList<ConsoleMessage> lines)
    {
        _outputSpans.Clear();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) _outputSpans.Add(new TextSpan(MsgNormal, "\n"));
            Color c = lines[i].Type switch
            {
                ConsoleMessageType.Input => MsgInput,
                ConsoleMessageType.Output => MsgOutput,
                ConsoleMessageType.Warning => MsgWarning,
                ConsoleMessageType.Error => MsgError,
                ConsoleMessageType.Exception => MsgException,
                ConsoleMessageType.Log => MsgLog,
                _ => MsgNormal,
            };
            _outputSpans.Add(new TextSpan(c, lines[i].Text));
        }
    }

    void DrawSuggestionsPopup(
        CommandBuffer cmd,
        float alpha,
        ConsoleAnchor anchor,
        Vector2 inputOrigin,
        float lineH,
        IReadOnlyList<Suggestion> suggestions,
        int activeIdx)
    {
        Vector4 consoleBounds = anchor.GetBoundsRect();
        const float PadY = 0.008f;
        const float PadX = 0.018f;

        // The popup sits just above the input line and grows upward.
        float popupBottom = inputOrigin.y + lineH * 0.4f;
        float popupTop = popupBottom + suggestions.Count * lineH + PadY * 2;
        var popupBounds = new Vector4(consoleBounds.x + PadX, popupBottom, consoleBounds.z - PadX, popupTop);

        // Backdrop rectangle.
        SetOverlayMaterial(_popupMaterial, popupBounds, PopupBackdrop, DefaultBorder, DefaultBorderThickness, 0f, alpha);
        cmd.DrawProcedural(Matrix4x4.identity, _popupMaterial, 0, MeshTopology.Triangles, 3);

        // Text baseline for the first row, then per-row below.
        float textY = popupTop - PadY - TextEmSize * 0.6f;

        // Active row highlight — centered on the text line's visual midpoint.
        int clampedActive = Mathf.Clamp(activeIdx, 0, suggestions.Count - 1);
        float lineBaseY = textY - clampedActive * lineH;
        float rowMidY = lineBaseY + TextEmSize * 0.2f;
        var rowBounds = new Vector4(consoleBounds.x + PadX, rowMidY - lineH * 0.5f, consoleBounds.z - PadX, rowMidY + lineH * 0.5f);
        SetOverlayMaterial(_highlightMaterial, rowBounds, HighlightBackdrop, HighlightBackdrop, 0f, 0f, alpha);
        cmd.DrawProcedural(Matrix4x4.identity, _highlightMaterial, 0, MeshTopology.Triangles, 3);

        // Build and draw suggestion text.
        BuildSuggestionSpans(suggestions, activeIdx);
        _suggestionsRenderer?.SetSpans(_suggestionSpans, consoleBounds.x + PadX + 0.008f, textY, TextEmSize);
        _suggestionsRenderer?.SetClipRect(popupBounds);
        _suggestionsRenderer?.SetAlpha(alpha);
        _suggestionsRenderer?.Draw(cmd);
    }

    void SetOverlayMaterial(Material mat, Vector4 bounds, Color backdrop, Color border, float borderThickness, float scanlines, float alpha)
    {
        mat.SetVector(_boundsRectId, bounds);
        mat.SetColor(_backdropColorId, backdrop);
        mat.SetColor(_borderColorId, border);
        mat.SetFloat(_borderThicknessId, borderThickness);
        mat.SetFloat(_alphaId, alpha);
        mat.SetFloat(_scanlineStrengthId, scanlines);
    }

    void BuildSuggestionSpans(IReadOnlyList<Suggestion> suggestions, int activeIdx)
    {
        _suggestionSpans.Clear();

        for (int i = 0; i < suggestions.Count; i++)
        {
            if (i > 0)
                _suggestionSpans.Add(new TextSpan(SuggInactive, "\n"));

            Suggestion s = suggestions[i];
            bool isActive = (i == activeIdx);
            Color baseColor = isActive ? SuggActive : SuggInactive;
            Color matchColor = isActive ? SuggMatchActive : SuggMatchInactive;

            string text = s.DisplayText;
            int mStart = s.MatchStart;
            int mLen = s.MatchLength;

            if (mLen <= 0 || mStart >= text.Length)
            {
                _suggestionSpans.Add(new TextSpan(baseColor, text));
                continue;
            }

            if (mStart > 0)
                _suggestionSpans.Add(new TextSpan(baseColor, text.Substring(0, mStart)));

            int end = Mathf.Min(mStart + mLen, text.Length);
            _suggestionSpans.Add(new TextSpan(matchColor, text.Substring(mStart, end - mStart)));

            if (end < text.Length)
                _suggestionSpans.Add(new TextSpan(baseColor, text.Substring(end)));
        }
    }

    public void Dispose()
    {
        if (_material != null) { Object.Destroy(_material); _material = null; }
        if (_popupMaterial != null) { Object.Destroy(_popupMaterial); _popupMaterial = null; }
        if (_highlightMaterial != null) { Object.Destroy(_highlightMaterial); _highlightMaterial = null; }
        if (_scrollbarBgMaterial != null) { Object.Destroy(_scrollbarBgMaterial); _scrollbarBgMaterial = null; }
        if (_scrollbarThumbMaterial != null) { Object.Destroy(_scrollbarThumbMaterial); _scrollbarThumbMaterial = null; }
        _inputLineRenderer?.Dispose(); _inputLineRenderer = null;
        _outputRenderer?.Dispose(); _outputRenderer = null;
        _suggestionsRenderer?.Dispose(); _suggestionsRenderer = null;
    }
}
