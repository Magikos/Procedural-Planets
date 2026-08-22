using UnityEngine;

// Bursts leaf + bark particles when a tree is felled (subscribes to ScatterHarvestedEvent, the FX seam from
// plan 005 §3b) — sells the chop and helps mask the standing-tree -> stump + falling-log swap. A procedural
// one-shot ParticleSystem oriented to the surface up; it self-destroys when done. Dig events (ProtoIndex < 0)
// are skipped (they would want dirt, not leaves).
public sealed class ChopFxSystem : System.IDisposable
{
    static readonly Color LeafGreen = new Color(0.30f, 0.48f, 0.16f);
    static readonly Color BarkBrown = new Color(0.34f, 0.22f, 0.12f);

    readonly Transform _planetTransform;
    Material _particleMat;

    public ChopFxSystem(Transform planetTransform)
    {
        _planetTransform = planetTransform;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        if (shader != null)
            _particleMat = new Material(shader) { name = "Chop FX", hideFlags = HideFlags.HideAndDontSave };
        EventBus<ScatterHarvestedEvent>.Listen(OnHarvested);
    }

    void OnHarvested(ScatterHarvestedEvent e)
    {
        if (e.ProtoIndex < 0 || _particleMat == null) return; // fell only

        Vector3 up = e.WorldPos - _planetTransform.position;
        up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;

        var go = new GameObject("Chop FX");
        go.transform.SetPositionAndRotation(e.WorldPos + up * 0.6f, Quaternion.FromToRotation(Vector3.up, up));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.4f;
        main.loop = false;
        main.startLifetime = 1.3f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.22f);
        main.startColor = new ParticleSystem.MinMaxGradient(LeafGreen, BarkBrown); // random per particle
        main.gravityModifier = 0.6f;
        main.maxParticles = 60;
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 34) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.35f;

        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f); // tumble a little

        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.material = _particleMat;
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psr.receiveShadows = false;

        ps.Play();
    }

    public void Dispose()
    {
        EventBus<ScatterHarvestedEvent>.Unlisten(OnHarvested);
        if (_particleMat != null)
        {
            if (Application.isPlaying) Object.Destroy(_particleMat);
            else Object.DestroyImmediate(_particleMat);
            _particleMat = null;
        }
    }
}
