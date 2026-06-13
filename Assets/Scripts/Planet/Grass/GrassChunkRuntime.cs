using UnityEngine;
using UnityEngine.Rendering;

// Per-chunk GPU resources for the placement renderer: the blade instance buffer, the indirect-draw
// args, the placement stats buffer, and the readback state. One instance per resident chunk; created
// and disposed by GrassPlacementController.
sealed class GrassChunkRuntime : System.IDisposable
{
    public const int StatsCount = 16;

    const int BladeStride = sizeof(float) * 12;
    static readonly uint[] ArgsScratch = new uint[4];
    static readonly uint[] StatsScratch = new uint[StatsCount];

    readonly GraphicsBuffer _bladeBuffer;
    readonly GraphicsBuffer _argsBuffer;
    readonly GraphicsBuffer _statsBuffer;
    readonly MaterialPropertyBlock _props;
    readonly Bounds _worldBounds;
    readonly uint[] _stats = new uint[StatsCount];
    bool _disposed;
    int _readbackInstanceCount;
    bool _hasStats;

    public GraphicsBuffer BladeBuffer => _bladeBuffer;
    public GraphicsBuffer ArgsBuffer => _argsBuffer;
    public GraphicsBuffer StatsBuffer => _statsBuffer;
    public bool IsValid => !_disposed && _bladeBuffer != null && _argsBuffer != null && _statsBuffer != null;
    public int Capacity { get; }
    public int ReportedInstanceCount => _readbackInstanceCount;
    public long BufferBytes { get; }
    public Bounds WorldBounds => _worldBounds;
    public bool HasStats => _hasStats;

    GrassChunkRuntime(GraphicsBuffer bladeBuffer, GraphicsBuffer argsBuffer, GraphicsBuffer statsBuffer,
        MaterialPropertyBlock props, int capacity, Bounds worldBounds)
    {
        _bladeBuffer = bladeBuffer;
        _argsBuffer = argsBuffer;
        _statsBuffer = statsBuffer;
        _props = props;
        _worldBounds = worldBounds;
        Capacity = Mathf.Max(0, capacity);
        BufferBytes = (long)Capacity * BladeStride + GraphicsBuffer.IndirectDrawArgs.size + (long)StatsCount * sizeof(uint);
    }

    public static GrassChunkRuntime Create(int capacity, int vertexCount, int bladeInstancesId,
        int statsCount, Bounds worldBounds)
    {
        if (capacity <= 0) return null;

        var bladeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, BladeStride);
        var argsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
            4,
            sizeof(uint));
        var statsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(statsCount, 1), sizeof(uint));

        var props = new MaterialPropertyBlock();
        props.SetBuffer(bladeInstancesId, bladeBuffer);

        var runtime = new GrassChunkRuntime(bladeBuffer, argsBuffer, statsBuffer, props, capacity, worldBounds);
        runtime.ResetArgsAndStats(vertexCount);
        return runtime;
    }

    public void ResetArgsAndStats(int vertexCount)
    {
        if (_argsBuffer == null) return;
        ArgsScratch[0] = (uint)Mathf.Max(vertexCount, 0);
        ArgsScratch[1] = 0;
        ArgsScratch[2] = 0;
        ArgsScratch[3] = 0;
        _argsBuffer.SetData(ArgsScratch);
        if (_statsBuffer != null)
            _statsBuffer.SetData(StatsScratch);
        _readbackInstanceCount = 0;
        _hasStats = false;
    }

    public void RequestReadbacks()
    {
        if (_argsBuffer == null || !SystemInfo.supportsAsyncGPUReadback)
            return;

        AsyncGPUReadback.Request(_argsBuffer, request =>
        {
            if (_disposed || request.hasError)
                return;
            var data = request.GetData<uint>();
            if (data.Length >= 2)
                _readbackInstanceCount = Mathf.Max(0, (int)data[1]);
        });

        if (_statsBuffer == null)
            return;

        AsyncGPUReadback.Request(_statsBuffer, request =>
        {
            if (_disposed || request.hasError)
                return;
            var data = request.GetData<uint>();
            int count = Mathf.Min(data.Length, _stats.Length);
            for (int i = 0; i < count; i++)
                _stats[i] = data[i];
            for (int i = count; i < _stats.Length; i++)
                _stats[i] = 0;
            _hasStats = true;
        });
    }

    public uint GetStat(int index)
    {
        return index >= 0 && index < _stats.Length ? _stats[index] : 0u;
    }

    public void Render(Material material, Camera camera, int layer)
    {
        if (_disposed || _argsBuffer == null) return;

        var renderParams = new RenderParams(material)
        {
            camera = camera,
            layer = layer,
            matProps = _props,
            worldBounds = _worldBounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = true,
        };
        Graphics.RenderPrimitivesIndirect(renderParams, MeshTopology.Triangles, _argsBuffer, 1, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bladeBuffer?.Dispose();
        _argsBuffer?.Dispose();
        _statsBuffer?.Dispose();
    }
}
