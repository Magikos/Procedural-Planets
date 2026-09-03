using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

// Measures every prototype's whole LOD ladder against its LOD0 mesh, instead of asking someone to eyeball
// 176 of them. LOD0 is the source of truth: every coarser tier and the card are scored on how far they
// drift from it at the screen size where the swap really happens.
//
// Coverage is extracted exactly rather than keyed off brightness: each tier is rendered twice, against a
// black backdrop and a white one, and alpha falls out as 1 - (white - black). That survives alpha-blended
// card edges, dark trunks and dark leaves, all of which defeat a "not the background" test.
//
// The backdrop is a physical unlit quad, not the camera clear colour. This project's URP setup clears to
// black whatever Camera.backgroundColor says, so a clear-colour probe reports every pixel as covered.
[RequireComponent(typeof(ScatterLodCompare))]
public sealed class ScatterLodSweep : MonoBehaviour
{
    public string OutputFolder = "Assets/Screenshots/LOD";
    public float HandoverPixels = 36f;
    public bool SaveImages = true;

    const int RtSize = 384;
    const int Crop = 128;
    const int Zoom = 3;

    ScatterLodCompare _bench;
    bool _running;

    void Awake() => _bench = GetComponent<ScatterLodCompare>();

    void Update()
    {
        if (_running) return;
        Keyboard k = Keyboard.current;
        if (k != null && k.fKey.wasPressedThisFrame) Run();
    }

    public void Run()
    {
        if (_running) return;
        _running = true;
        RunSweep();
    }

    sealed class Shot
    {
        public string Tier;
        public bool Drawn;
        public float[] Alpha;
        public Color[] Colour;
        public Color32[] Raw;
        public float Coverage, Luminance, Iou, RgbDist;
        public float CovRatio, LumRatio;
        public string Flags = "";
    }

    sealed class Row
    {
        public string Name, Key;
        public float SizeM, MeshCull, CardStart, CardEnd;
        public bool HasCard, CardValid;
        public int LastMeshLod, MeshLodCount, DeadLods;
        public List<Shot> Shots = new();
        public string Flags;
        public float Score;
    }

    async Awaitable RunSweep()
    {
        Camera cam = _bench.BenchCamera;
        if (cam == null) { Debug.LogError("[LodSweep] no camera"); _running = false; return; }

        _bench.SweepActive = true;
        RenderTexture prevTarget = cam.targetTexture;
        CameraClearFlags prevClear = cam.clearFlags;
        Color prevBg = cam.backgroundColor;

        var rt = new RenderTexture(RtSize, RtSize, 24) { filterMode = FilterMode.Point };
        cam.targetTexture = rt;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.aspect = 1f;

        // Fog would tint the backdrop toward the fog colour and break the white pass.
        bool prevFog = RenderSettings.fog;
        RenderSettings.fog = false;

        Directory.CreateDirectory(OutputFolder);
        var rows = new List<Row>();
        var black = new Texture2D(Crop, Crop, TextureFormat.RGBA32, false);
        var white = new Texture2D(Crop, Crop, TextureFormat.RGBA32, false);

        try
        {
            for (int i = 0; i < _bench.ProtoCount; i++)
            {
                ScatterPrototypeDto proto = _bench.ProtoAt(i);
                int cardTier = _bench.CardTierOf(proto);
                int lastMesh = _bench.LastDrawnMeshLod(i);
                bool cardValid = proto.HasImpostor && _bench.ImpostorOf(i).Valid;

                var row = new Row
                {
                    Name = proto.DisplayName,
                    Key = proto.SpeciesKey ?? "-",
                    SizeM = proto.BoundsSizeMeters,
                    MeshCull = proto.MeshCullDistance,
                    CardStart = proto.HasImpostor ? proto.ImpostorStartDistance : 0f,
                    CardEnd = proto.HasImpostor ? proto.ImpostorEndDistance : 0f,
                    HasCard = proto.HasImpostor,
                    CardValid = cardValid,
                    LastMeshLod = lastMesh,
                    MeshLodCount = cardTier,
                    DeadLods = Mathf.Max(0, cardTier - 1 - lastMesh),
                };

                for (int tier = 0; tier < cardTier; tier++)
                {
                    Shot s = await CaptureTier(cam, rt, black, white, i, tier);
                    s.Tier = $"LOD{tier}";
                    s.Drawn = _bench.TierDraws(i, tier);
                    row.Shots.Add(s);
                }
                if (cardValid)
                {
                    Shot s = await CaptureTier(cam, rt, black, white, i, cardTier);
                    s.Tier = "CARD";
                    s.Drawn = true;
                    row.Shots.Add(s);
                }

                ScoreRow(row);
                rows.Add(row);
                if (SaveImages) SaveStrip(i, row);
                foreach (Shot s in row.Shots) { s.Alpha = null; s.Colour = null; s.Raw = null; }
            }
        }
        finally
        {
            cam.targetTexture = prevTarget;
            cam.clearFlags = prevClear;
            cam.backgroundColor = prevBg;
            cam.ResetAspect();
            RenderSettings.fog = prevFog;
            rt.Release();
            Destroy(rt);
            Destroy(black);
            Destroy(white);
            if (_backdrop != null) { Destroy(_backdrop); _backdrop = null; }
            if (_backdropMesh != null) { Destroy(_backdropMesh); _backdropMesh = null; }
            if (_backdropMat != null) { Destroy(_backdropMat); _backdropMat = null; }
            _bench.SweepActive = false;
            _running = false;
        }

        // No pixel ever came out brighter on the white pass, so nothing separated prop from background and
        // every coverage number below is 1.0 by construction.
        if (_backdropSeen == 0)
        {
            Debug.LogError("[LodSweep] backdrop never rendered - coverage, IoU and silhouette scores are invalid");
            return;
        }

        WriteCsv(rows);
        WriteSummary(rows);
        Debug.Log($"[LodSweep] {rows.Count} prototypes -> {OutputFolder}");
    }

    async Awaitable<Shot> CaptureTier(Camera cam, RenderTexture rt, Texture2D black, Texture2D white,
                                      int protoIndex, int tier)
    {
        await RenderOnce(cam, rt, black, protoIndex, tier, Color.black);
        await RenderOnce(cam, rt, white, protoIndex, tier, Color.white);

        Color32[] b = black.GetPixels32();
        Color32[] w = white.GetPixels32();
        var shot = new Shot { Alpha = new float[b.Length], Colour = new Color[b.Length], Raw = b };
        for (int p = 0; p < b.Length; p++)
        {
            float bl = (b[p].r + b[p].g + b[p].b) / 765f;
            float wh = (w[p].r + w[p].g + w[p].b) / 765f;
            if (wh - bl > 0.02f) _backdropSeen++;
            shot.Alpha[p] = Mathf.Clamp01(1f - (wh - bl));
            shot.Colour[p] = new Color(b[p].r / 255f, b[p].g / 255f, b[p].b / 255f);
        }
        shot.Coverage = Coverage(shot.Alpha);
        shot.Luminance = MeanLuminance(shot.Alpha, shot.Colour);
        return shot;
    }

    async Awaitable RenderOnce(Camera cam, RenderTexture rt, Texture2D dst,
                               int protoIndex, int tier, Color bg)
    {
        await Awaitable.NextFrameAsync();
        _bench.PlaceCamera(protoIndex, HandoverPixels, RtSize, 0f);
        DrawBackdrop(cam, bg);
        _bench.DrawTierFor(protoIndex, tier);
        await Awaitable.EndOfFrameAsync();

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        int o = (RtSize - Crop) / 2;
        dst.ReadPixels(new Rect(o, o, Crop, Crop), 0, 0);
        dst.Apply(false);
        RenderTexture.active = prev;
    }

    static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int _colorId = Shader.PropertyToID("_Color");
    int _backdropSeen;
    GameObject _backdrop;
    Mesh _backdropMesh;
    Material _backdropMat;

    // A flat unlit wall behind the prop, well outside its bounds, so the "white" pass is made of geometry
    // rather than a clear colour URP overrides. A real MeshRenderer, not Graphics.DrawMesh - the immediate
    // API draws nothing on this camera. Wound both ways because it is oriented with the camera, not at it.
    void DrawBackdrop(Camera cam, Color colour)
    {
        if (_backdrop == null)
        {
            var mesh = new Mesh { name = "LodSweepBackdrop", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f),
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1, 0, 1, 2, 2, 1, 3 };
            mesh.RecalculateBounds();
            _backdropMesh = mesh;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _backdropMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            _backdrop = new GameObject("LodSweepBackdrop") { hideFlags = HideFlags.HideAndDontSave };
            _backdrop.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = _backdrop.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _backdropMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        _backdropMat.SetColor(_baseColorId, colour);
        _backdropMat.SetColor(_colorId, colour);

        Transform t = cam.transform;
        float dist = Mathf.Min(t.position.magnitude * 3f + 50f, cam.farClipPlane * 0.8f);
        float span = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * dist * 2.5f;
        _backdrop.transform.SetPositionAndRotation(t.position + t.forward * dist, t.rotation);
        _backdrop.transform.localScale = new Vector3(span, span, 1f);
    }

    // Every tier is scored against LOD0, the model itself. A tier that no longer reads as the same object at
    // handover size is the pop, whether it is a coarse mesh or the card.
    static void ScoreRow(Row row)
    {
        if (row.Shots.Count == 0) { row.Flags = "MESH_EMPTY"; row.Score = 99f; return; }
        Shot reference = row.Shots[0];
        float[] refSoft = Soften(reference.Alpha);

        foreach (Shot s in row.Shots)
        {
            s.CovRatio = reference.Coverage > 0.0001f ? s.Coverage / reference.Coverage : 0f;
            s.LumRatio = reference.Luminance > 0.0001f ? s.Luminance / reference.Luminance : 0f;

            int an = 0, bn = 0;
            var aSum = Vector3.zero;
            var bSum = Vector3.zero;
            for (int p = 0; p < reference.Alpha.Length; p++)
            {
                if (reference.Alpha[p] > 0.5f) { aSum += ToVec(reference.Colour[p]); an++; }
                if (s.Alpha[p] > 0.5f) { bSum += ToVec(s.Colour[p]); bn++; }
            }

            float[] soft = Soften(s.Alpha);
            float lo = 0f, hi = 0f;
            for (int p = 0; p < soft.Length; p++)
            {
                lo += Mathf.Min(refSoft[p], soft[p]);
                hi += Mathf.Max(refSoft[p], soft[p]);
            }
            s.Iou = hi > 0f ? lo / hi : 0f;
            s.RgbDist = an > 0 && bn > 0 ? Vector3.Distance(aSum / an, bSum / bn) : 0f;
            s.Flags = ClassifyShot(s);
        }

        var flags = new List<string>();
        if (!row.HasCard) flags.Add("NO_CARD");
        else if (!row.CardValid) flags.Add("CARD_BUILD_FAILED");
        if (reference.Coverage < 0.002f) flags.Add("MESH_EMPTY");
        if (row.DeadLods > 0) flags.Add($"DEAD_LODS({row.DeadLods})");

        float worst = 0f;
        foreach (Shot s in row.Shots)
        {
            if (s.Tier == "LOD0") continue;
            float sc = ShotScore(s);
            if (sc > worst) worst = sc;
            if (s.Flags != "ok") flags.Add($"{s.Tier}:{s.Flags}");
        }
        if (!row.HasCard || !row.CardValid) worst = Mathf.Max(worst, 3f);
        row.Score = worst;
        row.Flags = flags.Count == 0 ? "ok" : string.Join(" ", flags);
    }

    static Vector3 ToVec(Color c) => new Vector3(c.r, c.g, c.b);

    // Thresholds are deliberately loose. A coarse tier only has to survive being seen at handover size, and
    // a card is a flat quad - it can never reproduce a mesh exactly. These catch tiers that read as a
    // different object.
    static string ClassifyShot(Shot s)
    {
        var f = new List<string>();
        if (s.Coverage < 0.002f) f.Add("EMPTY");
        else
        {
            if (s.CovRatio < 0.70f) f.Add($"THIN({s.CovRatio:0.00})");
            if (s.CovRatio > 1.35f) f.Add($"FAT({s.CovRatio:0.00})");
            if (s.Iou < 0.55f) f.Add($"SILHOUETTE({s.Iou:0.00})");
            if (s.LumRatio < 0.75f) f.Add($"DARK({s.LumRatio:0.00})");
            if (s.LumRatio > 1.30f) f.Add($"BRIGHT({s.LumRatio:0.00})");
            if (s.RgbDist > 0.10f) f.Add($"COLOUR({s.RgbDist:0.000})");
        }
        return f.Count == 0 ? "ok" : string.Join(",", f);
    }

    static float ShotScore(Shot s)
    {
        if (s.Coverage < 0.002f) return 90f;
        float cov = Mathf.Abs(Mathf.Log(Mathf.Max(s.CovRatio, 0.01f)));
        float lum = Mathf.Abs(Mathf.Log(Mathf.Max(s.LumRatio, 0.01f)));
        return cov + (1f - s.Iou) + lum + s.RgbDist * 4f;
    }

    // At 36 px a prop is read as a blob, not as individual leaves. Scoring 4x4-averaged masks asks whether
    // the shape still reads the same, instead of punishing every leaf that moved one pixel.
    static float[] Soften(float[] a)
    {
        const int n = Crop / 4;
        var o = new float[n * n];
        for (int y = 0; y < Crop; y++)
            for (int x = 0; x < Crop; x++)
                o[y / 4 * n + x / 4] += a[y * Crop + x] * (1f / 16f);
        return o;
    }

    static float Coverage(float[] a)
    {
        float s = 0f;
        for (int p = 0; p < a.Length; p++) s += a[p];
        return s / a.Length;
    }

    static float MeanLuminance(float[] a, Color[] c)
    {
        float sum = 0f, n = 0f;
        for (int p = 0; p < a.Length; p++)
        {
            if (a[p] <= 0.5f) continue;
            sum += 0.2126f * c[p].r + 0.7152f * c[p].g + 0.0722f * c[p].b;
            n++;
        }
        return n > 0f ? sum / n : 0f;
    }

    void SaveStrip(int index, Row row)
    {
        int panel = Crop * Zoom;
        int gap = 6;
        int n = row.Shots.Count;
        if (n == 0) return;
        int w = panel * n + gap * (n - 1);
        var px = new Color32[w * panel];
        var divider = new Color32(40, 40, 40, 255);
        for (int i = 0; i < px.Length; i++) px[i] = divider;
        for (int i = 0; i < n; i++) Blit(px, w, row.Shots[i].Raw, i * (panel + gap));

        var outTex = new Texture2D(w, panel, TextureFormat.RGB24, false);
        outTex.SetPixels32(px);
        outTex.Apply(false);
        File.WriteAllBytes(Path.Combine(OutputFolder, $"{index:000}_{MakeSafe(row.Name)}.png"),
                           ImageConversion.EncodeToPNG(outTex));
        Destroy(outTex);
    }

    static void Blit(Color32[] dst, int dstWidth, Color32[] src, int xOffset)
    {
        for (int y = 0; y < Crop * Zoom; y++)
        {
            int sy = y / Zoom;
            for (int x = 0; x < Crop * Zoom; x++)
                dst[y * dstWidth + xOffset + x] = src[sy * Crop + x / Zoom];
        }
    }

    static string MakeSafe(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        return sb.ToString();
    }

    void WriteCsv(List<Row> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("index,name,key,sizeM,meshCull,cardStart,cardEnd,hasCard,cardValid,lastMeshLod,meshLodCount,deadLods,tier,drawnInGame,coverage,covRatioVsLod0,iouVsLod0,luminance,lumRatioVsLod0,rgbDistVsLod0,tierFlags");
        for (int i = 0; i < rows.Count; i++)
        {
            Row r = rows[i];
            foreach (Shot s in r.Shots)
                sb.AppendLine($"{i},{Csv(r.Name)},{Csv(r.Key)},{r.SizeM:0.00},{r.MeshCull:0},{r.CardStart:0},{r.CardEnd:0}," +
                              $"{r.HasCard},{r.CardValid},{r.LastMeshLod},{r.MeshLodCount},{r.DeadLods}," +
                              $"{s.Tier},{s.Drawn},{s.Coverage:0.0000},{s.CovRatio:0.000},{s.Iou:0.000}," +
                              $"{s.Luminance:0.0000},{s.LumRatio:0.000},{s.RgbDist:0.0000},{Csv(s.Flags)}");
        }
        File.WriteAllText(Path.Combine(OutputFolder, "lod_sweep.csv"), sb.ToString());
    }

    static string Csv(string s) => s != null && s.Contains(",") ? $"\"{s}\"" : s;

    void WriteSummary(List<Row> rows)
    {
        var counts = new Dictionary<string, int>();
        foreach (Row r in rows)
            foreach (string f in r.Flags.Split(' '))
            {
                string key = f.Contains("(") ? f.Substring(0, f.IndexOf('(')) : f;
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }

        var sb = new StringBuilder();
        sb.AppendLine($"LOD ladder sweep - {rows.Count} prototypes, every tier scored against its own LOD0 at {HandoverPixels:0} px");
        sb.AppendLine();
        sb.AppendLine("flag counts:");
        var keys = new List<string>(counts.Keys);
        keys.Sort();
        foreach (string k in keys) sb.AppendLine($"  {k,-22} {counts[k]}");

        rows.Sort((a, b) => b.Score.CompareTo(a.Score));
        sb.AppendLine();
        sb.AppendLine("worst first:");
        foreach (Row r in rows)
        {
            if (r.Flags == "ok") continue;
            sb.AppendLine($"{r.Score,6:0.00}  {r.Name} | {r.Key} | size {r.SizeM:0.0} m | cull {r.MeshCull:0} m | {r.Flags}");
            foreach (Shot s in r.Shots)
                sb.AppendLine($"          {s.Tier,-6} drawn={s.Drawn,-5} cov {s.CovRatio,5:0.00}  iou {s.Iou,5:0.00}  lum {s.LumRatio,5:0.00}  rgb {s.RgbDist,6:0.000}  {s.Flags}");
        }
        File.WriteAllText(Path.Combine(OutputFolder, "lod_sweep_summary.txt"), sb.ToString());
    }
}
