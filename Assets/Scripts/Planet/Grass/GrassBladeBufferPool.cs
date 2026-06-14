using System.Collections.Generic;
using UnityEngine;

// Reuses fixed-size chunk grass blade buffers as chunks page in and out, instead of
// allocating/disposing a GraphicsBuffer per chunk every time the camera moves. Every chunk
// runtime uses the same capacity and stride, so a single free-list works. Only the blade
// buffer is pooled — it carries no AsyncGPUReadback, so reuse can't race a pending readback
// (the args/stats buffers, which are read back, stay per-runtime and tiny).
sealed class GrassBladeBufferPool : System.IDisposable
{
    readonly int _capacity;
    readonly int _stride;
    readonly Stack<GraphicsBuffer> _free = new();
    int _liveCount;

    public int FreeCount => _free.Count;
    public int LiveCount => _liveCount;

    public GrassBladeBufferPool(int capacity, int stride)
    {
        _capacity = Mathf.Max(1, capacity);
        _stride = Mathf.Max(1, stride);
    }

    public GraphicsBuffer Acquire()
    {
        _liveCount++;
        if (_free.Count > 0)
            return _free.Pop();
        return new GraphicsBuffer(GraphicsBuffer.Target.Structured, _capacity, _stride);
    }

    public void Release(GraphicsBuffer buffer)
    {
        if (buffer == null) return;
        _liveCount = Mathf.Max(0, _liveCount - 1);
        _free.Push(buffer);
    }

    public void Dispose()
    {
        while (_free.Count > 0)
            _free.Pop().Dispose();
        _liveCount = 0;
    }
}
