using UnityEngine;

/// <summary>
/// Scene host for the asset bench. It exists only because the bench needs a per-frame hotkey poll, an on-screen
/// HUD and a parent for spawned candidates; all judging logic lives in the plain-class
/// <see cref="AssetBenchService"/>.
/// </summary>
public sealed class AssetBenchHost : MonoBehaviour
{
    const float PanelWidth = 430f;
    const float Margin = 12f;

    static readonly Color CandidateColor = new(0.45f, 1f, 0.5f);
    static readonly Color ReferenceColor = new(0.75f, 0.82f, 1f);

    AssetBenchService _service;
    IConsoleService _console;

    /// <summary>
    /// Lazily created rather than built in <c>Awake</c>: <c>AddComponent</c> does not run <c>Awake</c> in edit
    /// mode, so a command issued before play would otherwise dereference a null service.
    /// </summary>
    public AssetBenchService Service => _service ??= new AssetBenchService();

    /// <summary>Last message produced by a hotkey, so the HUD/console can surface it without polling state.</summary>
    public string LastMessage { get; private set; } = "";

    public bool HudVisible = true;

    GUIStyle _panel;
    GUIStyle _text;
    GUIStyle _tag;

    /// The bench polls legacy <c>Input</c>, which keeps firing while the console owns the new Input System —
    /// so typing a command was also judging assets, and Tab was fighting console completion.
    bool ConsoleHasKeyboard()
    {
        if (_console == null) ServiceLocator.TryGet(out _console);
        return _console != null && _console.IsOpen;
    }

    void Update()
    {
        if (!Service.IsLoaded || ConsoleHasKeyboard())
            return;

        // Verdict keys are live only while a batch is loaded, so they cannot fight normal play-mode input.
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) Service.FocusPrevious();
            else Service.FocusNext();
            LastMessage = Service.StatusLine;
        }
        else if (Input.GetKeyDown(KeyCode.F1)) LastMessage = Service.SetVerdict(BenchVerdict.Keep);
        else if (Input.GetKeyDown(KeyCode.F2)) LastMessage = Service.SetVerdict(BenchVerdict.Cut);
        else if (Input.GetKeyDown(KeyCode.F3)) LastMessage = Service.SetVerdict(BenchVerdict.Later);
        else if (Input.GetKeyDown(KeyCode.F4)) HudVisible = !HudVisible;
        else if (Input.GetKeyDown(KeyCode.H)) LastMessage = Service.ToggleIsolate();
        else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            LastMessage = Service.SetZoom(Service.Zoom * 1.25f);
        else if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            LastMessage = Service.SetZoom(Service.Zoom / 1.25f);
        else PollNumberKeys();
    }

    /// Jump straight to a pair. Re-pressing the current number re-frames it, which is the way back after
    /// flying off to inspect something.
    void PollNumberKeys()
    {
        int max = Mathf.Min(Service.Count, 9);
        for (int i = 0; i < max; i++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha1 + i) && !Input.GetKeyDown(KeyCode.Keypad1 + i))
                continue;

            LastMessage = Service.FocusOneBased(i + 1);
            return;
        }
    }

    void OnGUI()
    {
        if (!HudVisible || !Service.IsLoaded)
            return;

        EnsureStyles();
        DrawWorldTags();
        DrawPanel();
    }

    /// Which object is which is the whole question a pair asks, and side-by-side props do not answer it on
    /// their own. Tag every candidate in world space — the verdict rides on the tag, so a glance across the
    /// row shows what has been dealt with and what has not.
    void DrawWorldTags()
    {
        AssetBenchService s = Service;
        Camera cam = Camera.main;
        if (cam == null) return;

        for (int i = 0; i < s.Pairs.Count; i++)
        {
            BenchPair pair = s.Pairs[i];
            if (pair.Candidate == null) continue;

            bool focused = i == s.FocusIndex;
            BenchVerdict verdict = i < s.Rows.Count ? s.Rows[i].Verdict : BenchVerdict.Unjudged;

            string label = verdict == BenchVerdict.Unjudged
                ? $"▼ {i + 1}. CANDIDATE"
                : $"▼ {i + 1}. {verdict.ToString().ToUpperInvariant()}";

            DrawTag(cam, pair.CandidateLabelPoint, label, VerdictColor(verdict), focused);

            if (focused && pair.Reference != null && pair.Reference.activeSelf)
                DrawTag(cam, pair.ReferenceLabelPoint, "▼ reference", ReferenceColor, true);
        }
    }

    static Color VerdictColor(BenchVerdict verdict) => verdict switch
    {
        BenchVerdict.Keep => new Color(0.40f, 1f, 0.45f),
        BenchVerdict.Cut => new Color(1f, 0.45f, 0.40f),
        BenchVerdict.Later => new Color(1f, 0.85f, 0.35f),
        BenchVerdict.Error => new Color(1f, 0.35f, 0.75f),
        _ => CandidateColor,
    };

    void DrawTag(Camera cam, Vector3 worldPoint, string label, Color color, bool focused)
    {
        Vector3 sp = cam.WorldToScreenPoint(worldPoint);
        if (sp.z <= 0f)
            return;

        var content = new GUIContent(label);
        Vector2 size = _tag.CalcSize(content);
        var rect = new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x, size.y);

        DrawBackground(rect, focused ? 0.72f : 0.4f);
        _tag.normal.textColor = focused ? color : new Color(color.r, color.g, color.b, 0.55f);
        GUI.Label(rect, content, _tag);
    }

    /// The style's texture is white so the alpha can be chosen per surface; tint it here rather than
    /// baking a second texture.
    void DrawBackground(Rect rect, float alpha)
    {
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.Box(rect, GUIContent.none, _panel);
        GUI.color = previous;
    }

    void DrawPanel()
    {
        AssetBenchService s = Service;
        var body = new System.Text.StringBuilder();

        int judged = s.JudgedCount;
        body.Append($"BENCH · judged {judged}/{s.Count}\n");
        body.Append(judged >= s.Count
            ? "ALL JUDGED — run  bench.report  to write it up\n\n"
            : "\n");

        for (int i = 0; i < s.Rows.Count; i++)
        {
            BenchRow r = s.Rows[i];
            bool current = i == s.FocusIndex;
            body.Append(current ? "▶ " : "   ");
            body.Append($"{i + 1}. {r.Label}  [{r.Verdict}]{(r.NeedsRework ? " [rework]" : "")}");
            if (current && !string.IsNullOrEmpty(r.Question))
                body.Append($"\n      {r.Question}");
            body.Append('\n');
        }

        body.Append("\n1-9 jump / re-frame · Tab next · Shift+Tab prev");
        body.Append($"\nH {(s.IsIsolated ? "show reference" : "hide reference")} · F4 hide HUD");
        body.Append($"\n- / = zoom out / in ({s.Zoom:F2})");
        body.Append("\nF1 keep · F2 cut · F3 later");

        var text = body.ToString();
        // Anchored right: the F6 debug overlay owns the top-left corner.
        var rect = new Rect(Screen.width - PanelWidth - Margin, Margin, PanelWidth, 0f);
        rect.height = _text.CalcHeight(new GUIContent(text), PanelWidth - 20f) + 20f;

        DrawBackground(rect, 0.8f);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 10f, PanelWidth - 20f, rect.height - 20f), text, _text);
    }

    void EnsureStyles()
    {
        if (_text != null) return;

        // GUI.skin.box tints with the scene behind it; the bench needs the text readable over any terrain.
        var bg = new Texture2D(1, 1);
        bg.SetPixel(0, 0, Color.white);
        bg.Apply();

        _panel = new GUIStyle(GUI.skin.box);
        _panel.normal.background = bg;

        _text = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft,
        };
        _text.normal.textColor = Color.white;

        _tag = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8, 8, 3, 3),
        };
    }

    void OnDestroy() => _service?.Unload();
}
