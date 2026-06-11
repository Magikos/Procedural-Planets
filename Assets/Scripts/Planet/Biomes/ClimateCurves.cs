using UnityEngine;

public static class ClimateCurves
{
    public static AnimationCurve CreateLinear(params Vector2[] points)
    {
        if (points == null || points.Length == 0)
            points = new[] { new Vector2(0f, 0f), new Vector2(1f, 1f) };

        var keys = new Keyframe[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            float inTangent = i > 0 ? Slope(points[i - 1], points[i]) : 0f;
            float outTangent = i + 1 < points.Length ? Slope(points[i], points[i + 1]) : 0f;
            keys[i] = new Keyframe(points[i].x, points[i].y, inTangent, outTangent);
        }

        return new AnimationCurve(keys);
    }

    public static AnimationCurve WithLinearPoint(AnimationCurve source, float latitude01, float value01)
    {
        latitude01 = Mathf.Clamp01(latitude01);
        value01 = Mathf.Clamp01(value01);

        var points = new System.Collections.Generic.List<Vector2>();
        if (source != null)
        {
            Keyframe[] keys = source.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (Mathf.Abs(keys[i].time - latitude01) > 0.0001f)
                    points.Add(new Vector2(keys[i].time, keys[i].value));
            }
        }

        points.Add(new Vector2(latitude01, value01));
        points.Sort((a, b) => a.x.CompareTo(b.x));
        return CreateLinear(points.ToArray());
    }

    public static AnimationCurve EarthlikeMoisture()
    {
        return CreateLinear(
            new Vector2(0f, 0.90f),
            new Vector2(0.18f, 0.30f),
            new Vector2(0.42f, 0.68f),
            new Vector2(0.70f, 0.42f),
            new Vector2(1f, 0.18f));
    }

    public static AnimationCurve DefaultTemperature()
    {
        return CreateLinear(
            new Vector2(0f, 1f),
            new Vector2(1f, 0f));
    }

    public static string Describe(AnimationCurve curve)
    {
        if (curve == null)
            return "<missing>";

        Keyframe[] keys = curve.keys;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('(')
                .Append(keys[i].time.ToString("F2"))
                .Append(':')
                .Append(keys[i].value.ToString("F2"))
                .Append(')');
        }
        return sb.ToString();
    }

    static float Slope(Vector2 a, Vector2 b)
    {
        float dt = b.x - a.x;
        return Mathf.Abs(dt) > 0.000001f ? (b.y - a.y) / dt : 0f;
    }
}
