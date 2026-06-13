using UnityEngine;

public sealed class WaterWakeController : MonoBehaviour
{
    public const int MaxWakeSources = 8;

    static readonly int _waterWakeCountId = Shader.PropertyToID(ShaderGlobalIds.WaterWakeCount);
    static readonly int _waterWakePositionsId = Shader.PropertyToID(ShaderGlobalIds.WaterWakePositions);
    static readonly int _waterWakeDirectionsId = Shader.PropertyToID(ShaderGlobalIds.WaterWakeDirections);
    static readonly int _waterWakeParamsId = Shader.PropertyToID(ShaderGlobalIds.WaterWakeParams);
    static readonly int _oceanDebugModeId = Shader.PropertyToID(ShaderGlobalIds.OceanDebugMode);

    static readonly Vector4[] _positions = new Vector4[MaxWakeSources];
    static readonly Vector4[] _directions = new Vector4[MaxWakeSources];
    static readonly Vector4[] _params = new Vector4[MaxWakeSources];

    public struct WakeSource
    {
        public Vector3 Position;
        public Vector3 Direction;
        public float Radius;
        public float Length;
        public float Strength;
        public float FoamStrength;
        public float Speed01;
    }

    void OnEnable()
    {
        ClearWakeGlobals();
    }

    void LateUpdate()
    {
        using (FrameTimingCounters.Measure(FrameTimingSection.Water))
            PublishWakeSources();
    }

    void PublishWakeSources()
    {
        int count = 0;
        var emitters = WaterWakeEmitter.ActiveEmitters;
        for (int i = 0; i < emitters.Count && count < MaxWakeSources; i++)
        {
            WaterWakeEmitter emitter = emitters[i];
            if (emitter == null || !emitter.isActiveAndEnabled)
                continue;

            if (!emitter.TryGetWakeSource(out WakeSource source))
                continue;

            Vector3 direction = source.Direction.sqrMagnitude > 0.0001f
                ? source.Direction.normalized
                : Vector3.forward;

            _positions[count] = new Vector4(source.Position.x, source.Position.y, source.Position.z, Mathf.Max(source.Radius, 0.25f));
            _directions[count] = new Vector4(direction.x, direction.y, direction.z, Mathf.Clamp01(source.Speed01));
            _params[count] = new Vector4(
                Mathf.Clamp01(source.Strength),
                Mathf.Clamp01(source.FoamStrength),
                Mathf.Max(source.Length, source.Radius * 2f),
                0f);
            count++;
        }

        if (count == 0 && TryGetDebugPreviewWake(out WakeSource preview))
            WriteWakeSource(preview, count++);

        for (int i = count; i < MaxWakeSources; i++)
        {
            _positions[i] = Vector4.zero;
            _directions[i] = Vector4.zero;
            _params[i] = Vector4.zero;
        }

        Shader.SetGlobalInt(_waterWakeCountId, count);
        Shader.SetGlobalVectorArray(_waterWakePositionsId, _positions);
        Shader.SetGlobalVectorArray(_waterWakeDirectionsId, _directions);
        Shader.SetGlobalVectorArray(_waterWakeParamsId, _params);
    }

    static void WriteWakeSource(WakeSource source, int index)
    {
        Vector3 direction = source.Direction.sqrMagnitude > 0.0001f
            ? source.Direction.normalized
            : Vector3.forward;

        _positions[index] = new Vector4(source.Position.x, source.Position.y, source.Position.z, Mathf.Max(source.Radius, 0.25f));
        _directions[index] = new Vector4(direction.x, direction.y, direction.z, Mathf.Clamp01(source.Speed01));
        _params[index] = new Vector4(
            Mathf.Clamp01(source.Strength),
            Mathf.Clamp01(source.FoamStrength),
            Mathf.Max(source.Length, source.Radius * 2f),
            0f);
    }

    static bool TryGetDebugPreviewWake(out WakeSource source)
    {
        source = default;

        int debugMode = Shader.GetGlobalInt(_oceanDebugModeId);
        if (debugMode != DebugModeConstants.WakeMask
            && debugMode != DebugModeConstants.SurfacePolish
            && debugMode != DebugModeConstants.SurfaceFxProof)
            return false;

        if (!ServiceLocator.TryGet(out ICameraRigContext cameraContext)
            || cameraContext.CameraTransform == null
            || cameraContext.SeaLevelRadius <= 0f)
            return false;

        Vector3 origin = cameraContext.CameraTransform.position;
        Vector3 rayDirection = cameraContext.CameraTransform.forward;
        if (rayDirection.sqrMagnitude <= 0.0001f)
            return false;

        rayDirection.Normalize();
        if (!TryRaySphere(origin, rayDirection, cameraContext.PlanetCenter, cameraContext.SeaLevelRadius, out Vector3 wakePosition))
        {
            Vector3 cameraNormal = origin - cameraContext.PlanetCenter;
            if (cameraNormal.sqrMagnitude <= 0.0001f)
                return false;

            wakePosition = cameraContext.PlanetCenter + cameraNormal.normalized * cameraContext.SeaLevelRadius;
        }

        Vector3 normal = wakePosition - cameraContext.PlanetCenter;
        if (normal.sqrMagnitude <= 0.0001f)
            return false;

        normal.Normalize();
        Vector3 direction = Vector3.ProjectOnPlane(rayDirection, normal);
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.Cross(normal, Vector3.up);
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector3.Cross(normal, Vector3.right);

        direction.Normalize();
        float scale = Mathf.Max(cameraContext.SeaLevelRadius / 5000f, 0.1f);
        source = new WakeSource
        {
            Position = wakePosition,
            Direction = direction,
            Radius = 42f * scale,
            Length = 380f * scale,
            Strength = 0.95f,
            FoamStrength = 0.85f,
            Speed01 = 1f
        };
        return true;
    }

    static bool TryRaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out Vector3 hitPoint)
    {
        hitPoint = default;
        Vector3 offset = origin - center;
        float b = Vector3.Dot(offset, direction);
        float c = Vector3.Dot(offset, offset) - radius * radius;
        float discriminant = b * b - c;
        if (discriminant < 0f)
            return false;

        float root = Mathf.Sqrt(discriminant);
        float near = -b - root;
        float far = -b + root;
        float distance = near > 0f ? near : far;
        if (distance <= 0f)
            return false;

        hitPoint = origin + direction * distance;
        return true;
    }

    void OnDisable()
    {
        ClearWakeGlobals();
    }

    static void ClearWakeGlobals()
    {
        for (int i = 0; i < MaxWakeSources; i++)
        {
            _positions[i] = Vector4.zero;
            _directions[i] = Vector4.zero;
            _params[i] = Vector4.zero;
        }

        Shader.SetGlobalInt(_waterWakeCountId, 0);
        Shader.SetGlobalVectorArray(_waterWakePositionsId, _positions);
        Shader.SetGlobalVectorArray(_waterWakeDirectionsId, _directions);
        Shader.SetGlobalVectorArray(_waterWakeParamsId, _params);
    }
}
