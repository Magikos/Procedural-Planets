using UnityEngine;

// Index math for the water level grid WaterBodyMap produces, shared by every consumer that has to ask
// "how high does water stand here" outside the water system itself.
//
// Scatter reads this from two places - a managed path and a Burst job - which cannot share an array type.
// They share THIS instead, so the part that could actually drift (projecting a direction onto the cube grid)
// has one implementation, and each caller only does its own array read.
public static class WaterLevelGrid
{
    public const float NoWater = float.NegativeInfinity;

    public static int Index(Vector3 dir, int res)
    {
        float ax = Mathf.Abs(dir.x);
        float ay = Mathf.Abs(dir.y);
        float az = Mathf.Abs(dir.z);
        int face;
        float uSigned, vSigned;

        if (ay >= ax && ay >= az)
        {
            float inv = 1f / Mathf.Max(ay, 1e-8f);
            if (dir.y >= 0f) { face = 0; uSigned = dir.x * inv; vSigned = -dir.z * inv; }
            else { face = 1; uSigned = -dir.x * inv; vSigned = -dir.z * inv; }
        }
        else if (ax >= ay && ax >= az)
        {
            float inv = 1f / Mathf.Max(ax, 1e-8f);
            if (dir.x >= 0f) { face = 3; uSigned = dir.z * inv; vSigned = -dir.y * inv; }
            else { face = 2; uSigned = -dir.z * inv; vSigned = -dir.y * inv; }
        }
        else
        {
            float inv = 1f / Mathf.Max(az, 1e-8f);
            if (dir.z >= 0f) { face = 4; uSigned = dir.y * inv; vSigned = -dir.x * inv; }
            else { face = 5; uSigned = -dir.y * inv; vSigned = -dir.x * inv; }
        }

        int x = Mathf.Clamp((int)((uSigned * 0.5f + 0.5f) * res), 0, res - 1);
        int y = Mathf.Clamp((int)((vSigned * 0.5f + 0.5f) * res), 0, res - 1);
        return face * res * res + y * res + x;
    }

    // Local-space radius of the water surface above a direction. Falls back to the global sea radius wherever
    // no water stands, which keeps every existing altitude and clearance test behaving exactly as it did.
    public static float SeaRadius(float level, float baseRadiusLocal, float fallbackSeaRadiusLocal) =>
        level == NoWater ? fallbackSeaRadiusLocal : baseRadiusLocal * (1f + level);
}
