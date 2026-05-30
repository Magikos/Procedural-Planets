using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

// 3D simplex noise, refactored for Burst compatibility.
//
//   NoiseData     blittable struct holding the seeded permutation table; embeddable
//                 directly in Burst jobs (fixed buffer, no NativeArray, no Dispose).
//   NoiseData.Evaluate  Burst-compatible instance method; float-based.
//   Noise         managed back-compat wrapper preserving the original Vector3 API.
//
// Behavioural note: ported from double to float for SIMD/Burst. Output differs from
// the original by ~1e-6 magnitude on the same seed and is not bit-identical.

public unsafe struct NoiseData
{
    public const int PermutationSize = 256;
    public fixed int Permutation[PermutationSize * 2];

    static readonly int[] Source = {
        151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225, 140, 36, 103, 30, 69, 142,
        8, 99, 37, 240, 21, 10, 23, 190, 6, 148, 247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203,
        117, 35, 11, 32, 57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175, 74, 165,
        71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122, 60, 211, 133, 230, 220, 105, 92, 41,
        55, 46, 245, 40, 244, 102, 143, 54, 65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89,
        18, 169, 200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64, 52, 217, 226, 250,
        124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212, 207, 206, 59, 227, 47, 16, 58, 17, 182, 189,
        28, 42, 223, 183, 170, 213, 119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
        129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104, 218, 246, 97, 228, 251, 34,
        242, 193, 238, 210, 144, 12, 191, 179, 162, 241, 81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31,
        181, 199, 106, 157, 184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93, 222, 114,
        67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180
    };

    public static NoiseData Create(int seed)
    {
        NoiseData data;
        if (seed != 0)
        {
            byte b0 = (byte)(seed & 0xff);
            byte b1 = (byte)((seed >> 8) & 0xff);
            byte b2 = (byte)((seed >> 16) & 0xff);
            byte b3 = (byte)((seed >> 24) & 0xff);
            for (int i = 0; i < PermutationSize; i++)
            {
                int v = Source[i] ^ b0;
                v ^= b1;
                v ^= b2;
                v ^= b3;
                data.Permutation[i] = v;
                data.Permutation[i + PermutationSize] = v;
            }
        }
        else
        {
            for (int i = 0; i < PermutationSize; i++)
            {
                int v = Source[i];
                data.Permutation[i] = v;
                data.Permutation[i + PermutationSize] = v;
            }
        }
        return data;
    }

    const float F3 = 1f / 3f;
    const float G3 = 1f / 6f;

    [BurstCompile]
    public float Evaluate(float3 point)
    {
        float x = point.x, y = point.y, z = point.z;
        float n0 = 0f, n1 = 0f, n2 = 0f, n3 = 0f;

        float s = (x + y + z) * F3;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);
        int k = FastFloor(z + s);

        float t = (i + j + k) * G3;
        float x0 = x - (i - t);
        float y0 = y - (j - t);
        float z0 = z - (k - t);

        int i1, j1, k1, i2, j2, k2;
        if (x0 >= y0)
        {
            if (y0 >= z0)      { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
            else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
            else               { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
        }
        else
        {
            if (y0 < z0)       { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
            else if (x0 < z0)  { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
            else               { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
        }

        float x1 = x0 - i1 + G3;
        float y1 = y0 - j1 + G3;
        float z1 = z0 - k1 + G3;

        float x2 = x0 - i2 + F3;
        float y2 = y0 - j2 + F3;
        float z2 = z0 - k2 + F3;

        float x3 = x0 - 0.5f;
        float y3 = y0 - 0.5f;
        float z3 = z0 - 0.5f;

        int ii = i & 0xff;
        int jj = j & 0xff;
        int kk = k & 0xff;

        float t0 = 0.6f - x0 * x0 - y0 * y0 - z0 * z0;
        if (t0 > 0f)
        {
            t0 *= t0;
            int gi0 = Permutation[ii + Permutation[jj + Permutation[kk]]] % 12;
            n0 = t0 * t0 * Grad3Dot(gi0, x0, y0, z0);
        }

        float t1 = 0.6f - x1 * x1 - y1 * y1 - z1 * z1;
        if (t1 > 0f)
        {
            t1 *= t1;
            int gi1 = Permutation[ii + i1 + Permutation[jj + j1 + Permutation[kk + k1]]] % 12;
            n1 = t1 * t1 * Grad3Dot(gi1, x1, y1, z1);
        }

        float t2 = 0.6f - x2 * x2 - y2 * y2 - z2 * z2;
        if (t2 > 0f)
        {
            t2 *= t2;
            int gi2 = Permutation[ii + i2 + Permutation[jj + j2 + Permutation[kk + k2]]] % 12;
            n2 = t2 * t2 * Grad3Dot(gi2, x2, y2, z2);
        }

        float t3 = 0.6f - x3 * x3 - y3 * y3 - z3 * z3;
        if (t3 > 0f)
        {
            t3 *= t3;
            int gi3 = Permutation[ii + 1 + Permutation[jj + 1 + Permutation[kk + 1]]] % 12;
            n3 = t3 * t3 * Grad3Dot(gi3, x3, y3, z3);
        }

        return (n0 + n1 + n2 + n3) * 32f;
    }

    static int FastFloor(float x) => x >= 0f ? (int)x : (int)x - 1;

    static float Grad3Dot(int hash, float x, float y, float z)
    {
        switch (hash)
        {
            case 0:  return  x + y;
            case 1:  return -x + y;
            case 2:  return  x - y;
            case 3:  return -x - y;
            case 4:  return  x + z;
            case 5:  return -x + z;
            case 6:  return  x - z;
            case 7:  return -x - z;
            case 8:  return  y + z;
            case 9:  return -y + z;
            case 10: return  y - z;
            case 11: return -y - z;
            default: return 0f;
        }
    }
}

public class Noise
{
    NoiseData _data;

    public Noise() : this(0) { }
    public Noise(int seed) { _data = NoiseData.Create(seed); }

    public float Evaluate(Vector3 point)
        => _data.Evaluate(new float3(point.x, point.y, point.z));

    public ref NoiseData Data => ref _data;
}
