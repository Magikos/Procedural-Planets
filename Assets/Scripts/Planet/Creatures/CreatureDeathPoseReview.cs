using UnityEngine;

/// <summary>Matched authored/supported death poses for visual review, without a gameplay authority.</summary>
public sealed class CreatureDeathPoseReview : MonoBehaviour, IGroundingProvider
{
    public CreatureVisualSettings Visuals;
    [Min(.1f)] public float Height = 1.84f;
    [Range(-40f, 40f)] public float SlopeDegrees = 20f;
    [Range(0f, 1f)] public float MeatFraction = 1f;
    public bool ShowAuthored;
    CreatureAnimationView _authored;
    CreatureCarcassView _supported;
    readonly System.Collections.Generic.List<Material> _materials = new();
    public bool Settled => _supported != null && _supported.Settled;

    void OnEnable() => Initialize();
    public void Initialize()
    {
        if (_authored != null || Visuals == null) return;
        var visuals = Visuals.Snapshot();
        _authored = new CreatureAnimationView(transform, 1, visuals, Height) { Dead = true };
        Vector3 position = transform.position + transform.up * Height * .5f;
        _authored.Root.SetPositionAndRotation(position, transform.rotation);
        _authored.Tick(Vector3.zero, transform.up, (visuals.Death?.length ?? 0f) + .01f);
        var species = CreatureSpeciesDto.From(new CreatureSpecies { DisplayName = "Death pose review", BodyHeightMeters = Height, Visuals = Visuals });
        var corpse = new CreatureCorpse(new EntityId(1), 0, position, transform.rotation, 0, false);
        _supported = new CreatureCarcassView(transform, corpse, species, this);
        CreatureAnimationPrototype.ApplyPreviewMaterials(_authored.Root, _materials);
        CreatureAnimationPrototype.ApplyPreviewMaterials(_supported.Root, _materials);
    }

    void Update() => Step();
    public void Step()
    {
        Initialize();
        if (_authored == null) return;
        _authored.Root.gameObject.SetActive(ShowAuthored);
        _supported.Root.gameObject.SetActive(true);
        _supported.Sync(MeatFraction, CorpseStage.Fresh);
        _supported.Root.gameObject.SetActive(!ShowAuthored);
    }

    public bool TryGround(Vector3 point, Vector3 down, float offset, out GroundResult result)
    {
        Vector3 normal = Quaternion.AngleAxis(SlopeDegrees, transform.forward) * transform.up;
        float denominator = Vector3.Dot(down, normal);
        if (Mathf.Abs(denominator) < .001f) { result = default; return false; }
        result = new GroundResult(point + down * (Vector3.Dot(transform.position - point, normal) / denominator - offset), normal);
        return true;
    }

    void OnDisable()
    {
        _authored?.Dispose(); _authored = null;
        _supported?.Dispose(); _supported = null;
        foreach (var material in _materials)
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        _materials.Clear();
    }
}
