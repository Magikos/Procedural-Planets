using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Client presentation only. Hearing stimuli belong to the authority simulation.</summary>
public sealed class CreatureAudioPlayback : IDisposable
{
    public const int VoiceLimit = 8;
    readonly Transform _parent;
    readonly AudioSource[] _voices = new AudioSource[VoiceLimit];
    readonly ulong[] _owners = new ulong[VoiceLimit];
    readonly double[] _busyUntil = new double[VoiceLimit];
    readonly Dictionary<ulong, State> _states = new();

    sealed class State
    {
        public CreatureBehaviour Behaviour;
        public float Call, Alert, Attack, Feed;
        public uint Random;
    }

    public CreatureAudioPlayback(Transform parent) => _parent = parent;
    public int TrackedCount => _states.Count;
    public int PlayedCount { get; private set; }
    public int DroppedCount { get; private set; }

    public void Tick(ulong id, Transform body, CreatureAudioDto settings, CreatureBehaviour behaviour,
        Vector3 listenerPosition, float deltaTime)
    {
        if (settings == null || body == null || !float.IsFinite(deltaTime) || deltaTime <= 0) return;
        if (!_states.TryGetValue(id, out State state))
        {
            state = new State { Behaviour = behaviour, Random = unchecked((uint)(id ^ (id >> 32))) | 1u };
            state.Call = Interval(state, settings);
            _states.Add(id, state);
        }
        state.Call -= deltaTime;
        state.Alert -= deltaTime;
        state.Attack -= deltaTime;
        state.Feed -= deltaTime;
        bool changed = state.Behaviour != behaviour;
        state.Behaviour = behaviour;
        bool audible = body.gameObject.activeInHierarchy && Finite(listenerPosition)
            && (body.position - listenerPosition).sqrMagnitude <= settings.AudibleDistance * settings.AudibleDistance;
        double now = AudioSettings.dspTime;
        for (int i = 0; i < VoiceLimit; i++)
        {
            if (_owners[i] != id || _voices[i] == null) continue;
            _voices[i].transform.position = body.position;
            if (!audible) { _voices[i].Stop(); _busyUntil[i] = 0; }
        }

        if (changed && (behaviour == CreatureBehaviour.Alert || behaviour == CreatureBehaviour.Flee
            || behaviour == CreatureBehaviour.Threaten || behaviour == CreatureBehaviour.Defend) && state.Alert <= 0)
        {
            if (audible) Play(id, body.position, settings, settings.Alerts, state, now);
            state.Alert = settings.AlertCooldownSeconds;
        }
        else if (changed && behaviour == CreatureBehaviour.Attack && state.Attack <= 0)
        {
            if (audible) Play(id, body.position, settings, settings.Attacks, state, now);
            state.Attack = settings.AttackCooldownSeconds;
        }
        else if (behaviour == CreatureBehaviour.Feed && state.Feed <= 0)
        {
            if (audible) Play(id, body.position, settings, settings.Feeding, state, now);
            state.Feed = settings.FeedingIntervalSeconds;
        }
        else if (state.Call <= 0)
        {
            // Stalking, fleeing, eating and sleeping suppress ambient vocalizations.
            if (audible && (behaviour == CreatureBehaviour.Wander || behaviour == CreatureBehaviour.Rest
                || behaviour == CreatureBehaviour.Perch || behaviour == CreatureBehaviour.Gather))
                Play(id, body.position, settings, settings.Calls, state, now);
            state.Call = Interval(state, settings);
        }
    }

    void Play(ulong id, Vector3 position, CreatureAudioDto settings, IReadOnlyList<AudioClip> clips, State state, double now)
    {
        if (clips == null || clips.Count == 0 || settings.Volume <= 0 || !Finite(position)) return;
        AudioClip clip = clips[(int)(Next(state) % (uint)clips.Count)];
        if (clip == null) return;
        int free = -1;
        for (int i = 0; i < VoiceLimit; i++)
        {
            if (_busyUntil[i] > now && _owners[i] == id) return;
            if (free < 0 && _busyUntil[i] <= now) free = i;
        }
        if (free < 0) { DroppedCount++; return; }
        AudioSource voice = _voices[free];
        if (voice == null)
        {
            var host = new GameObject("Creature voice");
            host.transform.SetParent(_parent, false);
            voice = host.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 1;
            voice.dopplerLevel = 0;
            voice.rolloffMode = AudioRolloffMode.Logarithmic;
            _voices[free] = voice;
        }
        voice.transform.position = position;
        voice.minDistance = settings.NearDistance;
        voice.maxDistance = settings.AudibleDistance;
        voice.volume = settings.Volume;
        voice.pitch = .97f + Next(state) % 601 / 10000f;
        voice.clip = clip;
        voice.Play();
        _owners[free] = id;
        _busyUntil[free] = now + clip.length / voice.pitch;
        PlayedCount++;
    }

    static uint Next(State state)
    {
        uint value = state.Random;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        return state.Random = value;
    }
    static float Interval(State state, CreatureAudioDto settings) =>
        Mathf.Lerp(settings.MinimumCallSeconds, settings.MaximumCallSeconds, (Next(state) & 65535) / 65535f);
    static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    public void Forget(ulong id)
    {
        _states.Remove(id);
        for (int i = 0; i < VoiceLimit; i++)
            if (_owners[i] == id) { if (_voices[i] != null) _voices[i].Stop(); _busyUntil[i] = 0; }
    }

    public void Dispose()
    {
        for (int i = 0; i < VoiceLimit; i++)
        {
            if (_voices[i] != null) UnityEngine.Object.Destroy(_voices[i].gameObject);
            _voices[i] = null;
            _busyUntil[i] = 0;
        }
        _states.Clear();
    }
}
