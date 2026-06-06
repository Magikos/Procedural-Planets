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

    const float TextEmSize = 0.022f;
    const float FallbackLineHeight = 1.2f;

    // Scrollbar geometry (not colors — those live on ConsoleTheme).
    const float ScrollbarWidth = 0.004f;
    const float ScrollbarPadY = 0.006f;

    public struct ConfirmRenderData
    {
        public string Question;
        public bool ActiveIsYes;
    }

    /// <summary>
    /// All state the controller hands to <see cref="Render"/> each frame. Replaces a
    /// 12-parameter call site with a single value-typed bundle.
    /// </summary>
    public struct ConsoleRenderState
    {
        public float Alpha;
        public ConsoleAnchor Anchor;
        public IList<TextSpan> InputSpans;
        public ConsoleScrollback Scrollback;
        public int ScrollOffset;
        public IReadOnlyList<Suggestion> Suggestions;
        public int ActiveSuggestion;
        public int PopupScrollOffset;
        public int PopupVisibleCount;
        public ConfirmRenderData? Confirm;
        public bool HasNewMessages;
    }

    SDFFontAsset _font;
    Material _material;
    Material _popupMaterial;
    Material _highlightMaterial;
    SDFTextRenderer _inputLineRenderer;
    SDFTextRenderer _outputRenderer;
    SDFTextRenderer _suggestionsRenderer;
    readonly List<TextSpan> _suggestionSpans = new();
    readonly List<TextSpan> _outputSpans = new();
    readonly List<TextSpan> _parseScratch = new();
    Material _scrollbarBgMaterial;
    Material _scrollbarThumbMaterial;
    int _lastScrollbackVersion = -1;
    int _lastVisibleCount = -1;
    int _lastScrollOffset = -1;
    bool _materialMissing;
    bool _ownsTheme;

    /// <summary>
    /// Color palette + styling. Loaded from <c>Resources/ConsoleTheme.asset</c> if present,
    /// otherwise <see cref="ConsoleTheme.CreateDefault"/> provides an in-memory default.
    /// </summary>
    public ConsoleTheme Theme { get; private set; }

    public ConsoleRenderer()
    {
        Theme = Resources.Load<ConsoleTheme>("ConsoleTheme");
        if (Theme == null)
        {
            Theme = ConsoleTheme.CreateDefault();
            Theme.hideFlags = HideFlags.HideAndDontSave;
            _ownsTheme = true;
        }

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

    public void Render(CommandBuffer cmd, in ConsoleRenderState state)
    {
        if (_material == null || _materialMissing)
            return;

        Vector4 bounds = state.Anchor.GetBoundsRect();

        // Pulse the border to amber while the user is scrolled back and new messages exist.
        Color activeBorderColor = state.HasNewMessages
            ? Color.Lerp(Theme.Border, Theme.NewMessageBorderPulse,
                (Mathf.Sin(Time.unscaledTime * Mathf.PI * 2.5f) + 1f) * 0.5f)
            : Theme.Border;

        _material.SetVector(_boundsRectId, bounds);
        _material.SetColor(_backdropColorId, Theme.Backdrop);
        _material.SetColor(_borderColorId, activeBorderColor);
        _material.SetFloat(_borderThicknessId, Theme.BorderThickness);
        _material.SetFloat(_alphaId, state.Alpha);
        _material.SetFloat(_scanlineStrengthId, Theme.ScanlineStrength);

        cmd.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);

        Vector2 inputOrigin = state.Anchor.GetInputLineOrigin();
        float lineHeightUnits = (_font != null ? _font.LineHeight : FallbackLineHeight) * TextEmSize;

        // Top edge of the output area, just below the backdrop top edge so glyph caps don't kiss the border.
        float outputTopY = bounds.w - TextEmSize * 0.6f;

        // Max lines that fit between the input line and the output area's top edge.
        float available = outputTopY - inputOrigin.y - lineHeightUnits;
        int maxLines = Mathf.Max(1, Mathf.FloorToInt(available / lineHeightUnits) + 1);

        // Clip rect for text — anything outside the backdrop is discarded by the shader.
        _inputLineRenderer?.SetClipRect(bounds);
        _outputRenderer?.SetClipRect(bounds);

        if (_outputRenderer != null && state.Scrollback != null && state.Scrollback.Count > 0)
        {
            int visibleCount = Mathf.Min(maxLines, state.Scrollback.Count);

            if (state.Scrollback.Version != _lastScrollbackVersion
                || visibleCount != _lastVisibleCount
                || state.ScrollOffset != _lastScrollOffset)
            {
                var window = state.Scrollback.GetWindow(visibleCount, state.ScrollOffset);
                BuildOutputSpans(window);
                float originY = inputOrigin.y + lineHeightUnits * window.Count;
                _outputRenderer.SetSpans(_outputSpans, inputOrigin.x, originY, TextEmSize);
                _lastScrollbackVersion = state.Scrollback.Version;
                _lastVisibleCount = visibleCount;
                _lastScrollOffset = state.ScrollOffset;
            }

            _outputRenderer.SetAlpha(state.Alpha);
            _outputRenderer.Draw(cmd);

            // Scrollbar — only when there is more history than fits.
            if (state.Scrollback.Count > maxLines)
                DrawScrollbar(cmd, state.Alpha, bounds, inputOrigin.y, outputTopY,
                    state.Scrollback.Count, maxLines, state.ScrollOffset);
        }

        if (_inputLineRenderer != null && state.InputSpans != null && state.InputSpans.Count > 0)
        {
            _inputLineRenderer.SetSpans(state.InputSpans, inputOrigin.x, inputOrigin.y, TextEmSize);
            _inputLineRenderer.SetAlpha(state.Alpha);
            _inputLineRenderer.Draw(cmd);
        }

        // ---- Confirm modal takes precedence over suggestions popup -----
        if (state.Confirm.HasValue)
            DrawConfirmModal(cmd, state.Alpha, state.Anchor, inputOrigin, lineHeightUnits, state.Confirm.Value);
        else if (state.Suggestions != null && state.Suggestions.Count > 0)
            DrawSuggestionsPopup(cmd, state.Alpha, state.Anchor, inputOrigin, lineHeightUnits,
                state.Suggestions, state.ActiveSuggestion, state.PopupScrollOffset, state.PopupVisibleCount);
    }

    // Shared geometry / padding for popup frames (confirm modal + suggestions popup).
    const float PopupPadY = 0.008f;
    const float PopupPadX = 0.018f;
    const float PopupTextPadX = 0.008f;

    /// <summary>
    /// Draw a popup backdrop rectangle anchored above the input line. Returns the popup's
    /// world-space bounds so callers can place text + clip rect inside it.
    /// </summary>
    Vector4 DrawPopupFrame(
        CommandBuffer cmd, float alpha, ConsoleAnchor anchor,
        Vector2 inputOrigin, float lineH, int rowCount, Color backdrop)
    {
        Vector4 consoleBounds = anchor.GetBoundsRect();
        float popupBottom = inputOrigin.y + lineH * 0.4f;
        float popupTop = popupBottom + rowCount * lineH + PopupPadY * 2;
        var popupBounds = new Vector4(consoleBounds.x + PopupPadX, popupBottom, consoleBounds.z - PopupPadX, popupTop);

        SetOverlayMaterial(_popupMaterial, popupBounds, backdrop, Theme.Border, Theme.BorderThickness, 0f, alpha);
        cmd.DrawProcedural(Matrix4x4.identity, _popupMaterial, 0, MeshTopology.Triangles, 3);
        return popupBounds;
    }

    void DrawConfirmModal(
        CommandBuffer cmd,
        float alpha,
        ConsoleAnchor anchor,
        Vector2 inputOrigin,
        float lineH,
        ConfirmRenderData data)
    {
        Vector4 popupBounds = DrawPopupFrame(cmd, alpha, anchor, inputOrigin, lineH, rowCount: 1, Theme.ConfirmBackdrop);

        float textY = popupBounds.w - PopupPadY - TextEmSize * 0.6f;

        _suggestionSpans.Clear();
        _suggestionSpans.Add(new TextSpan(Theme.ConfirmQuestion, data.Question));
        _suggestionSpans.Add(new TextSpan(Theme.ConfirmInactive, "   "));
        _suggestionSpans.Add(new TextSpan(
            data.ActiveIsYes ? Theme.ConfirmActive : Theme.ConfirmInactive,
            data.ActiveIsYes ? "[*] Yes" : "[ ] Yes"));
        _suggestionSpans.Add(new TextSpan(Theme.ConfirmInactive, "   "));
        _suggestionSpans.Add(new TextSpan(
            !data.ActiveIsYes ? Theme.ConfirmActive : Theme.ConfirmInactive,
            !data.ActiveIsYes ? "[*] No" : "[ ] No"));

        _suggestionsRenderer?.SetSpans(_suggestionSpans, popupBounds.x + PopupTextPadX, textY, TextEmSize);
        _suggestionsRenderer?.SetClipRect(popupBounds);
        _suggestionsRenderer?.SetAlpha(alpha);
        _suggestionsRenderer?.Draw(cmd);
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
        // Main scrollback bar: offset 0 = live tail at bottom, so the thumb rises as offset grows (thumbAtTopWhenMax: false).
        float barRight = consoleBounds.z - ScrollbarWidth * 0.5f;
        float barLeft = consoleBounds.z - ScrollbarWidth * 1.5f;
        float barBottom = outputBottomY + ScrollbarPadY;
        float barTop = outputTopY - ScrollbarPadY;
        DrawVerticalScrollbar(cmd, alpha, barLeft, barRight, barBottom, barTop,
            totalLines, visibleLines, scrollOffset, thumbAtTopWhenMax: false);
    }

    void BuildOutputSpans(IReadOnlyList<ConsoleMessage> lines)
    {
        _outputSpans.Clear();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) _outputSpans.Add(new TextSpan(Theme.MsgNormal, "\n"));
            Color defaultColor = lines[i].Type switch
            {
                ConsoleMessageType.Input => Theme.MsgInput,
                ConsoleMessageType.Output => Theme.MsgOutput,
                ConsoleMessageType.Warning => Theme.MsgWarning,
                ConsoleMessageType.Error => Theme.MsgError,
                ConsoleMessageType.Exception => Theme.MsgException,
                ConsoleMessageType.Log => Theme.MsgLog,
                _ => Theme.MsgNormal,
            };

            // Parse inline <color=...> markup if present, using the type colour as the default.
            // Tagless lines emit exactly one span (parser early-exits on the inner StringBuilder
            // flush), so this stays cheap for the common case.
            _parseScratch.Clear();
            ConsoleColorTagParser.Parse(lines[i].Text, defaultColor, _parseScratch);
            for (int s = 0; s < _parseScratch.Count; s++)
                _outputSpans.Add(_parseScratch[s]);
        }
    }

    void DrawSuggestionsPopup(
        CommandBuffer cmd,
        float alpha,
        ConsoleAnchor anchor,
        Vector2 inputOrigin,
        float lineH,
        IReadOnlyList<Suggestion> suggestions,
        int activeIdx,
        int scrollOffset,
        int visibleSlots)
    {
        const float PopupScrollbarWidth = 0.003f;

        // Fixed-size popup window — visibleCount rows tall.
        int visibleCount = Mathf.Min(visibleSlots, suggestions.Count - scrollOffset);
        if (visibleCount <= 0) return;

        Vector4 popupBounds = DrawPopupFrame(cmd, alpha, anchor, inputOrigin, lineH, visibleCount, Theme.PopupBackdrop);

        // Text baseline for the first row, then per-row below.
        float textY = popupBounds.w - PopupPadY - TextEmSize * 0.6f;

        // Active row highlight — relative to the visible window.
        int activeRowInPopup = Mathf.Clamp(activeIdx - scrollOffset, 0, visibleCount - 1);
        float lineBaseY = textY - activeRowInPopup * lineH;
        float rowMidY = lineBaseY + TextEmSize * 0.2f;
        var rowBounds = new Vector4(popupBounds.x, rowMidY - lineH * 0.5f, popupBounds.z, rowMidY + lineH * 0.5f);
        SetOverlayMaterial(_highlightMaterial, rowBounds, Theme.SuggHighlightBackdrop, Theme.SuggHighlightBackdrop, 0f, 0f, alpha);
        cmd.DrawProcedural(Matrix4x4.identity, _highlightMaterial, 0, MeshTopology.Triangles, 3);

        // Build and draw the visible window of suggestions.
        BuildSuggestionSpans(suggestions, activeIdx, scrollOffset, visibleCount);
        _suggestionsRenderer?.SetSpans(_suggestionSpans, popupBounds.x + PopupTextPadX, textY, TextEmSize);
        _suggestionsRenderer?.SetClipRect(popupBounds);
        _suggestionsRenderer?.SetAlpha(alpha);
        _suggestionsRenderer?.Draw(cmd);

        // Scroll indicator when the full list is taller than the window.
        if (suggestions.Count > visibleSlots)
        {
            DrawPopupScrollIndicator(cmd, alpha, popupBounds,
                suggestions.Count, visibleSlots, scrollOffset, PopupScrollbarWidth);
        }
    }

    void DrawPopupScrollIndicator(
        CommandBuffer cmd,
        float alpha,
        Vector4 popupBounds,
        int total,
        int visible,
        int scrollOffset,
        float scrollbarWidth)
    {
        // Popup bar: offset 0 = first row at the top, so the thumb starts at top and descends as offset grows (thumbAtTopWhenMax: true).
        const float Pad = 0.004f;
        float barRight = popupBounds.z - 0.002f;
        float barLeft = barRight - scrollbarWidth;
        float barBottom = popupBounds.y + Pad;
        float barTop = popupBounds.w - Pad;
        DrawVerticalScrollbar(cmd, alpha, barLeft, barRight, barBottom, barTop,
            total, visible, scrollOffset, thumbAtTopWhenMax: true);
    }

    /// <summary>
    /// Draw a vertical scrollbar (background track + proportional thumb). When
    /// <paramref name="thumbAtTopWhenMax"/> is false (main scrollback), thumb sits at the
    /// bottom when scrollOffset is 0 and rises with offset. When true (popup), thumb sits
    /// at the top when scrollOffset is 0 and descends with offset.
    /// </summary>
    void DrawVerticalScrollbar(
        CommandBuffer cmd, float alpha,
        float barLeft, float barRight, float barBottom, float barTop,
        int total, int visible, int scrollOffset, bool thumbAtTopWhenMax)
    {
        float barHeight = barTop - barBottom;
        if (barHeight <= 0f) return;

        SetOverlayMaterial(_scrollbarBgMaterial,
            new Vector4(barLeft, barBottom, barRight, barTop),
            Theme.ScrollbarBg, Theme.ScrollbarBg, 0f, 0f, alpha);
        cmd.DrawProcedural(Matrix4x4.identity, _scrollbarBgMaterial, 0, MeshTopology.Triangles, 3);

        float thumbFraction = Mathf.Clamp01((float)visible / total);
        float thumbHeight = barHeight * thumbFraction;
        int maxOffset = total - visible;
        float thumbT = maxOffset > 0 ? Mathf.Clamp01((float)scrollOffset / maxOffset) : 0f;

        float thumbBottom, thumbTop;
        if (thumbAtTopWhenMax)
        {
            thumbTop = barTop - thumbT * (barHeight - thumbHeight);
            thumbBottom = thumbTop - thumbHeight;
        }
        else
        {
            thumbBottom = barBottom + thumbT * (barHeight - thumbHeight);
            thumbTop = thumbBottom + thumbHeight;
        }

        SetOverlayMaterial(_scrollbarThumbMaterial,
            new Vector4(barLeft, thumbBottom, barRight, thumbTop),
            Theme.ScrollbarThumb, Theme.ScrollbarThumb, 0f, 0f, alpha);
        cmd.DrawProcedural(Matrix4x4.identity, _scrollbarThumbMaterial, 0, MeshTopology.Triangles, 3);
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

    void BuildSuggestionSpans(IReadOnlyList<Suggestion> suggestions, int activeIdx, int scrollOffset, int count)
    {
        _suggestionSpans.Clear();

        for (int j = 0; j < count; j++)
        {
            int i = scrollOffset + j;
            if (i < 0 || i >= suggestions.Count) continue;
            if (j > 0)
                _suggestionSpans.Add(new TextSpan(Theme.SuggInactive, "\n"));

            Suggestion s = suggestions[i];
            bool isActive = (i == activeIdx);
            Color baseColor = isActive ? Theme.SuggActive : Theme.SuggInactive;
            Color matchColor = isActive ? Theme.SuggMatchActive : Theme.SuggMatchInactive;

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
        if (_ownsTheme && Theme != null) Object.Destroy(Theme);
        Theme = null;
        _ownsTheme = false;
    }
}
