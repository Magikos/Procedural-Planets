using UnityEngine;

/// <summary>Continuous free line, with a second span only at an acquired hand contact.</summary>
public static class FishingLineGeometry
{
    public static Vector3 Point(Vector3 tip, Vector3 end, Vector3 hand, Vector3 up, float sag, float grip, float t)
    {
        const float contact = 2f / 3f;
        Vector3 free = Vector3.Lerp(tip, end, t) - up * (4 * t * (1 - t) * sag);
        float u = t <= contact ? t / contact : (t - contact) / (1 - contact);
        Vector3 held = t <= contact ? Vector3.Lerp(tip, hand, u) : Vector3.Lerp(hand, end, u);
        held -= up * (4 * u * (1 - u) * sag);
        return Vector3.Lerp(free, held, Mathf.Clamp01(grip));
    }

    public static Vector3 LimitReach(Vector3 tip, Vector3 fish, float length)
    {
        Vector3 delta = fish - tip;
        return tip + Vector3.ClampMagnitude(delta, Mathf.Max(0, length));
    }
}
