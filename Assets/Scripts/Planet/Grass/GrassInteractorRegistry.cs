using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Packs active grass interactors and a bounded set of fading release samples into
/// the global GPU buffer consumed by the grass shaders.
/// </summary>
public static class GrassInteractorRegistry
{
    public const int MaxInteractors = 8;
    const int MaxReleaseSamples = 32;
    const float TrailSampleInterval = 0.12f;
    const float TrailMinimumMovement = 0.15f;

    static readonly int InteractorsBufferId = Shader.PropertyToID("_GrassInteractors");
    static readonly int InteractorCountId = Shader.PropertyToID("_GrassInteractorCount");

    static readonly List<IGrassInteractor> InteractorsList = new();
    static readonly GrassInteractorGpu[] CpuBuffer = new GrassInteractorGpu[MaxInteractors];
    static readonly Dictionary<IGrassInteractor, SourceState> SourceStates = new();
    static readonly List<ReleaseSample> ReleaseSamples = new(MaxReleaseSamples);
    static readonly List<IGrassInteractor> StaleSources = new();

    static ComputeBuffer _gpuBuffer;
    static int _lastActiveCount;
    static int _lastActiveSourceCount;
    static int _lastReleaseSampleCount;

    sealed class SourceState
    {
        public Vector3 LastSamplePosition;
        public float LastSampleTime;
        public float Radius;
        public float Strength;
        public float ReleaseSeconds;
        public bool WasActive;
    }

    readonly struct ReleaseSample
    {
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly float Strength;
        public readonly float CreatedAt;
        public readonly float Lifetime;

        public ReleaseSample(
            Vector3 position,
            float radius,
            float strength,
            float createdAt,
            float lifetime)
        {
            Position = position;
            Radius = radius;
            Strength = strength;
            CreatedAt = createdAt;
            Lifetime = lifetime;
        }
    }

    public static void Initialize()
    {
        EnsureBuffer();
        Shader.SetGlobalBuffer(InteractorsBufferId, _gpuBuffer);
        Shader.SetGlobalInt(InteractorCountId, 0);
        SourceStates.Clear();
        ReleaseSamples.Clear();
        _lastActiveCount = 0;
        _lastActiveSourceCount = 0;
        _lastReleaseSampleCount = 0;
    }

    public static int RegisteredCount => InteractorsList.Count;
    public static int LastActiveCount => _lastActiveCount;
    public static int LastActiveSourceCount => _lastActiveSourceCount;
    public static int LastReleaseSampleCount => _lastReleaseSampleCount;
    public static int RetainedReleaseSampleCount => ReleaseSamples.Count;
    public static IReadOnlyList<IGrassInteractor> Interactors => InteractorsList;

    public static void Register(IGrassInteractor interactor)
    {
        if (IsMissing(interactor) || InteractorsList.Contains(interactor))
            return;

        InteractorsList.Add(interactor);
        GrassInteractorSnapshot snap = GrassInteractorSnapshot.From(interactor);
        SourceStates[interactor] = new SourceState
        {
            LastSamplePosition = snap.WorldPosition,
            LastSampleTime = Time.unscaledTime,
            Radius = snap.Radius,
            Strength = snap.Strength,
            ReleaseSeconds = snap.ReleaseSeconds,
            WasActive = interactor.IsActive,
        };
    }

    public static void Unregister(IGrassInteractor interactor)
    {
        if (interactor == null)
            return;

        if (SourceStates.TryGetValue(interactor, out SourceState state) && state.WasActive)
        {
            AddReleaseSample(
                state.LastSamplePosition,
                state.Radius,
                state.Strength,
                state.ReleaseSeconds,
                Time.unscaledTime);
        }

        InteractorsList.Remove(interactor);
        SourceStates.Remove(interactor);
    }

    internal static void UploadPerFrame()
    {
        EnsureBuffer();
        float now = Time.unscaledTime;
        RemoveExpiredReleaseSamples(now);

        int gpuCount = 0;
        int activeSources = 0;
        StaleSources.Clear();

        for (int i = 0; i < InteractorsList.Count; i++)
        {
            IGrassInteractor source = InteractorsList[i];
            if (IsMissing(source))
            {
                StaleSources.Add(source);
                continue;
            }

            if (!SourceStates.TryGetValue(source, out SourceState state))
            {
                GrassInteractorSnapshot initial = GrassInteractorSnapshot.From(source);
                state = new SourceState
                {
                    LastSamplePosition = initial.WorldPosition,
                    LastSampleTime = now,
                    Radius = initial.Radius,
                    Strength = initial.Strength,
                    ReleaseSeconds = initial.ReleaseSeconds,
                };
                SourceStates[source] = state;
            }

            if (!source.IsActive)
            {
                if (state.WasActive)
                {
                    AddReleaseSample(
                        state.LastSamplePosition,
                        state.Radius,
                        state.Strength,
                        state.ReleaseSeconds,
                        now);
                    state.WasActive = false;
                }
                continue;
            }

            GrassInteractorSnapshot snap = GrassInteractorSnapshot.From(source);
            UpdateReleaseTrail(state, snap, now);
            activeSources++;

            if (gpuCount < MaxInteractors)
                PackGpuSlot(ref gpuCount, snap.WorldPosition, snap.Radius, snap.Strength);
        }

        for (int i = 0; i < StaleSources.Count; i++)
        {
            IGrassInteractor stale = StaleSources[i];
            InteractorsList.Remove(stale);
            SourceStates.Remove(stale);
        }

        int releaseSamples = 0;
        for (int i = ReleaseSamples.Count - 1; i >= 0 && gpuCount < MaxInteractors; i--)
        {
            ReleaseSample sample = ReleaseSamples[i];
            float age01 = Mathf.Clamp01((now - sample.CreatedAt) / sample.Lifetime);
            float strength = sample.Strength * (1f - Mathf.SmoothStep(0f, 1f, age01));
            if (strength <= 0.001f)
                continue;

            PackGpuSlot(ref gpuCount, sample.Position, sample.Radius, strength);
            releaseSamples++;
        }

        if (gpuCount > 0)
            _gpuBuffer.SetData(CpuBuffer, 0, 0, gpuCount);

        Shader.SetGlobalBuffer(InteractorsBufferId, _gpuBuffer);
        Shader.SetGlobalInt(InteractorCountId, gpuCount);
        _lastActiveCount = gpuCount;
        _lastActiveSourceCount = activeSources;
        _lastReleaseSampleCount = releaseSamples;
    }

    internal static void DisposeBuffer()
    {
        if (_gpuBuffer != null)
        {
            _gpuBuffer.Release();
            _gpuBuffer = null;
        }

        Shader.SetGlobalInt(InteractorCountId, 0);
        SourceStates.Clear();
        ReleaseSamples.Clear();
        _lastActiveCount = 0;
        _lastActiveSourceCount = 0;
        _lastReleaseSampleCount = 0;
    }

    static void EnsureBuffer()
    {
        if (_gpuBuffer != null)
            return;

        // 32 bytes per element (two float4s) matches the HLSL struct.
        _gpuBuffer = new ComputeBuffer(MaxInteractors, sizeof(float) * 8);
    }

    static void UpdateReleaseTrail(
        SourceState state,
        GrassInteractorSnapshot snap,
        float now)
    {
        if (state.WasActive)
        {
            float moved = Vector3.Distance(state.LastSamplePosition, snap.WorldPosition);
            if (moved >= TrailMinimumMovement
                && now - state.LastSampleTime >= TrailSampleInterval)
            {
                AddReleaseSample(
                    state.LastSamplePosition,
                    state.Radius,
                    state.Strength,
                    state.ReleaseSeconds,
                    now);
                state.LastSamplePosition = snap.WorldPosition;
                state.LastSampleTime = now;
            }
        }
        else
        {
            state.LastSamplePosition = snap.WorldPosition;
            state.LastSampleTime = now;
        }

        state.Radius = snap.Radius;
        state.Strength = snap.Strength;
        state.ReleaseSeconds = snap.ReleaseSeconds;
        state.WasActive = true;
    }

    static void AddReleaseSample(
        Vector3 position,
        float radius,
        float strength,
        float releaseSeconds,
        float now)
    {
        if (radius <= 0f || strength <= 0f || releaseSeconds <= 0f)
            return;

        if (ReleaseSamples.Count >= MaxReleaseSamples)
            ReleaseSamples.RemoveAt(0);

        ReleaseSamples.Add(new ReleaseSample(
            position,
            radius,
            strength,
            now,
            Mathf.Max(releaseSeconds, 0.05f)));
    }

    static void RemoveExpiredReleaseSamples(float now)
    {
        for (int i = ReleaseSamples.Count - 1; i >= 0; i--)
        {
            ReleaseSample sample = ReleaseSamples[i];
            if (now - sample.CreatedAt >= sample.Lifetime)
                ReleaseSamples.RemoveAt(i);
        }
    }

    static void PackGpuSlot(
        ref int index,
        Vector3 position,
        float radius,
        float strength)
    {
        CpuBuffer[index] = new GrassInteractorGpu
        {
            PositionRadius = new Vector4(position.x, position.y, position.z, radius),
            StrengthType = new Vector4(strength, 0f, 0f, 0f),
        };
        index++;
    }

    static bool IsMissing(IGrassInteractor source)
    {
        if (source == null)
            return true;

        return source is Object unityObject && unityObject == null;
    }
}
