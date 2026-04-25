using UnityEngine;

public static class CoordinateConverter
{
    static readonly Vector3[] CubeFaceDirections =
    {
        Vector3.up, Vector3.down, Vector3.left,
        Vector3.right, Vector3.forward, Vector3.back
    };

    public static Vector3 UnitSphereToWorld(Vector3 pointOnUnitSphere, float elevation)
    {
        return pointOnUnitSphere * elevation;
    }

    public static Vector3 WorldToUnitSphere(Vector3 worldPoint)
    {
        return worldPoint.normalized;
    }

    public static (float latitude, float longitude) UnitSphereToLatLong(Vector3 point)
    {
        float latitude = Mathf.Asin(Mathf.Clamp(point.y, -1f, 1f));
        float longitude = Mathf.Atan2(point.x, -point.z);
        return (latitude, longitude);
    }

    public static Vector3 LatLongToUnitSphere(float latitude, float longitude)
    {
        float y = Mathf.Sin(latitude);
        float r = Mathf.Cos(latitude);
        float x = Mathf.Sin(longitude) * r;
        float z = -Mathf.Cos(longitude) * r;
        return new Vector3(x, y, z);
    }

    public static (int face, Vector2 uv) UnitSphereToCubeFace(Vector3 point)
    {
        float absX = Mathf.Abs(point.x);
        float absY = Mathf.Abs(point.y);
        float absZ = Mathf.Abs(point.z);

        int face;
        float u, v;

        if (absY >= absX && absY >= absZ)
        {
            face = point.y > 0 ? 0 : 1;
            float sign = point.y > 0 ? 1f : -1f;
            u = point.x / absY;
            v = point.z / absY * sign;
        }
        else if (absX >= absY && absX >= absZ)
        {
            face = point.x > 0 ? 3 : 2;
            float sign = point.x > 0 ? 1f : -1f;
            u = point.z / absX * -sign;
            v = point.y / absX;
        }
        else
        {
            face = point.z > 0 ? 4 : 5;
            float sign = point.z > 0 ? 1f : -1f;
            u = point.x / absZ * sign;
            v = point.y / absZ;
        }

        return (face, new Vector2((u + 1f) * 0.5f, (v + 1f) * 0.5f));
    }

    public static Vector3 CubeFaceToUnitSphere(int face, Vector2 uv)
    {
        float u = uv.x * 2f - 1f;
        float v = uv.y * 2f - 1f;

        Vector3 localUp = CubeFaceDirections[face];
        Vector3 axisA = new Vector3(localUp.y, localUp.z, localUp.x);
        Vector3 axisB = Vector3.Cross(localUp, axisA);

        Vector3 pointOnCube = localUp + u * axisA + v * axisB;
        return pointOnCube.normalized;
    }

    public static ChunkCoord UnitSphereToChunkCoord(Vector3 point, int chunksPerFaceEdge)
    {
        var (face, uv) = UnitSphereToCubeFace(point);
        int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * chunksPerFaceEdge), 0, chunksPerFaceEdge - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * chunksPerFaceEdge), 0, chunksPerFaceEdge - 1);
        return new ChunkCoord(face, x, y);
    }

    public static Vector3 ChunkCoordToUnitSphere(ChunkCoord coord, int chunksPerFaceEdge)
    {
        float u = (coord.X + 0.5f) / chunksPerFaceEdge;
        float v = (coord.Y + 0.5f) / chunksPerFaceEdge;
        return CubeFaceToUnitSphere(coord.Face, new Vector2(u, v));
    }

    public static float ArcDistance(Vector3 a, Vector3 b, float radius)
    {
        return Mathf.Atan2(Vector3.Cross(a.normalized, b.normalized).magnitude,
            Vector3.Dot(a.normalized, b.normalized)) * radius;
    }

    public static Vector3 SurfaceNormal(Vector3 worldPoint, Vector3 planetCenter)
    {
        return (worldPoint - planetCenter).normalized;
    }

    public static float NormalizedLatitude(Vector3 pointOnUnitSphere)
    {
        return Mathf.Abs(Mathf.Asin(Mathf.Clamp(pointOnUnitSphere.y, -1f, 1f))) / (Mathf.PI * 0.5f);
    }
}
