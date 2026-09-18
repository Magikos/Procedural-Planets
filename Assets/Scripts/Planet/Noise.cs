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
// The full-seed shuffle replaces the original eight-bit seed fold for every caller.
// Existing seeds intentionally generate different worlds; no legacy mode is retained.

public unsafe struct NoiseData
{
    public const int PermutationSize = 256;
    public fixed int Permutation[PermutationSize * 2];

    public static NoiseData Create(int seed)
    {
        NoiseData data;
        for (int i = 0; i < PermutationSize; i++) data.Permutation[i] = i;

        // Hash the full seed and shuffle step so signed seeds and seed zero retain all their bits.
        for (int i = PermutationSize - 1; i > 0; i--)
        {
            uint value = math.hash(new uint2(unchecked((uint)seed), (uint)i));
            int j = (int)(value % (uint)(i + 1));
            int entry = data.Permutation[i];
            data.Permutation[i] = data.Permutation[j];
            data.Permutation[j] = entry;
        }
        for (int i = 0; i < PermutationSize; i++)
            data.Permutation[i + PermutationSize] = data.Permutation[i];
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

    public unsafe void CopyPermutation(int[] dest, int destOffset)
    {
        fixed (int* src = Permutation)
            for (int i = 0; i < PermutationSize * 2; i++)
                dest[destOffset + i] = src[i];
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
