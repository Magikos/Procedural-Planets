public class MinMax
{
    float _min;
    float _max;

    public float Min => _min;
    public float Max => _max;

    public MinMax()
    {
        Reset();
    }

    public void Reset()
    {
        _min = float.MaxValue;
        _max = float.MinValue;
    }

    // Not thread-safe — single-writer expected. Post-PERF-02 the elevation sweep that feeds this
    // is sequential on the main thread (after Burst mesh jobs complete). If a future caller wants
    // to feed it from multiple threads, restore Interlocked CAS on _min / _max.
    public void AddValue(float value)
    {
        if (value < _min) _min = value;
        if (value > _max) _max = value;
    }
}
