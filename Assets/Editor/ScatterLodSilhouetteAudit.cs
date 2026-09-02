using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Measures every prototype's LOD ladder as a pure silhouette and reports the tiers that change shape.
//
// An LOD tier is meant to hold the same outline at lower cost, so the defect this finds is a tier whose
// covered area jumps: a leafless canopy, a solid blob standing in for an open flower clump, a mesh the
// alpha test erases. Coverage is read with _LodDebugTint forced to opaque white, so every pixel that
// survives the alpha clip and the dither counts and nothing else does — the measurement is independent
// of sun direction, time of day, exposure and fog, which is what makes it comparable between prototypes.
// Three elevations because a flat prop and a tall prop disagree most from above.
public static class ScatterLodSilhouetteAudit
{
    const string LibraryPath = "Assets/Resources/Settings/ScatterLibrary.asset";
    const int Res = 512;
    const float Fov = 30f;
    // On-screen size the prototype is framed to. Deliberately large: at tree-line sizes (12-40 px) pixel
    // quantization fattens a sparse alpha prop far more than a dense one, so the ratios stop describing the
    // shapes and start describing the rasterizer. A reed tuft measured 8x its own area that way. Shape is
    // compared near mip 0; the distance behaviour is a separate question the mip-relax settings answer.
    const int TargetPx = 200;
    const int AuditLayer = 31;

    // A tier that changes covered area by more than this against LOD0 reads as a pop rather than a
    // cheaper version of the same shape.
    const float LodDeviation = 1.35f;
    // The card replaces the last mesh LOD outright, so it is allowed a wider band: an octahedral bake can
    // never match a mesh exactly, and a card slightly fatter than the mesh is the safe direction.
    const float CardThin = 0.7f;
    const float CardFat = 1.6f;

    static readonly float[] Elevations = { 0f, 30f, 70f };

    [MenuItem("Tools/ProceduralPlanets/Impostors/Audit LOD Silhouettes", false, 23)]
    public static void Audit()
    {
        var lib = AssetDatabase.LoadAssetAtPath<ScatterLibrary>(LibraryPath);
        if (lib == null) { Debug.LogError($"[LOD audit] no library at {LibraryPath}"); return; }
        ScatterLibraryDto dto = ScatterLibraryDto.From(lib);

        var rig = new AuditRig();
        var report = new StringBuilder();
        int flagged = 0;
        try
        {
            for (int pi = 0; pi < dto.Prototypes.Length; pi++)
            {
                ScatterPrototypeDto proto = dto.Prototypes[pi];
                if (!proto.CanRender) continue;
                EditorUtility.DisplayProgressBar("Auditing LOD silhouettes", proto.DisplayName,
                    (float)pi / dto.Prototypes.Length);
                if (Measure(rig, proto, pi, report)) flagged++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            rig.Dispose();
        }

        if (flagged == 0) Debug.Log("[LOD audit] every LOD tier and impostor card holds its silhouette.");
        else Debug.LogWarning($"[LOD audit] {flagged} prototype(s) change shape between tiers:\n{report}");
    }

    static bool Measure(AuditRig rig, ScatterPrototypeDto proto, int index, StringBuilder report)
    {
        Bounds bounds = default;
        bool first = true;
        int lodCount = 0;
        foreach (ScatterPartDto part in proto.Parts)
        {
            if (!part.CanRender) continue;
            Bounds b = part.LodMeshes[0].bounds;
            if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
            lodCount = Mathf.Max(lodCount, part.LodMeshes.Length);
        }
        if (first) return false;

        // Framed on the largest extent, matching how the impostor baker sizes its square card — framing on
        // height alone puts a wide flat prop off-screen and reads as a false mismatch.
        float extent = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.z), Mathf.Max(bounds.size.y, 1e-3f));
        float distance = Res * extent / (2f * TargetPx * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad));
        Vector3 target = rig.Origin + bounds.center;

        var coverage = new float[lodCount][];
        for (int lod = 0; lod < lodCount; lod++)
        {
            rig.ShowMeshLod(proto, lod);
            coverage[lod] = rig.CoverAtElevations(target, distance, Elevations, TargetPx);
        }
        rig.ClearProps();

        float[] cardCover = null;
        ScatterLodBatcher.Impostor impostor = ScatterImpostorFactory.TryBuild(proto, bounds);
        if (impostor.Valid)
        {
            rig.ShowCard(impostor);
            cardCover = rig.CoverAtElevations(target, distance, Elevations, TargetPx);
            rig.ClearProps();
            Object.DestroyImmediate(impostor.Params.material);
        }

        var problems = new List<string>();
        for (int lod = 1; lod < lodCount; lod++)
        {
            float worst = WorstRatio(coverage[0], coverage[lod]);
            if (worst > LodDeviation || worst < 1f / LodDeviation)
                problems.Add($"LOD{lod} is {worst:0.00}x LOD0");
        }
        if (cardCover != null)
        {
            float worst = WorstRatio(coverage[lodCount - 1], cardCover);
            if (worst > CardFat || worst < CardThin)
                problems.Add($"card is {worst:0.00}x LOD{lodCount - 1}");
        }
        if (problems.Count == 0) return false;

        report.Append($"  [{index}] {proto.DisplayName}: {string.Join(", ", problems)}  |");
        for (int lod = 0; lod < lodCount; lod++) report.Append($" L{lod}[{Join(coverage[lod])}]");
        if (cardCover != null) report.Append($" card[{Join(cardCover)}]");
        report.AppendLine();
        return true;
    }

    // The ratio furthest from 1 across the sampled angles, expressed as a multiplier of the reference.
    // Angles where the prop has nearly edged out of view are dropped: a card always faces the camera, so it
    // cannot foreshorten with the mesh, and comparing there measures that fact rather than the silhouette.
    // A lily pad seen edge-on covers a sixteenth of its own top-down area and reads as a 3.5x fat card.
    const float VanishedFraction = 0.4f;

    static float WorstRatio(float[] reference, float[] tier)
    {
        float peak = 0f;
        for (int i = 0; i < reference.Length; i++) peak = Mathf.Max(peak, reference[i]);

        float worst = 1f;
        for (int i = 0; i < reference.Length; i++)
        {
            if (reference[i] < 0.005f) continue;   // reference too small to compare against
            if (reference[i] < peak * VanishedFraction) continue;
            float ratio = tier[i] / reference[i];
            if (Mathf.Abs(Mathf.Log(Mathf.Max(ratio, 1e-4f))) > Mathf.Abs(Mathf.Log(Mathf.Max(worst, 1e-4f))))
                worst = ratio;
        }
        return worst;
    }

    static string Join(float[] v)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(','); sb.Append(v[i].ToString("0.000")); }
        return sb.ToString();
    }

    // Offscreen camera on an isolated layer, parked far below the world so a loaded planet cannot render
    // into the frame. Everything it draws is tinted opaque white, so a lit pixel means "geometry survived
    // the clip here" and nothing more.
    sealed class AuditRig
    {
        static readonly int _fadeStartId = Shader.PropertyToID("_FadeStart");
        static readonly int _fadeEndId = Shader.PropertyToID("_FadeEnd");
        static readonly int _fadeInStartId = Shader.PropertyToID("_FadeInStart");
        static readonly int _fadeInEndId = Shader.PropertyToID("_FadeInEnd");
        static readonly int _fadeOutStartId = Shader.PropertyToID("_FadeOutStart");
        static readonly int _fadeOutEndId = Shader.PropertyToID("_FadeOutEnd");
        static readonly int _lodDebugTintId = Shader.PropertyToID("_LodDebugTint");

        // Far enough out that the fade-out band never opens, without overflowing the shader's float math.
        const float NeverFade = 1e8f;

        public readonly Vector3 Origin = new Vector3(0f, -200000f, 0f);

        readonly GameObject _root;
        readonly GameObject _camGo;
        readonly Camera _cam;
        readonly RenderTexture _rt;
        readonly Texture2D _readback;
        readonly List<Object> _perProto = new List<Object>();
        ComputeBuffer _dummyInteractors;

        public AuditRig()
        {
            // This rig runs in edit mode, where no planet has bound _GrassInteractors. Without the fallback
            // every FoliageLit draw is dropped and the audit measures zero mesh coverage — which reads as
            // every card being hundreds of times its mesh.
            GrassInteractorFallback.Bind(ref _dummyInteractors);
            _root = new GameObject("__lodAudit") { hideFlags = HideFlags.HideAndDontSave };
            _root.transform.position = Origin;
            _camGo = new GameObject("cam");
            _camGo.transform.SetParent(_root.transform, false);
            _cam = _camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.cullingMask = 1 << AuditLayer;
            _cam.fieldOfView = Fov;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 200000f;
            _cam.enabled = false;   // rendered on demand, never per frame
            var data = _camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            _rt = new RenderTexture(Res, Res, 24, RenderTextureFormat.ARGB32);
            _rt.Create();
            _readback = new Texture2D(Res, Res, TextureFormat.RGBA32, false);
            _cam.targetTexture = _rt;
        }

        public void ShowMeshLod(ScatterPrototypeDto proto, int lod)
        {
            ClearProps();
            foreach (ScatterPartDto part in proto.Parts)
            {
                if (!part.CanRender) continue;
                Mesh mesh = part.LodMeshes[Mathf.Min(lod, part.LodMeshes.Length - 1)];
                if (mesh == null) continue;
                var mat = new Material(part.Material);
                mat.SetFloat(_fadeStartId, NeverFade);
                mat.SetFloat(_fadeEndId, NeverFade + 1f);
                mat.SetColor(_lodDebugTintId, Color.white);
                AddProp(mesh, mat);
            }
        }

        public void ShowCard(ScatterLodBatcher.Impostor impostor)
        {
            ClearProps();
            var mat = new Material(impostor.Params.material);
            mat.SetFloat(_fadeInStartId, 0f);
            mat.SetFloat(_fadeInEndId, 0.001f);
            mat.SetFloat(_fadeOutStartId, NeverFade);
            mat.SetFloat(_fadeOutEndId, NeverFade + 1f);
            mat.SetColor(_lodDebugTintId, Color.white);
            AddProp(impostor.Quad, mat);
        }

        void AddProp(Mesh mesh, Material mat)
        {
            _perProto.Add(mat);
            var go = new GameObject("prop") { layer = AuditLayer, hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            _perProto.Add(go);
        }

        public void ClearProps()
        {
            foreach (Object o in _perProto) Object.DestroyImmediate(o);
            _perProto.Clear();
        }

        public float[] CoverAtElevations(Vector3 target, float distance, float[] elevations, int targetPx)
        {
            var result = new float[elevations.Length];
            for (int i = 0; i < elevations.Length; i++)
            {
                float rad = elevations[i] * Mathf.Deg2Rad;
                var offset = new Vector3(0f, Mathf.Sin(rad), -Mathf.Cos(rad)) * distance;
                _camGo.transform.position = target + offset;
                _camGo.transform.LookAt(target);
                result[i] = LitFraction(targetPx);
            }
            return result;
        }

        float LitFraction(int targetPx)
        {
            _cam.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _rt;
            _readback.ReadPixels(new Rect(0, 0, Res, Res), 0, 0);
            _readback.Apply();
            RenderTexture.active = previous;

            Color32[] px = _readback.GetPixels32();
            int lit = 0;
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                if ((c.r * 77 + c.g * 150 + c.b * 29) >> 8 > 40) lit++;
            }
            // Normalised by the framed box, so a prototype wider than it is tall can exceed 1.
            return lit / (float)(targetPx * targetPx);
        }

        public void Dispose()
        {
            ClearProps();
            _cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(_readback);
            _rt.Release();
            Object.DestroyImmediate(_rt);
            Object.DestroyImmediate(_root);
            _dummyInteractors?.Release();
            _dummyInteractors = null;
        }
    }
}
