using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

public struct FishThreatSnapshot
{
    public Vector3 Position;
    public bool Found;
}

public struct FishJobSchool
{
    public FishSchoolState State;
    public int Offset, Count;
}

[BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
public struct FishSimulationJob : IJobParallelFor
{
    public NativeArray<FishJobSchool> Schools;
    // Each school owns a disjoint contiguous range. School-internal updates remain ordered.
    [NativeDisableParallelForRestriction] public NativeArray<FishAgentState> Agents;
    [ReadOnly] public NativeArray<FishThreatSnapshot> Threats;
    public WaterQueryJobData Water;
    public float DeltaTime;

    public void Execute(int index)
    {
        var school = Schools[index];
        FishSimulation.Tick(ref school.State,
            new Buffer { Agents = Agents, Offset = school.Offset, Count = school.Count }, Water,
            new ThreatQuery { Threats = Threats, Offset = school.Offset }, DeltaTime,
            // Radial subtraction at planet scale can differ by a few float rounding units across backends.
            4f * 1.192092896e-7f * Water.PlanetRadius * Water.WorldScale);
        Schools[index] = school;
    }
    struct Buffer : IFishStateBuffer
    {
        public NativeArray<FishAgentState> Agents;
        public int Offset;
        public int Count { get; set; }
        public FishAgentState this[int index] { get => Agents[Offset + index]; set => Agents[Offset + index] = value; }
    }
    struct ThreatQuery : IFishThreatQuery
    {
        public NativeArray<FishThreatSnapshot> Threats;
        public int Offset;
        public bool TryFind(int index, Vector3 position, out Vector3 threat)
        { var value = Threats[Offset + index]; threat = value.Position; return value.Found; }
    }
}

public sealed class FishSimulationJobs : IDisposable
{
    readonly WaterQueryService _water;
    readonly List<FishSchool> _owners = new();
    WaterQueryJobSnapshot _snapshot;
    NativeArray<FishJobSchool> _schools;
    NativeArray<FishAgentState> _agents;
    NativeArray<FishThreatSnapshot> _threats;
    JobHandle _handle;
    bool _pending;
    int _version = -1;
    public bool Pending => _pending;
    public int CompletedBatches { get; private set; }

    public FishSimulationJobs(WaterQueryService water) => _water = water;

    public bool TryComplete()
    {
        if (!_pending) return true;
        if (!_handle.IsCompleted) return false;
        _handle.Complete(); _pending = false;
        if (_version != _water.Version) return true;
        for (int i = 0; i < _owners.Count; i++)
        {
            var data = _schools[i];
            _owners[i].State = data.State;
            NativeArray<FishAgentState>.Copy(_agents, data.Offset, _owners[i].Agents, 0, data.Count);
        }
        CompletedBatches++;
        return true;
    }

    public bool Prepare()
    {
        if (_pending) return false;
        if (_version == _water.Version && _snapshot != null) return true;
        _snapshot?.Dispose(); _snapshot = null;
        _version = _water.Version;
        return _water.TryCreateJobSnapshot(out _snapshot);
    }

    public void Schedule(IReadOnlyList<FishPopulation.Group> groups, ThreatRegistry registry, long now, float dt)
    {
        if (_pending) throw new InvalidOperationException("Fish simulation already has a pending batch.");
        if (groups.Count == 0 || dt <= 0f) return;
        int count = 0;
        foreach (var group in groups) count += group.School.Agents.Length;
        EnsureCapacity(ref _schools, groups.Count);
        EnsureCapacity(ref _agents, count);
        EnsureCapacity(ref _threats, count);
        _owners.Clear();
        int offset = 0;
        for (int i = 0; i < groups.Count; i++)
        {
            var school = groups[i].School;
            _owners.Add(school);
            _schools[i] = new FishJobSchool { State = school.State, Offset = offset, Count = school.Agents.Length };
            NativeArray<FishAgentState>.Copy(school.Agents, 0, _agents, offset, school.Agents.Length);
            var query = school.CaptureThreatQuery(registry, now);
            for (int j = 0; j < school.Agents.Length; j++)
            {
                bool found = query.TryFind(j, school.Agents[j].Position, out Vector3 threat);
                _threats[offset + j] = new FishThreatSnapshot { Found = found, Position = threat };
            }
            offset += school.Agents.Length;
        }
        _water.CaptureJobTransform(_snapshot);
        _handle = new FishSimulationJob
        {
            Schools = _schools, Agents = _agents, Threats = _threats,
            Water = _snapshot.Data, DeltaTime = dt
        }.Schedule(groups.Count, 1);
        _pending = true;
        _water.RegisterReader(_handle);
        JobHandle.ScheduleBatchedJobs();
    }

    static void EnsureCapacity<T>(ref NativeArray<T> array, int count) where T : struct
    {
        if (array.IsCreated && array.Length >= count) return;
        if (array.IsCreated) array.Dispose();
        array = new NativeArray<T>(Mathf.NextPowerOfTwo(count), Allocator.Persistent);
    }

    public void Dispose()
    {
        if (_pending) { _handle.Complete(); _pending = false; }
        if (_schools.IsCreated) _schools.Dispose();
        if (_agents.IsCreated) _agents.Dispose();
        if (_threats.IsCreated) _threats.Dispose();
        _snapshot?.Dispose(); _snapshot = null;
        _owners.Clear();
    }
}
