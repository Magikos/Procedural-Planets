using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Console commands for the debug grass interactor sphere. Spawns / despawns the
/// debug sphere and tunes its parameters live.
/// </summary>
[CommandPrefix("grass")]
public static class GrassInteractorCommands
{
    const float DefaultForwardDistance = 20f;
    const float DefaultVisualAlpha = 0.22f;
    static DebugGrassInteractor _sphere;
    static CameraFollowGrassInteractor _follow;

    [ConsoleCommand("interactor-spawn", "Spawn a debug grass interactor sphere that follows the camera.")]
    public static string Spawn()
    {
        if (_sphere != null)
            return $"interactor sphere already exists at {_sphere.transform.position}";

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "[DebugGrassInteractor]";
        var existingCollider = go.GetComponent<Collider>();
        if (existingCollider != null) Object.Destroy(existingCollider);
        go.transform.localScale = Vector3.one * 8f;             // visual diameter ~= 2x radius

        var renderer = go.GetComponent<MeshRenderer>();
        Material debugMaterial = null;
        if (renderer != null)
        {
            debugMaterial = CreateTransparentDebugMaterial();
            if (debugMaterial != null)
                renderer.sharedMaterial = debugMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        _sphere = go.AddComponent<DebugGrassInteractor>();
        _sphere.radius = 4f;
        _sphere.strength = 1f;
        _sphere.releaseSeconds = 0.5f;
        _sphere.OwnDebugMaterial(debugMaterial);

        _follow = go.AddComponent<CameraFollowGrassInteractor>();
        _follow.SetForwardDistance(DefaultForwardDistance);
        return "interactor sphere spawned 20m ahead (camera-follow on). Hold [ / ] to pull / push.";
    }

    [ConsoleCommand("interactor-despawn", "Remove the debug grass interactor sphere.")]
    public static string Despawn()
    {
        if (_sphere == null) return "no interactor sphere exists";
        Object.Destroy(_sphere.gameObject);
        _sphere = null;
        _follow = null;
        return "interactor sphere despawned";
    }

    [ConsoleCommand("interactor-radius", "Get or set the bend radius in meters.")]
    public static string Radius(float? value = null)
    {
        if (_sphere == null) return "no interactor sphere — run 'grass.interactor-spawn' first";
        if (value.HasValue)
        {
            _sphere.radius = Mathf.Clamp(value.Value, 0.1f, 50f);
            _sphere.transform.localScale = Vector3.one * (_sphere.radius * 2f);
        }
        return $"interactor radius: {_sphere.radius:F2} m";
    }

    [ConsoleCommand("interactor-strength", "Get or set the bend strength (0-2).")]
    public static string Strength(float? value = null)
    {
        if (_sphere == null) return "no interactor sphere — run 'grass.interactor-spawn' first";
        if (value.HasValue)
            _sphere.strength = Mathf.Clamp(value.Value, 0f, 2f);
        return $"interactor strength: {_sphere.strength:F2}";
    }

    [ConsoleCommand("interactor-release", "Get or set grass recovery time in seconds.")]
    public static string Release(float? value = null)
    {
        if (_sphere == null) return MissingSphereMessage();
        if (value.HasValue)
            _sphere.releaseSeconds = Mathf.Clamp(value.Value, 0.05f, 5f);
        return $"interactor release: {_sphere.releaseSeconds:F2} s";
    }

    [ConsoleCommand("interactor-distance", "Get or set camera-follow distance in meters.")]
    public static string Distance(float? value = null)
    {
        if (_follow == null) return MissingSphereMessage();
        if (value.HasValue)
            _follow.SetForwardDistance(value.Value);
        return $"interactor distance: {_follow.forwardDistance:F2} m";
    }

    [ConsoleCommand("interactor-push", "Push the interactor away from the camera. Default 5m.")]
    public static string Push(float? meters = null)
    {
        if (_follow == null) return MissingSphereMessage();
        _follow.AdjustForwardDistance(Mathf.Abs(meters ?? 5f));
        return $"interactor distance: {_follow.forwardDistance:F2} m";
    }

    [ConsoleCommand("interactor-pull", "Pull the interactor toward the camera. Default 5m.")]
    public static string Pull(float? meters = null)
    {
        if (_follow == null) return MissingSphereMessage();
        _follow.AdjustForwardDistance(-Mathf.Abs(meters ?? 5f));
        return $"interactor distance: {_follow.forwardDistance:F2} m";
    }

    [ConsoleCommand("interactor-follow", "Toggle camera-follow on the interactor sphere. False stops following; sphere stays in place.")]
    public static string Follow(bool? value = null)
    {
        if (_follow == null) return "no interactor sphere — run 'grass.interactor-spawn' first";
        if (value.HasValue) _follow.enabled = value.Value;
        return $"interactor camera-follow: {_follow.enabled}";
    }

    [ConsoleCommand("interactor-status", "Show interactor count and parameters.")]
    public static string Status()
    {
        var sb = new StringBuilder();
        sb.Append("registered=").Append(GrassInteractorRegistry.RegisteredCount);
        sb.Append(", lastUploaded=").Append(GrassInteractorRegistry.LastActiveCount);
        sb.Append(", activeSources=").Append(GrassInteractorRegistry.LastActiveSourceCount);
        sb.Append(", releaseGpu=").Append(GrassInteractorRegistry.LastReleaseSampleCount);
        sb.Append(", releaseRetained=").Append(GrassInteractorRegistry.RetainedReleaseSampleCount);
        sb.Append(", cap=").Append(GrassInteractorRegistry.MaxInteractors);
        if (_sphere != null)
        {
            sb.Append("\n  debugSphere: pos=").Append(_sphere.transform.position);
            sb.Append(", radius=").Append(_sphere.radius.ToString("F2"));
            sb.Append(", strength=").Append(_sphere.strength.ToString("F2"));
            sb.Append(", release=").Append(_sphere.releaseSeconds.ToString("F2")).Append("s");
            sb.Append(", follow=").Append(_follow != null && _follow.enabled);
            if (_follow != null)
                sb.Append(", distance=").Append(_follow.forwardDistance.ToString("F2")).Append("m");
        }
        sb.Append("\n  controls: [ pull, ] push, Shift = 3x");
        return sb.ToString();
    }

    static string MissingSphereMessage()
    {
        return "no interactor sphere - run 'grass.interactor-spawn' first";
    }

    static Material CreateTransparentDebugMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (shader == null)
            return null;

        Color color = new Color(1f, 0.15f, 0.8f, DefaultVisualAlpha);
        var material = new Material(shader)
        {
            name = "Runtime Grass Interactor Debug",
            renderQueue = (int)RenderQueue.Transparent,
        };
        material.SetOverrideTag("RenderType", "Transparent");
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetShaderPassEnabled("ShadowCaster", false);
        return material;
    }
}
