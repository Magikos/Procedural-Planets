using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

// Broad phase runs once per frame. All drops share the resulting collider snapshot.
sealed class PrecipitationCollisionBinding : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct Shape
    {
        public Matrix4x4 WorldToLocal;
        public Vector4 CenterType;
        public Vector4 Size;
    }

    readonly List<Shape> _shapes = new();
    readonly List<Vector3> _triangles = new();
    readonly Dictionary<Mesh, (Vector3[] vertices, int[] indices)> _meshes = new();
    readonly HashSet<Mesh> _unreadable = new();
    ComputeBuffer _triangleBuffer;
    Collider[] _overlap = new Collider[64];
    ComputeBuffer _buffer;
    Texture2DArray _emptyClimate;
    readonly int[] _radiusIds = new int[6];
    readonly int[] _globalRadiusIds = {
        Shader.PropertyToID(ShaderGlobalIds.GrassSurfaceRadiusF0), Shader.PropertyToID(ShaderGlobalIds.GrassSurfaceRadiusF1),
        Shader.PropertyToID(ShaderGlobalIds.GrassSurfaceRadiusF2), Shader.PropertyToID(ShaderGlobalIds.GrassSurfaceRadiusF3),
        Shader.PropertyToID(ShaderGlobalIds.GrassSurfaceRadiusF4), Shader.PropertyToID(ShaderGlobalIds.GrassSurfaceRadiusF5) };

    public PrecipitationCollisionBinding()
    {
        for (int i = 0; i < 6; i++)
        {
            _radiusIds[i] = Shader.PropertyToID($"_RainSurfaceRadius{i}");
        }
    }

    public void Bind(ComputeShader compute, int kernel, Vector3 observer, float radius, Vector3 columnTop)
    {
        int count;
        while ((count = Physics.OverlapCapsuleNonAlloc(observer, columnTop, radius, _overlap,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) == _overlap.Length)
            Array.Resize(ref _overlap, _overlap.Length * 2);
        _shapes.Clear();
        _triangles.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider collider = _overlap[i];
            if (collider is BoxCollider box)
            {
                _shapes.Add(new Shape { WorldToLocal = box.transform.worldToLocalMatrix,
                    CenterType = new Vector4(box.center.x, box.center.y, box.center.z, 0),
                    Size = box.size * 0.5f });
            }
            else if (collider is SphereCollider sphere)
            {
                Vector3 scale = sphere.transform.lossyScale;
                float r = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                AddCapsule(sphere.transform.TransformPoint(sphere.center), Quaternion.identity, r, 0f);
            }
            else if (collider is CapsuleCollider capsule)
            {
                Vector3 scale = capsule.transform.lossyScale;
                int axis = capsule.direction;
                float r = capsule.radius * Mathf.Max(Mathf.Abs(scale[(axis + 1) % 3]), Mathf.Abs(scale[(axis + 2) % 3]));
                Vector3 up = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                AddCapsule(capsule.transform.TransformPoint(capsule.center),
                    Quaternion.FromToRotation(Vector3.up, capsule.transform.TransformDirection(up)),
                    r, Mathf.Max(0f, capsule.height * Mathf.Abs(scale[axis]) * 0.5f - r));
            }
            else if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
            {
                Mesh mesh = meshCollider.sharedMesh;
                if (!mesh.isReadable)
                {
                    if (_unreadable.Add(mesh)) LoggerProvider.Get().Log(LogLevel.Warning, "Precipitation",
                        $"Rain collision mesh '{mesh.name}' needs Read/Write enabled, or primitive colliders.");
                    continue;
                }
                if (!_meshes.TryGetValue(mesh, out var data))
                    _meshes[mesh] = data = (mesh.vertices, mesh.triangles);
                int offset = _triangles.Count;
                Vector3 center = mesh.bounds.center;
                foreach (int index in data.indices) _triangles.Add(data.vertices[index] - center);
                Vector3 extent = mesh.bounds.extents;
                _shapes.Add(new Shape { WorldToLocal = meshCollider.transform.worldToLocalMatrix,
                    CenterType = new Vector4(center.x, center.y, center.z, 2 + data.indices.Length / 3),
                    Size = new Vector4(extent.x, extent.y, extent.z, offset) });
            }
            else if (collider is CharacterController character)
            {
                Vector3 scale = character.transform.lossyScale;
                float r = character.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                AddCapsule(character.transform.TransformPoint(character.center), character.transform.rotation,
                    r, Mathf.Max(0f, character.height * Mathf.Abs(scale.y) * 0.5f - r));
            }
        }
        int required = Mathf.Max(1, _shapes.Count);
        if (_buffer == null || _buffer.count < required)
        {
            _buffer?.Release();
            _buffer = new ComputeBuffer(Mathf.NextPowerOfTwo(required), Marshal.SizeOf<Shape>());
        }
        if (_shapes.Count > 0) _buffer.SetData(_shapes);
        if (_triangleBuffer == null || _triangleBuffer.count < Mathf.Max(1, _triangles.Count))
        {
            _triangleBuffer?.Release();
            _triangleBuffer = new ComputeBuffer(Mathf.NextPowerOfTwo(Mathf.Max(1,_triangles.Count)), 12);
        }
        if (_triangles.Count > 0) _triangleBuffer.SetData(_triangles);
        compute.SetBuffer(kernel, "_RainTriangles", _triangleBuffer);
        compute.SetBuffer(kernel, "_RainColliders", _buffer);
        compute.SetInt("_RainColliderCount", _shapes.Count);
        int resolution = Shader.GetGlobalInt(ShaderGlobalIds.GrassSurfaceAtlasResolution);
        compute.SetInt("_RainSurfaceResolution", resolution);
        for (int i = 0; i < 6; i++)
            compute.SetTexture(kernel, _radiusIds[i], Shader.GetGlobalTexture(_globalRadiusIds[i]) ?? Texture2D.blackTexture);
        GrassWaterFieldBinding.Bind(compute, kernel);
        Texture climate = Shader.GetGlobalTexture(ShaderGlobalIds.ClimateMap);
        if (climate == null)
        {
            if (_emptyClimate == null) _emptyClimate = new Texture2DArray(1,1,6,TextureFormat.RGHalf,false)
                { hideFlags = HideFlags.HideAndDontSave };
            climate = _emptyClimate;
        }
        compute.SetTexture(kernel, ShaderGlobalIds.ClimateMap, climate);
        compute.SetFloat(ShaderGlobalIds.ClimateMapResolution, Shader.GetGlobalFloat(ShaderGlobalIds.ClimateMapResolution));
        compute.SetVector(ShaderGlobalIds.ClimateTemperatureRangeCelsius, Shader.GetGlobalVector(ShaderGlobalIds.ClimateTemperatureRangeCelsius));
        compute.SetVector(ShaderGlobalIds.WeatherParticlePhaseParams, Shader.GetGlobalVector(ShaderGlobalIds.WeatherParticlePhaseParams));
        compute.SetVector(ShaderGlobalIds.WeatherParticleSnowParams, Shader.GetGlobalVector(ShaderGlobalIds.WeatherParticleSnowParams));
        compute.SetInt(ShaderGlobalIds.WeatherParticleProof, Shader.GetGlobalInt(ShaderGlobalIds.WeatherParticleProof));
    }

    void AddCapsule(Vector3 center, Quaternion rotation, float radius, float halfSegment)
    {
        _shapes.Add(new Shape { WorldToLocal = Matrix4x4.TRS(center, rotation, Vector3.one).inverse,
            CenterType = new Vector4(0f, 0f, 0f, 1f), Size = new Vector4(radius, halfSegment, 0f, 0f) });
    }

    public void Dispose()
    {
        _buffer?.Release();
        _triangleBuffer?.Release();
        if (_emptyClimate != null) UnityEngine.Object.Destroy(_emptyClimate);
    }
}
