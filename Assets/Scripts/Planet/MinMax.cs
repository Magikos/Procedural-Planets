using System.Threading;

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

    public void AddValue(float value)
    {
        InterlockedMin(ref _min, value);
        InterlockedMax(ref _max, value);
    }

    static void InterlockedMin(ref float target, float value)
    {
        float current = target;
        while (value < current)
        {
            float previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current) break;
            current = previous;
        }
    }

    static void InterlockedMax(ref float target, float value)
    {
        float current = target;
        while (value > current)
        {
            float previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current) break;
            current = previous;
        }
    }
}
