using UnityEngine;

public static class Noise
{
    public static float Get3DNoise(Vector3 point, NoiseSettings settings)
    {
        float noiseValue = 0;
        float frequency = settings.simpleNoiseSettings.baseRoughness;
        float amplitude = 1;
        float weight = 1;

        bool isRidged = settings.filterType == NoiseSettings.FilterType.Ridged;
        NoiseSettings.RidgedNoiseSettings ridged = isRidged ? settings.ridgedNoiseSettings : null;
        NoiseSettings.SimpleNoiseSettings simple = isRidged ? ridged : settings.simpleNoiseSettings;

        for (int i = 0; i < simple.numLayers; i++)
        {
            float v = Evaluate(point * frequency + simple.centre);
            if (isRidged)
            {
                v = 1 - Mathf.Abs(v);
                v *= v;
                v *= weight;
                weight = Mathf.Clamp01(v * ridged.weightMultiplier);
            }
            noiseValue += v * amplitude;
            frequency *= simple.roughness;
            amplitude *= simple.persistence;
        }

        noiseValue = Mathf.Max(0, noiseValue - simple.minValue);
        return noiseValue * simple.strength;
    }

    public static float Evaluate(Vector3 point)
    {
        const float F3 = 1f / 3f;
        const float G3 = 1f / 6f;

        float s = (point.x + point.y + point.z) * F3;
        int i = Mathf.FloorToInt(point.x + s);
        int j = Mathf.FloorToInt(point.y + s);
        int k = Mathf.FloorToInt(point.z + s);

        float t = (i + j + k) * G3;
        Vector3 x0 = point - new Vector3(i - t, j - t, k - t);

        int i1, j1, k1;
        int i2, j2, k2;

        if (x0.x >= x0.y)
        {
            if (x0.y >= x0.z)
            {
                i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0;
            }
            else if (x0.x >= x0.z)
            {
                i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1;
            }
            else
            {
                i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1;
            }
        }
        else
        {
            if (x0.y < x0.z)
            {
                i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1;
            }
            else if (x0.x < x0.z)
            {
                i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1;
            }
            else
            {
                i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0;
            }
        }

        Vector3 x1 = x0 - new Vector3(i1, j1, k1) + new Vector3(G3, G3, G3);
        Vector3 x2 = x0 - new Vector3(i2, j2, k2) + new Vector3(2 * G3, 2 * G3, 2 * G3);
        Vector3 x3 = x0 - Vector3.one + new Vector3(3 * G3, 3 * G3, 3 * G3);

        float t0 = 0.5f - Vector3.Dot(x0, x0);
        float t1 = 0.5f - Vector3.Dot(x1, x1);
        float t2 = 0.5f - Vector3.Dot(x2, x2);
        float t3 = 0.5f - Vector3.Dot(x3, x3);

        t0 = (t0 < 0) ? 0 : t0 * t0 * t0 * (t0 * (t0 * 6 - 15) + 10);
        t1 = (t1 < 0) ? 0 : t1 * t1 * t1 * (t1 * (t1 * 6 - 15) + 10);
        t2 = (t2 < 0) ? 0 : t2 * t2 * t2 * (t2 * (t2 * 6 - 15) + 10);
        t3 = (t3 < 0) ? 0 : t3 * t3 * t3 * (t3 * (t3 * 6 - 15) + 10);

        Vector3 g0 = GetGradient(i, j, k);
        Vector3 g1 = GetGradient(i + i1, j + j1, k + k1);
        Vector3 g2 = GetGradient(i + i2, j + j2, k + k2);
        Vector3 g3 = GetGradient(i + 1, j + 1, k + 1);

        float noise = t0 * Vector3.Dot(g0, x0);
        noise += t1 * Vector3.Dot(g1, x1);
        noise += t2 * Vector3.Dot(g2, x2);
        noise += t3 * Vector3.Dot(g3, x3);

        return 45.23065f * noise; // Normalize to [-1,1] range for 3D
    }

    static Vector3 GetGradient(int x, int y, int z)
    {
        int h = (x * 1619 ^ y * 31337 ^ z * 6971) & 15;
        float u = h < 8 ? x : y;
        float v = h < 4 ? y : h == 12 || h == 14 ? x : z;
        return new Vector3((h & 1) == 0 ? u : -u, (h & 2) == 0 ? v : -v, 0);
    }
}