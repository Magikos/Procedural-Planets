using System.Collections.Generic;
using UnityEngine;

public struct ScaleReferenceDebugStats
{
    public bool HasDrop;
    public bool LastDropSucceeded;
    public string LastTargetStatus;
    public Vector3 LastAnchor;
    public Vector3 LastWorldUp;
    public Vector3 LastTangentForward;
    public float LastSurfaceRadius;
    public float LastSeaLevelRadius;
    public float LastCameraDistance;
    public float LastCameraToAnchorDistance;
    public float LastAltitudeAboveSurface;
    public float LastRayDistance;
    public int MarkerCount;
    public int MarkerProjectionHits;
    public int MarkerProjectionFallbacks;
}

public interface IScaleReferenceDebugStatsProvider
{
    ScaleReferenceDebugStats GetScaleReferenceDebugStats();
}

/// <summary>
/// Places known-size reference shapes on the planet surface at the camera look target.
/// The marker set is a grass/terrain scale diagnostic, so placement must use the same
/// sampled terrain surface the gameplay camera uses instead of a broad planet sphere.
/// </summary>
[DisallowMultipleComponent]
[CommandPrefix("scale")]
public sealed class ScaleReferenceMarkers : MonoBehaviour, IScaleReferenceDebugStatsProvider
{
    [ConsoleCommand("drop", "Drop scale reference markers (1m / 1.8m human / 3m / 10m / 30m) at the camera look target.")]
    static void DropCmd()
        => EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.DropScaleMarkers));

    [ConsoleCommand("clear", "Clear all scale reference markers.")]
    static void ClearCmd()
        => EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.ClearScaleMarkers));

    [ConsoleCommand("teleport", "Teleport the camera to the last marker chain.")]
    static void TeleportCmd()
        => EventBus<DebugCommandRequestedEvent>.Raise(new DebugCommandRequestedEvent(DebugCommandType.TeleportToScaleMarkers));

    static readonly MarkerSpec[] Specs = new[]
    {
        new MarkerSpec("ScaleRef_1m_cube", PrimitiveType.Cube, new Vector3(1f, 1f, 1f), new Color(0.92f, 0.18f, 0.18f)),
        new MarkerSpec("ScaleRef_1p8m_human", PrimitiveType.Capsule, new Vector3(0.5f, 0.9f, 0.5f), new Color(1f, 0.55f, 0.10f)),
        new MarkerSpec("ScaleRef_3m_cube", PrimitiveType.Cube, new Vector3(3f, 3f, 3f), new Color(0.30f, 0.85f, 0.30f)),
        new MarkerSpec("ScaleRef_10m_cube", PrimitiveType.Cube, new Vector3(10f, 10f, 10f), new Color(0.95f, 0.85f, 0.20f)),
        new MarkerSpec("ScaleRef_30m_pillar", PrimitiveType.Cube, new Vector3(1f, 30f, 1f), new Color(0.30f, 0.45f, 0.95f)),
    };

    const float MarkerSpacingMeters = 4f;
    const float RayHitToleranceMeters = 0.02f;
    const float RayMinStepMeters = 0.5f;
    const float RayMaxStepMeters = 250f;
    const int RayMarchMaxSteps = 160;
    const int RayBinaryRefineSteps = 18;
    const float MarkerProjectionUpMeters = 250f;
    const float MarkerProjectionDownMeters = 500f;
    const float MarkerSurfaceClearanceMeters = 0.03f;

    readonly List<GameObject> _markers = new();
    Vector3 _lastDropPlanetCenter;
    Vector3 _lastDropWorldUp;
    Vector3 _lastDropAnchor;
    Vector3 _lastDropTangentForward;
    bool _hasDrop;
    ScaleReferenceDebugStats _debugStats;
    Material _markerMaterial;

    void OnEnable()
    {
        ServiceLocator.Register<IScaleReferenceDebugStatsProvider>(this);
        EventBus<DebugDropScaleMarkersRequestedEvent>.Listen(OnDropRequested);
        EventBus<DebugClearScaleMarkersRequestedEvent>.Listen(OnClearRequested);
        EventBus<DebugTeleportToScaleMarkersRequestedEvent>.Listen(OnTeleportRequested);
    }

    void OnDisable()
    {
        EventBus<DebugDropScaleMarkersRequestedEvent>.Unlisten(OnDropRequested);
        EventBus<DebugClearScaleMarkersRequestedEvent>.Unlisten(OnClearRequested);
        EventBus<DebugTeleportToScaleMarkersRequestedEvent>.Unlisten(OnTeleportRequested);
        ServiceLocator.Unregister<IScaleReferenceDebugStatsProvider>(this);
    }

    void OnDestroy()
    {
        ClearMarkers();
        if (_markerMaterial != null)
        {
            Object.Destroy(_markerMaterial);
            _markerMaterial = null;
        }
    }

    void OnDropRequested(DebugDropScaleMarkersRequestedEvent _)
    {
        if (!TryResolveLookTarget(out Vector3 planetCenter, out Vector3 worldUnitDir, out float surfaceRadius,
                out float seaLevelRadius, out float rayDistance, out float cameraDistance,
                out float cameraToAnchorDistance, out float altitudeAboveSurface, out string targetStatus))
            return;

        ClearMarkers();

        Vector3 anchor = planetCenter + worldUnitDir * surfaceRadius;
        ResolveTangentFrame(worldUnitDir, out Vector3 tangentRight, out Vector3 tangentForward);

        Material material = EnsureMaterial();
        Quaternion anchorRotation = Quaternion.LookRotation(tangentForward, worldUnitDir);

        SpawnDropIndicatorSphere(anchor, anchorRotation, material);

        float lateralOffset = 0f;
        int markerProjectionHits = 0;
        int markerProjectionFallbacks = 0;
        for (int i = 0; i < Specs.Length; i++)
        {
            MarkerSpec spec = Specs[i];
            float halfFootprint = spec.LateralFootprint * 0.5f;
            lateralOffset += halfFootprint;

            Vector3 offsetProbe = anchor + tangentRight * lateralOffset;
            if (TryProjectMarkerBaseToVisibleSurface(offsetProbe, worldUnitDir, planetCenter,
                    out Vector3 markerBase, out Vector3 markerUp))
            {
                markerProjectionHits++;
            }
            else if (TrySampleSurfaceAtWorldPoint(offsetProbe, planetCenter, out markerBase, out markerUp))
            {
                markerProjectionFallbacks++;
            }
            else
            {
                markerBase = offsetProbe;
                markerUp = worldUnitDir;
                markerProjectionFallbacks++;
            }

            Vector3 markerForward = Vector3.ProjectOnPlane(tangentForward, markerUp).normalized;
            if (markerForward.sqrMagnitude < 0.001f)
                markerForward = Vector3.Cross(markerUp, tangentRight).normalized;
            if (markerForward.sqrMagnitude < 0.001f)
                markerForward = tangentForward;

            Quaternion markerRotation = Quaternion.LookRotation(markerForward, markerUp);
            Vector3 markerPosition = markerBase + markerUp * (spec.WorldHeight * 0.5f + MarkerSurfaceClearanceMeters);
            SpawnMarker(spec, markerPosition, markerRotation, material);
            lateralOffset += halfFootprint + MarkerSpacingMeters;
        }

        _hasDrop = true;
        _lastDropPlanetCenter = planetCenter;
        _lastDropWorldUp = worldUnitDir;
        _lastDropAnchor = anchor;
        _lastDropTangentForward = tangentForward;
        _debugStats = new ScaleReferenceDebugStats
        {
            HasDrop = true,
            LastDropSucceeded = true,
            LastTargetStatus = targetStatus,
            LastAnchor = anchor,
            LastWorldUp = worldUnitDir,
            LastTangentForward = tangentForward,
            LastSurfaceRadius = surfaceRadius,
            LastSeaLevelRadius = seaLevelRadius,
            LastCameraDistance = cameraDistance,
            LastCameraToAnchorDistance = cameraToAnchorDistance,
            LastAltitudeAboveSurface = altitudeAboveSurface,
            LastRayDistance = rayDistance,
            MarkerCount = _markers.Count,
            MarkerProjectionHits = markerProjectionHits,
            MarkerProjectionFallbacks = markerProjectionFallbacks,
        };

        LoggerProvider.Log(LogLevel.Debug, "ScaleRef",
            $"Dropped {Specs.Length} markers at planet-radial {worldUnitDir:F3}, surface r={surfaceRadius:F2}, " +
            $"ray={rayDistance:F1}m, anchor={anchor:F1}. Diagnostic sphere centered at anchor.");
    }

    void SpawnDropIndicatorSphere(Vector3 anchorWS, Quaternion rotation, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ScaleRef_AnchorIndicator_5m_sphere";
        go.transform.position = anchorWS;
        go.transform.rotation = rotation;
        go.transform.localScale = new Vector3(5f, 5f, 5f);
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            var props = new MaterialPropertyBlock();
            Color magenta = new Color(1f, 0f, 0.85f);
            props.SetColor("_BaseColor", magenta);
            props.SetColor("_Color", magenta);
            renderer.SetPropertyBlock(props);
        }
        _markers.Add(go);
    }

    void OnClearRequested(DebugClearScaleMarkersRequestedEvent _)
    {
        ClearMarkers();
        _hasDrop = false;
        _debugStats = new ScaleReferenceDebugStats
        {
            LastDropSucceeded = false,
            LastTargetStatus = "cleared",
            MarkerCount = 0,
        };
        LoggerProvider.Log(LogLevel.Debug, "ScaleRef", "Cleared scale-reference markers.");
    }

    void OnTeleportRequested(DebugTeleportToScaleMarkersRequestedEvent _)
    {
        if (!_hasDrop)
        {
            LoggerProvider.Log(LogLevel.Warning, "ScaleRef", "No markers dropped; press M near terrain first.");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            LoggerProvider.Log(LogLevel.Warning, "ScaleRef", "No Camera.main to teleport.");
            return;
        }

        const float BackOffMeters = 20f;
        const float UpOffMeters = 5f;
        Vector3 backward = -_lastDropTangentForward;
        Vector3 eyePosition = _lastDropAnchor + backward * BackOffMeters + _lastDropWorldUp * UpOffMeters;
        cam.transform.position = eyePosition;
        cam.transform.rotation = Quaternion.LookRotation(_lastDropAnchor - eyePosition, _lastDropWorldUp);

        LoggerProvider.Log(LogLevel.Debug, "ScaleRef",
            $"Teleported to markers: eye={eyePosition:F1}, look={_lastDropAnchor:F1}, " +
            $"backOffset={BackOffMeters}m, upOffset={UpOffMeters}m.");
    }

    bool TryResolveLookTarget(out Vector3 planetCenter, out Vector3 worldUnitDir, out float surfaceRadius,
        out float seaLevelRadius, out float rayDistance, out float cameraDistance,
        out float cameraToAnchorDistance, out float altitudeAboveSurface, out string targetStatus)
    {
        planetCenter = default;
        worldUnitDir = default;
        surfaceRadius = 0f;
        seaLevelRadius = 0f;
        rayDistance = 0f;
        cameraDistance = 0f;
        cameraToAnchorDistance = 0f;
        altitudeAboveSurface = 0f;
        targetStatus = "none";

        Camera cam = Camera.main;
        if (cam == null)
        {
            RecordFailedDrop("no-camera");
            LoggerProvider.Log(LogLevel.Warning, "ScaleRef", "No Camera.main; cannot drop markers.");
            return false;
        }
        if (!ServiceLocator.TryGet(out IPlanet planet) || planet.Transform == null)
        {
            RecordFailedDrop("no-planet");
            LoggerProvider.Log(LogLevel.Warning, "ScaleRef", "No IPlanet in service locator.");
            return false;
        }
        if (!ServiceLocator.TryGet(out IPlanetSurfaceSampler sampler))
        {
            RecordFailedDrop("no-surface-sampler");
            LoggerProvider.Log(LogLevel.Warning, "ScaleRef", "No IPlanetSurfaceSampler in service locator.");
            return false;
        }

        planetCenter = planet.Transform.position;
        Vector3 origin = cam.transform.position;
        Vector3 forward = cam.transform.forward.normalized;
        cameraDistance = Vector3.Distance(origin, planetCenter);
        seaLevelRadius = planet.LastSeaLevelRadius;
        if (cameraDistance < 0.001f)
        {
            RecordFailedDrop("camera-at-center");
            LoggerProvider.Log(LogLevel.Warning, "ScaleRef", "Camera is at planet center.");
            return false;
        }

        float maxRadius = Mathf.Max(planet.LastGeneratedRadius, planet.LastSeaLevelRadius);
        float maxRayDistance = Mathf.Max(maxRadius * 2.5f, 10000f);
        if (ServiceLocator.TryGet(out IPlanetSurfaceRaycaster raycaster)
            && raycaster.TryRaycastSurface(new Ray(origin, forward), maxRayDistance, out PlanetSurfaceRaycastHit meshHit))
        {
            Vector3 hitDir = meshHit.Point - planetCenter;
            if (hitDir.sqrMagnitude < 0.001f)
            {
                RecordFailedDrop("mesh-hit-degenerate");
                LoggerProvider.Log(LogLevel.Warning, "ScaleRef", "Terrain mesh raycast returned a degenerate hit.");
                return false;
            }

            worldUnitDir = hitDir.normalized;
            surfaceRadius = Mathf.Max(meshHit.SurfaceRadius, hitDir.magnitude);
            rayDistance = meshHit.Distance;
            targetStatus = "mesh-visible-terrain";
        }
        else if (!TryFindSurfaceRayHit(origin, forward, planetCenter, sampler, maxRayDistance,
                out _, out worldUnitDir, out surfaceRadius, out rayDistance, out targetStatus))
        {
            Vector3 radialDir = (origin - planetCenter).normalized;
            if (radialDir.sqrMagnitude < 0.001f
                || !sampler.TryGetSurfaceRadius(radialDir, out surfaceRadius) || surfaceRadius <= 0f)
            {
                RecordFailedDrop("ray-no-hit");
                LoggerProvider.Log(LogLevel.Warning, "ScaleRef", "Camera ray found no terrain hit and radial fallback failed.");
                return false;
            }

            worldUnitDir = radialDir;
            Vector3 fallbackHit = planetCenter + worldUnitDir * surfaceRadius;
            rayDistance = Vector3.Distance(origin, fallbackHit);
            targetStatus = "fallback-camera-radial";
        }

        if (worldUnitDir.sqrMagnitude < 0.001f)
        {
            RecordFailedDrop("degenerate-direction");
            LoggerProvider.Log(LogLevel.Warning, "ScaleRef", "Degenerate look direction.");
            return false;
        }

        Vector3 markerSurfaceWS = planetCenter + worldUnitDir * surfaceRadius;
        cameraToAnchorDistance = Vector3.Distance(origin, markerSurfaceWS);
        altitudeAboveSurface = cameraDistance - surfaceRadius;
        LoggerProvider.Log(LogLevel.Debug, "ScaleRef",
            $"Look target: status={targetStatus}, camDist={cameraDistance:F1}, surfaceR={surfaceRadius:F1}, " +
            $"ray={rayDistance:F1}m, distToMarkers={cameraToAnchorDistance:F1}m, camAltitudeAboveSurface={altitudeAboveSurface:F1}m.");

        return true;
    }

    bool TryFindSurfaceRayHit(Vector3 origin, Vector3 forward, Vector3 planetCenter,
        IPlanetSurfaceSampler sampler, float maxRayDistance,
        out Vector3 hitWorld, out Vector3 worldUnitDir, out float surfaceRadius,
        out float rayDistance, out string status)
    {
        hitWorld = default;
        worldUnitDir = default;
        surfaceRadius = 0f;
        rayDistance = 0f;
        status = "no-hit";

        if (!TrySampleSignedSurfaceHeight(origin, planetCenter, sampler, out float previousHeight, out _, out _))
            return false;

        if (Mathf.Abs(previousHeight) <= RayHitToleranceMeters)
        {
            return ResolveSurfaceHit(origin, planetCenter, sampler, "ray-origin-on-surface",
                out hitWorld, out worldUnitDir, out surfaceRadius, out status);
        }

        float previousT = 0f;
        float t = 0f;
        for (int i = 0; i < RayMarchMaxSteps && t < maxRayDistance; i++)
        {
            float step = Mathf.Clamp(Mathf.Abs(previousHeight) * 0.5f, RayMinStepMeters, RayMaxStepMeters);
            t = Mathf.Min(t + step, maxRayDistance);
            Vector3 point = origin + forward * t;
            if (!TrySampleSignedSurfaceHeight(point, planetCenter, sampler, out float height, out _, out _))
                break;

            if (Mathf.Abs(height) <= RayHitToleranceMeters || CrossedSurface(previousHeight, height))
            {
                float refinedT = RefineSurfaceRayHit(origin, forward, planetCenter, sampler, previousT, t, previousHeight);
                Vector3 refinedPoint = origin + forward * refinedT;
                rayDistance = refinedT;
                string hitStatus = previousHeight > 0f ? "ray-terrain-entry" : "ray-terrain-exit";
                return ResolveSurfaceHit(refinedPoint, planetCenter, sampler, hitStatus,
                    out hitWorld, out worldUnitDir, out surfaceRadius, out status);
            }

            previousT = t;
            previousHeight = height;
        }

        return false;
    }

    static bool CrossedSurface(float previousHeight, float height)
    {
        return (previousHeight > 0f && height <= 0f)
            || (previousHeight < 0f && height >= 0f);
    }

    float RefineSurfaceRayHit(Vector3 origin, Vector3 forward, Vector3 planetCenter,
        IPlanetSurfaceSampler sampler, float loT, float hiT, float loHeight)
    {
        for (int i = 0; i < RayBinaryRefineSteps; i++)
        {
            float midT = (loT + hiT) * 0.5f;
            Vector3 midPoint = origin + forward * midT;
            if (!TrySampleSignedSurfaceHeight(midPoint, planetCenter, sampler, out float midHeight, out _, out _))
                break;

            if (CrossedSurface(loHeight, midHeight) || Mathf.Abs(midHeight) <= RayHitToleranceMeters)
            {
                hiT = midT;
            }
            else
            {
                loT = midT;
                loHeight = midHeight;
            }
        }

        return (loT + hiT) * 0.5f;
    }

    bool ResolveSurfaceHit(Vector3 point, Vector3 planetCenter, IPlanetSurfaceSampler sampler, string hitStatus,
        out Vector3 hitWorld, out Vector3 worldUnitDir, out float surfaceRadius, out string status)
    {
        hitWorld = default;
        worldUnitDir = default;
        surfaceRadius = 0f;
        status = hitStatus;

        Vector3 dir = point - planetCenter;
        if (dir.sqrMagnitude < 0.001f)
            return false;

        worldUnitDir = dir.normalized;
        if (!sampler.TryGetSurfaceRadius(worldUnitDir, out surfaceRadius) || surfaceRadius <= 0f)
            return false;

        hitWorld = planetCenter + worldUnitDir * surfaceRadius;
        return true;
    }

    bool TrySampleSignedSurfaceHeight(Vector3 point, Vector3 planetCenter, IPlanetSurfaceSampler sampler,
        out float signedHeight, out Vector3 worldUnitDir, out float surfaceRadius)
    {
        signedHeight = 0f;
        worldUnitDir = default;
        surfaceRadius = 0f;

        Vector3 dir = point - planetCenter;
        float distance = dir.magnitude;
        if (distance < 0.001f)
            return false;

        worldUnitDir = dir / distance;
        if (!sampler.TryGetSurfaceRadius(worldUnitDir, out surfaceRadius) || surfaceRadius <= 0f)
            return false;

        signedHeight = distance - surfaceRadius;
        return true;
    }

    bool TrySampleSurfaceAtWorldPoint(Vector3 approximateWorld, Vector3 planetCenter,
        out Vector3 surfacePoint, out Vector3 worldUnitDir)
    {
        surfacePoint = default;
        worldUnitDir = default;

        if (!ServiceLocator.TryGet(out IPlanetSurfaceSampler sampler))
            return false;

        Vector3 dir = approximateWorld - planetCenter;
        if (dir.sqrMagnitude < 0.001f)
            return false;

        worldUnitDir = dir.normalized;
        if (!sampler.TryGetSurfaceRadius(worldUnitDir, out float surfaceRadius) || surfaceRadius <= 0f)
            return false;

        surfacePoint = planetCenter + worldUnitDir * surfaceRadius;
        return true;
    }

    bool TryProjectMarkerBaseToVisibleSurface(Vector3 approximateWorld, Vector3 preferredUp, Vector3 planetCenter,
        out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        surfacePoint = default;
        surfaceNormal = default;

        Vector3 up = preferredUp.sqrMagnitude > 0.001f ? preferredUp.normalized : Vector3.up;
        if (!ServiceLocator.TryGet(out IPlanetSurfaceRaycaster raycaster))
            return false;

        Vector3 rayOrigin = approximateWorld + up * MarkerProjectionUpMeters;
        float maxDistance = MarkerProjectionUpMeters + MarkerProjectionDownMeters;
        if (!raycaster.TryRaycastSurface(new Ray(rayOrigin, -up), maxDistance, out PlanetSurfaceRaycastHit hit))
            return false;

        surfacePoint = hit.Point;
        surfaceNormal = hit.Normal.sqrMagnitude > 0.001f
            ? hit.Normal.normalized
            : (surfacePoint - planetCenter).normalized;
        if (surfaceNormal.sqrMagnitude < 0.001f)
            surfaceNormal = up;
        return true;
    }

    void ResolveTangentFrame(Vector3 up, out Vector3 tangentRight, out Vector3 tangentForward)
    {
        Camera cam = Camera.main;
        tangentRight = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, up).normalized : Vector3.zero;
        if (tangentRight.sqrMagnitude < 0.1f)
            tangentRight = Vector3.Cross(Vector3.right, up).normalized;
        if (tangentRight.sqrMagnitude < 0.1f)
            tangentRight = Vector3.Cross(Vector3.forward, up).normalized;

        tangentForward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, up).normalized : Vector3.zero;
        if (tangentForward.sqrMagnitude < 0.1f)
            tangentForward = Vector3.Cross(up, tangentRight).normalized;
        if (tangentForward.sqrMagnitude < 0.1f)
            tangentForward = Vector3.forward;
    }

    void RecordFailedDrop(string status)
    {
        _debugStats = new ScaleReferenceDebugStats
        {
            LastDropSucceeded = false,
            LastTargetStatus = status,
            MarkerCount = _markers.Count,
        };
    }

    void SpawnMarker(MarkerSpec spec, Vector3 position, Quaternion rotation, Material sharedMaterial)
    {
        var go = GameObject.CreatePrimitive(spec.Primitive);
        go.name = spec.Name;
        go.transform.position = position;
        go.transform.rotation = rotation;
        go.transform.localScale = spec.SizeWorld;
        var collider = go.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = sharedMaterial;
            var props = new MaterialPropertyBlock();
            props.SetColor("_BaseColor", spec.Color);
            props.SetColor("_Color", spec.Color);
            renderer.SetPropertyBlock(props);
        }

        _markers.Add(go);
    }

    void ClearMarkers()
    {
        for (int i = 0; i < _markers.Count; i++)
        {
            if (_markers[i] != null) Object.Destroy(_markers[i]);
        }
        _markers.Clear();
    }

    Material EnsureMaterial()
    {
        if (_markerMaterial != null) return _markerMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        _markerMaterial = new Material(shader) { name = "ScaleRefMarker", hideFlags = HideFlags.HideAndDontSave };
        return _markerMaterial;
    }

    public ScaleReferenceDebugStats GetScaleReferenceDebugStats()
    {
        return _debugStats;
    }

    readonly struct MarkerSpec
    {
        public readonly string Name;
        public readonly PrimitiveType Primitive;
        public readonly Vector3 SizeWorld;
        public readonly Color Color;

        public MarkerSpec(string name, PrimitiveType primitive, Vector3 sizeWorld, Color color)
        {
            Name = name;
            Primitive = primitive;
            SizeWorld = sizeWorld;
            Color = color;
        }

        public float WorldHeight
        {
            get
            {
                switch (Primitive)
                {
                    case PrimitiveType.Capsule:
                        return SizeWorld.y * 2f;
                    default:
                        return SizeWorld.y;
                }
            }
        }

        public float LateralFootprint => Mathf.Max(SizeWorld.x, SizeWorld.z)
            * (Primitive == PrimitiveType.Capsule ? 2f : 1f);
    }
}
