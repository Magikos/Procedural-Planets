using System;
using UnityEngine;

sealed class WeatherThunderPlayback : IDisposable
{
    const int VoiceCount = 4;
    const float SoundSpeed = 343f;
    const float AudibleDistance = 10000f;
    readonly AudioSource[] _voices = new AudioSource[VoiceCount];
    readonly double[] _busyUntil = new double[VoiceCount];
    readonly AudioClip[] _clips;
    AudioListener _listener;
    Vector3 _planetCenter;
    float _planetRadius;
    int _clipIndex;

    public WeatherThunderPlayback()
    {
        _clips = Resources.LoadAll<AudioClip>("Weather/Thunder");
        Array.Sort(_clips, (a, b) => string.CompareOrdinal(a.name, b.name));
        EventBus<WeatherLightningEvent>.Listen(OnLightning);
    }

    public void Reset(Vector3 center, float radius)
    {
        _planetCenter = center;
        _planetRadius = radius;
        _clipIndex = 0;
        for (int i = 0; i < VoiceCount; i++)
        {
            if (_voices[i] != null) _voices[i].Stop();
            _busyUntil[i] = 0;
        }
        _listener = UnityEngine.Object.FindAnyObjectByType<AudioListener>();
    }

    void OnLightning(WeatherLightningEvent evt)
    {
        if (_clips.Length == 0 || !Finite(evt.WorldPosition) || !float.IsFinite(evt.Intensity))
            return;
        if (_listener == null || !_listener.isActiveAndEnabled)
            _listener = UnityEngine.Object.FindAnyObjectByType<AudioListener>();
        if (_listener == null || !_listener.isActiveAndEnabled)
            return;

        Vector3 listener = _listener.transform.position;
        Vector3 segment = evt.WorldPosition - listener;
        float distance = segment.magnitude;
        if (distance > AudibleDistance || evt.Intensity <= 0f)
            return;
        // A straight sound path through the planet must not sound like a nearby storm.
        float t = Mathf.Clamp01(Vector3.Dot(_planetCenter - listener, segment)
            / Mathf.Max(segment.sqrMagnitude, 0.0001f));
        if (_planetRadius > 0f && (listener + segment * t - _planetCenter).sqrMagnitude
            < _planetRadius * _planetRadius)
            return;

        double now = AudioSettings.dspTime;
        for (int i = 0; i < VoiceCount; i++)
        {
            if (_busyUntil[i] > now) continue;
            AudioSource voice = _voices[i];
            if (voice == null)
            {
                var host = new GameObject("Weather thunder") { hideFlags = HideFlags.HideAndDontSave };
                voice = host.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 1f;
                voice.dopplerLevel = 0f;
                voice.rolloffMode = AudioRolloffMode.Logarithmic;
                voice.minDistance = 100f;
                voice.maxDistance = AudibleDistance;
                _voices[i] = voice;
            }
            AudioClip clip = _clips[_clipIndex++ % _clips.Length];
            voice.transform.position = evt.WorldPosition;
            voice.clip = clip;
            voice.volume = Mathf.Clamp01(evt.Intensity);
            double arrival = now + distance / SoundSpeed;
            voice.PlayScheduled(arrival);
            _busyUntil[i] = arrival + clip.length;
            return;
        }
    }

    static bool Finite(Vector3 point) =>
        float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);

    public void Dispose()
    {
        EventBus<WeatherLightningEvent>.Unlisten(OnLightning);
        foreach (AudioSource voice in _voices)
        {
            if (voice == null) continue;
            voice.Stop();
            UnityEngine.Object.Destroy(voice.gameObject);
        }
    }
}
