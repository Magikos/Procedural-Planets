using UnityEngine;

// Review adapter for the shared, tested bone-chain solver. No second spring solver lives here.
public sealed class HumanAccessoryMotion : MonoBehaviour, IGroundingProvider
{
    public HumanStyleReview Review;
    public Transform Actor;
    public SpringChainDefinition Chain = new SpringChainDefinition();
    public CapsuleCollider[] Contacts = System.Array.Empty<CapsuleCollider>();
    public bool UseFloor;
    public HumanAccessoryFit Mount;
    BoneChainSpring _spring;
    Quaternion[] _rotations;
    Vector3[] _positions;
    int _motion = -1;
    float _phase;
    Vector4 _shape;
    HumanBodyReview _body;
    float[] _radii;
    float _influence;
    int _releaseVersion;
    public float ContactError => _spring?.MaxGroundPenetration ?? 0;

    void Initialize()
    {
        _spring = new BoneChainSpring(Chain);
        _influence = enabled && (Review == null || Review.SecondaryMotion) ? 1f : 0f;
        _rotations = new Quaternion[Chain.Bones.Length];
        _positions = new Vector3[Chain.Bones.Length];
        for (int i = 0; i < Chain.Bones.Length; i++)
        {
            _rotations[i] = Chain.Bones[i].localRotation;
            _positions[i] = Chain.Bones[i].localPosition;
        }
        _body = Review != null ? Review.GetComponent<HumanBodyReview>() : null;
        _radii = new float[Contacts.Length];
        for (int i = 0; i < Contacts.Length; i++) _radii[i] = Contacts[i].radius;
    }

    void Restore()
    {
        if (_rotations == null) return;
        for (int i = 0; i < Chain.Bones.Length; i++)
            Chain.Bones[i].SetLocalPositionAndRotation(_positions[i], _rotations[i]);
    }

    void LateUpdate() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f) return;
        if (_spring == null) Initialize();
        Restore();
        if (Mount != null) Mount.Apply();
        bool active = enabled && (Review == null || Review.SecondaryMotion);
        _influence = Mathf.MoveTowards(_influence, active ? 1f : 0f, Mathf.Min(deltaTime, .05f) * 6f);
        if (_influence <= 0f) { _spring.Reset(); return; }
        Vector4 shape = _body != null ? new Vector4(_body.Muscular, _body.Heavy, _body.Skinny, _body.Feminine) : Vector4.zero;
        if (Review != null && (_motion != Review.MotionIndex || shape != _shape
            || (Review.Paused && Mathf.Abs(_phase - Review.Phase) > .0001f))) _spring.Reset();
        if (Actor != null && Actor.name.EndsWith("_Fit"))
            for (int i = 0; i < Contacts.Length; i++)
                Contacts[i].radius = _radii[i] * Mathf.Max(.8f, 1 + shape.y * .0065f + shape.x * .0015f - shape.z * .0008f);
        var up = Actor != null ? Actor.up : Vector3.up;
        _spring.Tick(-up * 9.81f, deltaTime, 0, UseFloor || Contacts.Length > 0 ? this : null, up, _influence);
        if (Review != null) { _motion = Review.MotionIndex; _phase = Review.Phase; }
        _shape = shape;
    }

    void OnEnable() => _releaseVersion++;

    void OnDisable()
    {
        int version = ++_releaseVersion;
        if (_spring == null) return;
        if (!gameObject.activeInHierarchy)
        {
            // Hidden model teardown has no visible transition to preserve.
            Restore(); _spring.Reset(); _influence = 0f;
        }
        else if (Application.isPlaying) _ = ReleaseAfterDisable(version);
    }

    async Awaitable ReleaseAfterDisable(int version)
    {
        try
        {
            // A disabled component receives no LateUpdate. Keep the same writer alive
            // only for its bounded release; enabling it again invalidates this loop.
            while (this != null && !enabled && gameObject.activeInHierarchy && _influence > 0f && version == _releaseVersion)
            {
                await Awaitable.EndOfFrameAsync(destroyCancellationToken);
                if (this == null || enabled || !gameObject.activeInHierarchy || version != _releaseVersion) return;
                Tick(Time.deltaTime);
            }
        }
        catch (System.OperationCanceledException) { }
    }

    public bool TryGround(Vector3 point, Vector3 down, float clearance, out GroundResult result)
    {
        // This fitting room has a flat floor at y=0. Body capsules provide local support planes.
        result = new GroundResult(new Vector3(point.x, clearance, point.z), Vector3.up);
        float deepest = UseFloor ? clearance - point.y : float.NegativeInfinity;
        bool found = UseFloor;
        foreach (var capsule in Contacts)
        {
            if (capsule == null) continue;
            Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
            float half = Mathf.Max(0, capsule.height * .5f - capsule.radius);
            var a = capsule.transform.TransformPoint(capsule.center - axis * half);
            var b = capsule.transform.TransformPoint(capsule.center + axis * half);
            var segment = b - a;
            var center = a + segment * (segment.sqrMagnitude > 1e-10f
                ? Mathf.Clamp01(Vector3.Dot(point - a, segment) / segment.sqrMagnitude) : 0);
            var scale = capsule.transform.lossyScale;
            float radiusScale = capsule.direction == 0 ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))
                : capsule.direction == 1 ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            float radius = capsule.radius * radiusScale + clearance;
            var difference = point - center;
            float penetration = radius - difference.magnitude;
            if (penetration <= 0 || penetration <= deepest) continue;
            var normal = difference.sqrMagnitude > 1e-10f ? difference.normalized : -(Actor != null ? Actor.forward : Vector3.forward);
            result = new GroundResult(center + normal * radius, normal);
            deepest = penetration; found = true;
        }
        return found;
    }
}
