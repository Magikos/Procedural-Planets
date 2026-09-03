using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// LOD illusion bench. Two things make a tier swap hard to judge in the planet scene, and this fixes both.
//
// The prop is held at the DISTANCE where a tier really takes over and the frame is then magnified with
// point filtering, because a 36 px card cannot be judged at 36 px and walking closer to see it changes the
// mip level, the screen size and the tier all at once. And the tier is FORCED by key, so between two views
// the only thing that differs is the tier itself - the illusion holds if the silhouette, the colour and the
// brightness survive the swap at the size the swap happens.
//
// It draws the RUNTIME library (TreeInjection.ApplyAll), not the ScatterLibrary asset. The generated trees
// and rocks exist only after injection, so a harness reading the asset cannot see the props that pop.
public sealed class ScatterLodCompare : MonoBehaviour
{
    public ScatterLibrary Library;
    public bool IncludeGenerated = true;

    // Screen heights to hold the prop at. 36 is MeshHandoverPixels - the size every mesh-to-card swap
    // actually happens at, so it is the default and the only one that answers "is the swap visible".
    public float[] PixelHeights = { 36f, 48f, 96f, 192f };
    public int Magnify = 8;
    public float OrbitDegreesPerSecond = 20f;

    ScatterPrototypeDto[] _protos = System.Array.Empty<ScatterPrototypeDto>();
    readonly Dictionary<int, RenderParams[]> _partParams = new();
    readonly Dictionary<int, ScatterLodBatcher.Impostor> _impostors = new();
    readonly Dictionary<int, MaterialPropertyBlock> _cardProps = new();
    ScatterLodBatcher _batcher;
    Camera _cam;
    RenderTexture _rt;
    GUIStyle _labelStyle;

    int _index;
    int _tierIdx;      // 0..n-1 mesh LODs, n card, n+1 auto
    int _sizeStep;
    bool _pair = true;
    bool _orbit;
    float _yaw;

    const int MaxMeshLods = 8;

    static readonly int _fadeStartId = Shader.PropertyToID("_FadeStart");
    static readonly int _fadeEndId = Shader.PropertyToID("_FadeEnd");
    static readonly int _fadeInStartId = Shader.PropertyToID("_FadeInStart");
    static readonly int _fadeInEndId = Shader.PropertyToID("_FadeInEnd");
    static readonly int _fadeOutStartId = Shader.PropertyToID("_FadeOutStart");
    static readonly int _fadeOutEndId = Shader.PropertyToID("_FadeOutEnd");

    readonly Matrix4x4[] _one = new Matrix4x4[1];
    readonly List<Matrix4x4> _autoMatrices = new();
    readonly List<Vector3> _autoPositions = new();

    void Start()
    {
        _cam = Camera.main;
        _batcher = new ScatterLodBatcher();
        BuildLibrary();
        _tierIdx = Mathf.Min(1, TierCount(Current) - 1); // first coarser tier; LEFT holds the model
    }

    void OnDestroy()
    {
        if (_cam != null) _cam.targetTexture = null;
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
    }

    void BuildLibrary()
    {
        if (Library == null) return;
        ScatterLibraryDto dto = ScatterLibraryDto.From(Library);
        if (IncludeGenerated) dto = TreeInjection.ApplyAll(dto);
        var keep = new List<ScatterPrototypeDto>();
        foreach (ScatterPrototypeDto p in dto.Prototypes)
            if (p != null && p.CanRender) keep.Add(p);
        _protos = keep.ToArray();
    }

    ScatterPrototypeDto Current => _protos.Length > 0 ? _protos[Mathf.Clamp(_index, 0, _protos.Length - 1)] : null;

    int MeshLodCount(ScatterPrototypeDto proto)
    {
        if (proto == null) return 0;
        int n = 0;
        foreach (ScatterPartDto part in proto.Parts)
        {
            if (!part.CanRender) continue;
            n = Mathf.Max(n, Mathf.Min(part.LodMeshes.Length, part.LodEndDistances.Length));
        }
        return Mathf.Min(n, MaxMeshLods);
    }

    // Mesh LODs, then the card, then AUTO. One ordered list so the tier keys are a single clamped index.
    int TierCount(ScatterPrototypeDto proto) => MeshLodCount(proto) + 2;
    int CardIdx(ScatterPrototypeDto proto) => MeshLodCount(proto);
    int AutoIdx(ScatterPrototypeDto proto) => MeshLodCount(proto) + 1;

    // Built on demand: a prototype the bench never selects should not pay for a card.
    RenderParams[] PartParamsFor(int index)
    {
        if (_partParams.TryGetValue(index, out RenderParams[] hit)) return hit;
        ScatterPrototypeDto proto = _protos[index];
        var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        var rps = new RenderParams[proto.Parts.Length];
        for (int i = 0; i < proto.Parts.Length; i++)
        {
            ScatterPartDto part = proto.Parts[i];
            if (!part.CanRender) continue;
            rps[i] = new RenderParams(part.Material)
            {
                shadowCastingMode = part.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = part.ReceiveShadows,
                worldBounds = bounds,
                matProps = new MaterialPropertyBlock(),
            };
        }
        _partParams[index] = rps;
        return rps;
    }

    ScatterLodBatcher.Impostor ImpostorFor(int index)
    {
        if (_impostors.TryGetValue(index, out ScatterLodBatcher.Impostor hit)) return hit;
        var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        ScatterLodBatcher.Impostor imp = ScatterImpostorFactory.TryBuild(_protos[index], bounds);
        _impostors[index] = imp;
        return imp;
    }

    MaterialPropertyBlock CardPropsFor(int index)
    {
        if (_cardProps.TryGetValue(index, out MaterialPropertyBlock hit)) return hit;
        var mpb = new MaterialPropertyBlock();
        // Opaque at any bench distance. The card's baked fade ramps are keyed to its real band, and this
        // bench deliberately parks the prop outside that band to hold a fixed screen size.
        mpb.SetFloat(_fadeInStartId, -2f);
        mpb.SetFloat(_fadeInEndId, -1f);
        mpb.SetFloat(_fadeOutStartId, 1e9f);
        mpb.SetFloat(_fadeOutEndId, 1e9f + 1f);
        _cardProps[index] = mpb;
        return mpb;
    }

    // Union of the drawable parts' LOD0 bounds. Height sets the camera aim so a tall tree is framed on its
    // crown and not on its roots.
    static Bounds Lod0Bounds(ScatterPrototypeDto proto)
    {
        Bounds b = default;
        bool first = true;
        foreach (ScatterPartDto part in proto.Parts)
        {
            if (!part.CanRender) continue;
            Bounds pb = part.LodMeshes[0].bounds;
            if (first) { b = pb; first = false; } else b.Encapsulate(pb);
        }
        return b;
    }

    float TargetPixels => PixelHeights != null && PixelHeights.Length > 0
        ? PixelHeights[Mathf.Clamp(_sizeStep, 0, PixelHeights.Length - 1)] : 36f;

    // Inverse of the screen-size projection: how far a prop of this height must sit to cover `pixels` of a
    // `Screen.height` frame at this camera's FOV. Same relation ScatterPrototypeDto.MeshCullDistance uses to
    // pick the handover, so asking for 36 px puts the prop at its own handover distance.
    float DistanceForPixels(float sizeMeters, float pixels, int frameHeight)
    {
        if (sizeMeters <= 0f || pixels <= 0f || _cam == null) return 1f;
        float tan = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return sizeMeters * frameHeight / (2f * tan * pixels);
    }

    void Update()
    {
        if (SweepActive) return;
        ReadKeys();
        if (_orbit) _yaw += OrbitDegreesPerSecond * Time.deltaTime;
        Draw();
    }

    // ScatterLodSweep drives the same placement and draw code headlessly. It owns the camera and the render
    // target while it runs, so the interactive path stands down rather than fighting it for both.
    internal bool SweepActive { get; set; }
    internal int ProtoCount => _protos.Length;
    internal ScatterPrototypeDto ProtoAt(int i) => _protos[i];
    internal int CardTierOf(ScatterPrototypeDto proto) => CardIdx(proto);
    internal Camera BenchCamera => _cam;
    internal ScatterLodBatcher.Impostor ImpostorOf(int index) => ImpostorFor(index);

    // A mesh LOD whose band is empty after the MeshCullDistance clip is authored but never reached in game.
    internal bool TierDraws(int protoIndex, int tier)
    {
        ScatterPrototypeDto proto = _protos[protoIndex];
        if (tier >= CardIdx(proto)) return proto.HasImpostor;
        ScatterPartDto part = proto.TrunkPart;
        return part != null && tier < part.LodEndDistances.Length
            && ScatterLodBatcher.BandNearFor(tier, part.LodEndDistances)
               < ScatterLodBatcher.BandFarFor(tier, part.LodEndDistances, proto.MeshCullDistance);
    }

    // Highest mesh LOD that still draws. That is the tier the card actually replaces.
    internal int LastDrawnMeshLod(int protoIndex)
    {
        int last = 0;
        for (int lod = 0; lod < CardIdx(_protos[protoIndex]); lod++)
            if (TierDraws(protoIndex, lod)) last = lod;
        return last;
    }

    internal void PlaceCamera(int protoIndex, float pixels, int frameHeight, float yaw)
    {
        ScatterPrototypeDto proto = _protos[protoIndex];
        float sizeM = proto.BoundsSizeMeters;
        float dist = DistanceForPixels(sizeM, pixels, frameHeight);
        Vector3 aim = new Vector3(0f, Lod0Bounds(proto).center.y, 0f);
        Quaternion orbit = Quaternion.Euler(0f, yaw, 0f);
        _cam.transform.position = aim + orbit * new Vector3(0f, sizeM * 0.15f, -dist);
        _cam.transform.rotation = Quaternion.LookRotation(aim - _cam.transform.position, Vector3.up);
    }

    internal void DrawTierFor(int protoIndex, int tierIdx) =>
        DrawTier(protoIndex, _protos[protoIndex], tierIdx, Vector3.zero);

    void ReadKeys()
    {
        Keyboard k = Keyboard.current;
        if (k == null || _protos.Length == 0) return;

        int step = k.shiftKey.isPressed ? 10 : 1;
        int len = _protos.Length;
        if (k.rightArrowKey.wasPressedThisFrame) { _index = ((_index + step) % len + len) % len; ClampTier(); }
        if (k.leftArrowKey.wasPressedThisFrame) { _index = ((_index - step) % len + len) % len; ClampTier(); }

        int tiers = TierCount(Current);
        if (k.upArrowKey.wasPressedThisFrame) _tierIdx = (_tierIdx + 1) % tiers;
        if (k.downArrowKey.wasPressedThisFrame) _tierIdx = (_tierIdx - 1 + tiers) % tiers;

        if (PixelHeights != null)
            for (int i = 0; i < 4 && i < PixelHeights.Length; i++)
                if (k[Key.Digit1 + i].wasPressedThisFrame) _sizeStep = i;

        if (k.pKey.wasPressedThisFrame) _pair = !_pair;
        if (k.oKey.wasPressedThisFrame) _orbit = !_orbit;
        if (k.rKey.wasPressedThisFrame) _yaw = 0f;
        if (k.equalsKey.wasPressedThisFrame) Magnify = Mathf.Min(Magnify + 1, 24);
        if (k.minusKey.wasPressedThisFrame) Magnify = Mathf.Max(Magnify - 1, 1);
    }

    void ClampTier() => _tierIdx = Mathf.Clamp(_tierIdx, 0, TierCount(Current) - 1);


    void Draw()
    {
        ScatterPrototypeDto proto = Current;
        if (proto == null || _cam == null) return;

        float sizeM = proto.BoundsSizeMeters;
        float sep = _pair ? sizeM * 1.8f : 0f;
        PlaceCamera(_index, TargetPixels, Screen.height, _yaw);

        // LEFT is always the LOD0 mesh - the model itself. Only the right side moves, so any difference you
        // see is the tier drifting from the source of truth and nothing else.
        Vector3 right = _cam.transform.right;
        if (_pair) DrawTier(_index, proto, 0, -right * sep * 0.5f);
        DrawTier(_index, proto, _tierIdx, right * sep * 0.5f);
    }

    void DrawTier(int protoIndex, ScatterPrototypeDto proto, int idx, Vector3 offset)
    {
        Matrix4x4 m = Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one);

        if (idx == AutoIdx(proto))
        {
            _autoMatrices.Clear(); _autoPositions.Clear();
            _autoMatrices.Add(m); _autoPositions.Add(offset);
            _batcher.Draw(proto, PartParamsFor(protoIndex), _autoMatrices, _autoPositions,
                          _cam.transform.position, ImpostorFor(protoIndex));
            return;
        }

        _one[0] = m;
        if (idx == CardIdx(proto))
        {
            ScatterLodBatcher.Impostor imp = ImpostorFor(protoIndex);
            if (!imp.Valid) return;
            RenderParams rp = imp.Params;
            rp.matProps = CardPropsFor(protoIndex);
            Graphics.RenderMeshInstanced(rp, imp.Quad, 0, _one, 1);
            return;
        }

        RenderParams[] rps = PartParamsFor(protoIndex);
        for (int i = 0; i < proto.Parts.Length; i++)
        {
            ScatterPartDto part = proto.Parts[i];
            if (!part.CanRender) continue;
            int lodCount = Mathf.Min(part.LodMeshes.Length, part.LodEndDistances.Length);
            Mesh mesh = part.LodMeshes[Mathf.Clamp(idx, 0, lodCount - 1)];
            if (mesh == null) continue;
            RenderParams rp = rps[i];
            // No distance dither: the bench compares the tier, not its fade-out.
            rp.matProps.SetFloat(_fadeStartId, 1e9f);
            rp.matProps.SetFloat(_fadeEndId, 1e9f + 1f);
            Graphics.RenderMeshInstanced(rp, mesh, 0, _one, 1);
        }
    }

    string TierLabel(ScatterPrototypeDto proto, int idx)
    {
        if (idx == AutoIdx(proto)) return "AUTO (game rule)";
        if (idx == CardIdx(proto)) return "CARD";
        ScatterPartDto part = proto.TrunkPart;
        bool drawn = part != null && idx < part.LodEndDistances.Length
                  && ScatterLodBatcher.BandNearFor(idx, part.LodEndDistances)
                     < ScatterLodBatcher.BandFarFor(idx, part.LodEndDistances, proto.MeshCullDistance);
        return $"LOD{idx}{(drawn ? "" : "  [NEVER DRAWS IN GAME]")}";
    }

    void OnGUI()
    {
        ScatterPrototypeDto proto = Current;
        if (proto == null) return;
        EnsureRenderTexture();

        // Magnified point-filtered crop, centred. Preserves the real pixel size, mip level and silhouette -
        // the prop stays 36 px, you just get to look at those 36 px.
        if (_rt != null)
        {
            float f = 1f / Mathf.Max(1, Magnify);
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, Screen.width, Screen.height), _rt,
                                         new Rect(0.5f - f * 0.5f, 0.5f - f * 0.5f, f, f), false);
        }

        _labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = false };
        _labelStyle.normal.textColor = Color.white;

        float sizeM = proto.BoundsSizeMeters;
        float dist = DistanceForPixels(sizeM, TargetPixels, Screen.height);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{_index + 1}/{_protos.Length}] {proto.DisplayName}   key={proto.ImpostorShareKey ?? "-"}");
        sb.AppendLine($"LEFT: {(_pair ? "MODEL (LOD0)" : "off")}    RIGHT: {TierLabel(proto, _tierIdx)}   [{_tierIdx + 1}/{TierCount(proto)}]");
        sb.AppendLine($"held at {TargetPixels:0} px  =>  {dist:0.0} m   (prop {sizeM:0.0} m tall, magnified {Magnify}x)");
        sb.AppendLine(proto.HasImpostor
            ? $"game: mesh culls {proto.MeshCullDistance:0} m, card {proto.ImpostorStartDistance:0}..{proto.ImpostorEndDistance:0} m"
            : "game: NO CARD - hard culls at mesh range");
        sb.Append("left/right prop   up/down tier   1-4 size   P model on-off   O orbit   R reset   +/- zoom   F sweep all");

        GUI.Box(new Rect(8, 8, 640, 122), GUIContent.none);
        GUI.Label(new Rect(16, 12, 624, 114), sb.ToString(), _labelStyle);
    }

    void EnsureRenderTexture()
    {
        if (_cam == null) return;
        if (_rt != null && _rt.width == Screen.width && _rt.height == Screen.height) return;
        if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
        _rt = new RenderTexture(Screen.width, Screen.height, 24) { filterMode = FilterMode.Point };
        _cam.targetTexture = _rt;
    }
}
