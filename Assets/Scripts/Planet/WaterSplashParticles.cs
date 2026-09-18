using System;
using UnityEngine;

public sealed class WaterSplashParticles : IDisposable
{
    const int Capacity = 256;
    readonly ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[Capacity];
    ParticleSystem _system;
    Material _material;
    readonly AudioSource[] _voices = new AudioSource[4];
    AudioClip _splash;
    int _voice;

    public WaterSplashParticles(Transform parent)
    {
        var host = new GameObject("Water splashes");
        host.transform.SetParent(parent, false);
        _system = host.AddComponent<ParticleSystem>();
        _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = _system.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Capacity;
        main.gravityModifier = 0f;
        var emission = _system.emission;
        emission.enabled = false;
        var shape = _system.shape;
        shape.enabled = false;
        var color = _system.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(.8f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var renderer = _system.GetComponent<ParticleSystemRenderer>();
        Shader shader = Resources.Load<Shader>("WaterSplash");
        if (shader != null)
        {
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            renderer.sharedMaterial = _material;
        }
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _splash = CreateSplashAudio();
        for (int i = 0; i < _voices.Length; i++)
        {
            var voice = new GameObject("Water splash voice");
            voice.transform.SetParent(parent, false);
            _voices[i] = voice.AddComponent<AudioSource>();
            _voices[i].playOnAwake = false;
            _voices[i].spatialBlend = 1f;
            _voices[i].minDistance = 2f;
            _voices[i].maxDistance = 45f;
            _voices[i].dopplerLevel = 0f;
            _voices[i].clip = _splash;
        }
        _system.Play();
    }

    public void Emit(WaterImpulse impulse)
    {
        if (_system == null || !impulse.Splash) return;
        Vector3 tangent = CharacterMath.ArbitraryTangent(impulse.Normal);
        Vector3 across = Vector3.Cross(impulse.Normal, tangent);
        int count = Mathf.RoundToInt(Mathf.Lerp(8f, 28f, impulse.Strength));
        for (int i = 0; i < count; i++)
        {
            float angle = i * 2.399963f;
            Vector3 radial = tangent * Mathf.Cos(angle) + across * Mathf.Sin(angle);
            var emit = new ParticleSystem.EmitParams
            {
                position = impulse.Position + radial * impulse.Radius * .6f,
                velocity = radial * Mathf.Lerp(.7f, 2.6f, impulse.Strength)
                    + impulse.Normal * Mathf.Lerp(1f, 4f, impulse.Strength) * (.6f + (i % 5) * .1f),
                startLifetime = .35f + (i % 7) * .06f,
                startSize = .035f + impulse.Strength * .055f,
                startColor = new Color(.82f, .94f, 1f, .75f)
            };
            _system.Emit(emit, 1);
        }
        AudioSource voice = _voices[_voice++ % _voices.Length];
        voice.transform.position = impulse.Position;
        voice.volume = .3f * impulse.Strength;
        voice.Play();
    }

    public void Tick(Vector3 center, float deltaTime)
    {
        if (_system == null) return;
        int count = _system.GetParticles(_particles);
        for (int i = 0; i < count; i++)
            _particles[i].velocity -= (_particles[i].position - center).normalized * (9.81f * deltaTime);
        _system.SetParticles(_particles, count);
    }

    static AudioClip CreateSplashAudio()
    {
        const int rate = 22050;
        var data = new float[rate / 2];
        var random = new System.Random(9147);
        float filtered = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)rate;
            filtered += .22f * ((float)random.NextDouble() * 2f - 1f - filtered);
            data[i] = filtered * Mathf.Exp(-t * 11f) * Mathf.Min(t * 180f, 1f);
        }
        AudioClip clip = AudioClip.Create("Procedural water splash", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    public void Dispose()
    {
        if (_system != null) UnityEngine.Object.Destroy(_system.gameObject);
        if (_material != null) UnityEngine.Object.Destroy(_material);
        foreach (var voice in _voices) if (voice != null) UnityEngine.Object.Destroy(voice.gameObject);
        if (_splash != null) UnityEngine.Object.Destroy(_splash);
    }
}
