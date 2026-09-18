using UnityEngine;

public static class MoonOrbit
{
    public static float Advance(float progress, float elapsedSeconds, float daySeconds, float cycleDays)
    {
        if (!float.IsFinite(progress)) throw new System.ArgumentOutOfRangeException(nameof(progress));
        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0f) throw new System.ArgumentOutOfRangeException(nameof(elapsedSeconds));
        double value = progress;
        if (float.IsFinite(daySeconds) && daySeconds > 0f && float.IsFinite(cycleDays) && cycleDays > 0f)
            value += (double)elapsedSeconds / daySeconds / cycleDays;
        return (float)(value - System.Math.Floor(value));
    }

    public static Quaternion Frame(float timeOfDay, float axialTilt) =>
        Quaternion.Euler(axialTilt, 0f, 0f) * Quaternion.AngleAxis(timeOfDay * 360f, Vector3.forward);

    public static Vector3 Direction(float timeOfDay, float progress, float axialTilt, float inclination, float nodeAngle,
        out Vector3 orbitNormal)
    {
        Quaternion tilt = Quaternion.AngleAxis(nodeAngle, Vector3.forward)
            * Quaternion.AngleAxis(inclination, Vector3.up)
            * Quaternion.AngleAxis(-nodeAngle, Vector3.forward);
        Quaternion frame = Frame(timeOfDay, axialTilt) * tilt;
        orbitNormal = frame * Vector3.forward;
        return frame * (Quaternion.AngleAxis(-progress * 360f, Vector3.forward) * Vector3.up);
    }

    public static int PhaseIndex(float progress) => Mathf.FloorToInt(Mathf.Repeat(progress + 1f / 16f, 1f) * 8f) % 8;

    public static float VisualRadius(float orbitRadius, float diameterDegrees) =>
        orbitRadius * Mathf.Sin(diameterDegrees * Mathf.Deg2Rad * 0.5f);

    public static float ProjectionScale(float distance, float visualRadius, float farClip) =>
        Mathf.Min(1f, farClip * 0.9f / Mathf.Max(distance + visualRadius, 0.001f));
}
