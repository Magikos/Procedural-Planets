using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Owned by Planet, including the native particle emitters and runtime materials.
public sealed class RiverRenderer : IDisposable
{
    public readonly List<MeshFilter> VolumeSurfaces = new();
    readonly List<Mesh> _meshes = new();
    readonly List<GameObject> _objects = new();
    readonly List<ParticleSystem> _spray = new();
    readonly List<ParticleSystem> _mist = new();
    readonly List<ParticleSystem> _lipSpray = new();
    readonly List<RiverSegment> _falls = new();
    readonly Transform _planet;
    readonly int[] _sprayAssignments = new int[12];
    readonly float[] _nearDistances = new float[12];
    readonly int[] _nearFalls = new int[12];
    readonly Material _material;
    readonly Material _sprayMaterial;

    sealed class Geometry
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector4> Tangents = new();
        public readonly List<Vector4> Uv = new();
        public readonly List<Vector2> Blend = new();
        public readonly List<Color> Colors = new();
        public readonly List<int> Triangles = new();
    }

    public RiverRenderer(Transform planet, RiverField field, WaterDto water, WaterBodyMap bodies, Material surfaceMaterial)
    {
        if (surfaceMaterial == null) throw new ArgumentNullException(nameof(surfaceMaterial));
        _planet = planet;
        Array.Fill(_sprayAssignments, -1);
        Shader shader = Resources.Load<Shader>("River");
        if (shader == null) throw new InvalidOperationException("River shader is missing from Resources.");
        _material = new Material(shader) { name = "Waterfall sheet", hideFlags = HideFlags.DontSave };
        _material.SetColor("_WaterColor", water.ShallowBaseColor);
        _material.SetColor("_FoamColor", water.FoamColor);
        Shader sprayShader = Resources.Load<Shader>("WaterSplash");
        if (sprayShader != null) _sprayMaterial = new Material(sprayShader) { hideFlags = HideFlags.DontSave };
        var groups = new Dictionary<int, Geometry>();
        var connections = new Dictionary<Vector3, int>();
        var directions = new Dictionary<Vector3, Vector3>();
        var impacts = new HashSet<Vector3>();
        var receivingKinds = new Dictionary<int, float>();
        for (int i = 0; i < field.SegmentCount; i++)
        {
            var segment = field.Data.Segments[i];
            Count(segment.A.xyz); Count(segment.B.xyz);
            Vector3 direction = ((Vector3)segment.B.xyz - (Vector3)segment.A.xyz).normalized;
            AddDirection(segment.A.xyz, direction); AddDirection(segment.B.xyz, direction);
            if (segment.Shape.w > 0f) impacts.Add(segment.B.xyz);
            if (bodies != null && bodies.TrySampleBody(segment.B.xyz, out var body))
                receivingKinds[(int)segment.Flow.x] = body.Kind == WaterBodyKind.Ocean ? 1f : 0f;
        }
        void Count(Vector3 point) { connections.TryGetValue(point, out int count); connections[point] = count + 1; }
        void AddDirection(Vector3 point, Vector3 direction) { directions.TryGetValue(point, out var current); directions[point] = current + direction; }
        float radius = field.Data.PlanetRadius;
        float deepDepth = surfaceMaterial.GetFloat("_DeepDepth");
        for (int i = 0; i < field.SegmentCount; i++)
        {
            var segment = field.Data.Segments[i];
            receivingKinds.TryGetValue((int)segment.Flow.x, out float body01);
            int group = WaterLevelGrid.Index((Vector3)segment.A.xyz, 4) * 4 + (segment.Shape.w > 0f ? 1 : 0);
            float level = 0f;
            bool receivingApproach = segment.Shape.w == 0f && bodies != null &&
                connections[(Vector3)segment.B.xyz] == 1 &&
                bodies.TrySolvedLevelAt(segment.B.xyz, out level) &&
                Mathf.Abs(radius * (1f + level) - segment.B.w) < .02f;
            if (!groups.TryGetValue(group, out var geometry)) groups[group] = geometry = new Geometry();
            AddRibbon(geometry, segment, radius, deepDepth, connections, directions, impacts.Contains(segment.A.xyz), false, receivingApproach, body01);
            if (segment.Shape.w > 0f) _falls.Add(segment);
            else if (receivingApproach)
            {
                // A presentation-only tail overlaps existing receiving water. It cannot carve a new lake.
                var tail = segment;
                Vector3 start = segment.B.xyz;
                Vector3 along = Vector3.ProjectOnPlane((Vector3)segment.B.xyz - (Vector3)segment.A.xyz, start).normalized;
                float width = segment.Width(1f);
                float distance = 0f;
                for (int step = 1; step <= 16; step++)
                {
                    float candidateDistance = width * 4f * step / 16f;
                    Vector3 candidate = (start + along * (candidateDistance / radius)).normalized;
                    if (!bodies.TrySolvedLevelAt(candidate, out float receivingLevel) ||
                        Mathf.Abs(receivingLevel - level) * radius > .02f) break;
                    distance = candidateDistance;
                }
                if (distance > width * .25f)
                {
                    tail.A = segment.B;
                    tail.B = new Unity.Mathematics.float4((Unity.Mathematics.float3)(start + along * (distance / radius)).normalized, segment.B.w);
                    tail.Shape.x = width;
                    tail.Profile.x = width * 1.6f; tail.Profile.y = 0f;
                    tail.Flow.y = segment.Flow.z; tail.Flow.z += distance;
                    int mouthGroup = group + 2;
                    if (!groups.TryGetValue(mouthGroup, out var mouth)) groups[mouthGroup] = mouth = new Geometry();
                    AddRibbon(mouth, tail, radius, deepDepth, connections, directions, false, true, false, body01);
                }
            }
        }
        if (_sprayMaterial != null)
            for (int i = 0; i < Mathf.Min(12, _falls.Count); i++)
            {
                _spray.Add(AddSpray(planet, _falls[i], 0));
                _mist.Add(AddSpray(planet, _falls[i], 1));
                _lipSpray.Add(AddSpray(planet, _falls[i], 2));
            }
        foreach (var pair in groups)
        {
            var g = pair.Value;
            if ((pair.Key & 3) == 0)
                for (int v = 0; v < g.Vertices.Count; v++)
                {
                    Vector3 direction = g.Vertices[v].normalized;
                    if (field.Data.Sample(direction, out var selected, out float along, out _))
                        g.Vertices[v] = direction * (selected.Radius(along) + WaterMeshBuilder.SurfaceOffsetFor(radius));
                }
            var mesh = new Mesh { name = "River tile " + pair.Key, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(g.Vertices); mesh.SetNormals(g.Normals); mesh.SetTangents(g.Tangents);
            mesh.SetUVs(0, g.Uv); mesh.SetUVs(1, g.Blend); mesh.SetColors(g.Colors); mesh.SetTriangles(g.Triangles, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(mesh.name);
            go.transform.SetParent(planet, false);
            var filter = go.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = (pair.Key & 1) != 0 ? _material : surfaceMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshes.Add(mesh); _objects.Add(go);
            if ((pair.Key & 1) == 0)
            {
                VolumeSurfaces.Add(filter);
                WaterSurfaceRegistry.Register(filter);
            }
        }
    }

    static void AddRibbon(Geometry g, RiverSegment s, float radius, float deepDepth, Dictionary<Vector3, int> connections, Dictionary<Vector3, Vector3> directions, bool impact, bool mouth = false, bool receivingApproach = false, float body01 = 0f)
    {
        Vector3 a = s.A.xyz, b = s.B.xyz;
        Vector3 tangent = (b - a).normalized;
        float length = Vector3.Distance(a * s.A.w, b * s.B.w);
        int steps = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(s.Shape.x * .6f, 1f)));
        const int across = 4;
        int first = g.Vertices.Count;
        for (int row = 0; row <= steps; row++)
        {
            float t = (float)row / steps;
            Vector3 dir = Vector3.Slerp(a, b, t).normalized;
            Vector3 rowTangent = tangent;
            if (!mouth && s.Shape.w == 0f && row == 0 && connections[a] == 2) rowTangent = directions[a].normalized;
            if (!mouth && s.Shape.w == 0f && row == steps && connections[b] == 2) rowTangent = directions[b].normalized;
            Vector3 side = Vector3.Cross(rowTangent, dir).normalized;
            float width = s.Width(t);
            float r = s.Radius(t);
            Vector3 down = ((b * s.B.w) - (a * s.A.w)).normalized;
            Vector3 normal = Vector3.Cross(side, down).normalized;
            if (Vector3.Dot(normal, dir) < 0f) normal = -normal;
            for (int col = 0; col <= across; col++)
            {
                float cross = (float)col / across * 2f - 1f;
                // Keep the visible outer row inside the analytic bank despite the averaged join tangent.
                float visibleWidth = Mathf.Max(width - .06f, width * .95f);
                Vector3 vertexDir = (dir + side * (cross * visibleWidth / radius)).normalized;
                g.Vertices.Add(vertexDir * (r + WaterMeshBuilder.SurfaceOffsetFor(radius)));
                g.Normals.Add(normal);
                g.Tangents.Add(new Vector4(side.x, side.y, side.z, 1f));
                g.Uv.Add(new Vector4(cross * width, Mathf.Lerp(s.Flow.y, s.Flow.z, t), s.Shape.z, impact ? -Mathf.Exp(-t * length / (width * 1.5f)) : s.Shape.w));
                // The approach retains the river volume; the tail overlaps receiving water only.
                g.Blend.Add(mouth ? new Vector2(.35f + t * .65f, 1f) :
                    receivingApproach ? new Vector2(t * .35f, 2f) : Vector2.zero);
                // Shared water depth/shore/body data. Alpha 2 identifies river geometry in both passes.
                g.Colors.Add(new Color(Mathf.Clamp01(s.Shape.y / deepDepth), 1f - Mathf.Abs(cross), body01, 2f));
                if (row == steps || col == across) continue;
                int v = first + row * (across + 1) + col;
                g.Triangles.Add(v); g.Triangles.Add(v + across + 1); g.Triangles.Add(v + 1);
                g.Triangles.Add(v + 1); g.Triangles.Add(v + across + 1); g.Triangles.Add(v + across + 2);
            }
        }
        if (!mouth && s.Shape.w == 0f)
        {
            if (connections[a] > 2) AddCap(g, s, a, s.A.w, s.Flow.y, deepDepth, radius, body01);
            if (connections[b] > 2) AddCap(g, s, b, s.B.w, s.Flow.z, deepDepth, radius, body01);
        }
    }

    static void AddCap(Geometry g, RiverSegment s, Vector3 direction, float radius, float downstreamDistance, float deepDepth, float planetRadius, float body01)
    {
        Vector3 tangent = ((Vector3)s.B.xyz - (Vector3)s.A.xyz).normalized;
        Vector3 side = Vector3.Cross(tangent, direction).normalized;
        Vector3 along = Vector3.Cross(direction, side).normalized;
        const int sides = 16;
        int start = g.Vertices.Count;
        for (int i = 0; i <= sides; i++)
        {
            float angle = (i - 1) * Mathf.PI * 2f / sides;
            float width = s.Width(direction == (Vector3)s.A.xyz ? 0f : 1f);
            Vector2 offset = i == 0 ? Vector2.zero : new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * width;
            Vector3 dir = (direction + (side * offset.x + along * offset.y) / planetRadius).normalized;
            g.Vertices.Add(dir * (radius + WaterMeshBuilder.SurfaceOffsetFor(planetRadius)));
            g.Normals.Add(direction);
            g.Tangents.Add(new Vector4(side.x, side.y, side.z, 1f));
            g.Uv.Add(new Vector4(offset.x, downstreamDistance + offset.y, s.Shape.z, 0f));
            g.Blend.Add(Vector2.zero);
            g.Colors.Add(new Color(Mathf.Clamp01(s.Shape.y / deepDepth), i == 0 ? 1f : 0f, body01, 2f));
            if (i == 0) continue;
            g.Triangles.Add(start); g.Triangles.Add(start + i); g.Triangles.Add(start + i % sides + 1);
        }
    }

    ParticleSystem AddSpray(Transform planet, RiverSegment segment, int kind)
    {
        var go = new GameObject(kind == 0 ? "Waterfall splash" : kind == 1 ? "Waterfall mist" : "Waterfall lip spray");
        go.transform.SetParent(planet, false);
        go.transform.localPosition = (Vector3)segment.B.xyz * segment.B.w;
        go.transform.localRotation = Quaternion.LookRotation(segment.B.xyz);
        var particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = true; main.playOnAwake = false; main.maxParticles = kind == 1 ? 96 : 128;
        main.startLifetime = kind == 1 ? new ParticleSystem.MinMaxCurve(1.5f, 3f) : new ParticleSystem.MinMaxCurve(.5f, 1.2f);
        main.startSpeed = kind == 1 ? new ParticleSystem.MinMaxCurve(1f, 3f) : new ParticleSystem.MinMaxCurve(3f, 8f);
        main.startSize = kind == 1 ? new ParticleSystem.MinMaxCurve(3f, 8f) : new ParticleSystem.MinMaxCurve(.5f, 1.5f);
        main.startColor = new Color(.9f, .97f, 1f, kind == 1 ? .32f : .9f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = particles.emission; emission.rateOverTime = kind == 1 ? 28f : 70f;
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = kind == 1 ? 55f : 35f; shape.radius = segment.Width(kind == 2 ? 0f : 1f) * .8f;
        var color = particles.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var size = particles.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .5f, 1f, kind == 1 ? 2f : 1f));
        if (kind == 0)
        {
            var force = particles.forceOverLifetime; force.enabled = true;
            force.space = ParticleSystemSimulationSpace.Local; force.z = -9.81f;
        }
        var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = _sprayMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        _objects.Add(go);
        return particles;
    }

    public void Tick(Vector3 observer)
    {
        Array.Fill(_nearDistances, 250f * 250f);
        Array.Fill(_nearFalls, -1);
        for (int i = 0; i < _falls.Count; i++)
        {
            var fall = _falls[i];
            float d = (_planet.TransformPoint((Vector3)fall.B.xyz * fall.B.w) - observer).sqrMagnitude;
            for (int slot = 0; slot < _spray.Count; slot++)
            {
                if (d >= _nearDistances[slot]) continue;
                for (int j = _spray.Count - 1; j > slot; j--)
                {
                    _nearDistances[j] = _nearDistances[j - 1];
                    _nearFalls[j] = _nearFalls[j - 1];
                }
                _nearDistances[slot] = d; _nearFalls[slot] = i;
                break;
            }
        }
        for (int slot = 0; slot < _spray.Count; slot++)
        {
            var spray = _spray[slot];
            int fallIndex = _nearFalls[slot];
            if (_sprayAssignments[slot] == fallIndex) continue;
            spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _mist[slot].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _lipSpray[slot].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _sprayAssignments[slot] = fallIndex;
            if (fallIndex < 0) continue;
            var fall = _falls[fallIndex];
            Place(spray, false, false);
            Place(_mist[slot], false, true);
            Place(_lipSpray[slot], true, false);
            void Place(ParticleSystem particles, bool lip, bool mist)
            {
                Vector3 up = lip ? (Vector3)fall.A.xyz : (Vector3)fall.B.xyz;
                Vector3 tangent = ((Vector3)fall.B.xyz - (Vector3)fall.A.xyz).normalized;
                particles.transform.localPosition = up * ((lip ? fall.A.w : fall.B.w) + .4f);
                particles.transform.localRotation = Quaternion.LookRotation(lip ? tangent + up * .3f : up);
                float width = fall.Width(lip ? 0f : 1f);
                var shape = particles.shape; shape.radius = width * .8f;
                var main = particles.main;
                float scale = Mathf.Clamp(width / 10f, .5f, 4f);
                main.startSize = mist ? new ParticleSystem.MinMaxCurve(3f * scale, 7f * scale) : new ParticleSystem.MinMaxCurve(.5f * scale, 1.8f * scale);
                particles.Play();
            }
        }
    }

    public void Dispose()
    {
        foreach (var filter in VolumeSurfaces) WaterSurfaceRegistry.Unregister(filter);
        VolumeSurfaces.Clear();
        foreach (var go in _objects) Destroy(go);
        foreach (var mesh in _meshes) Destroy(mesh);
        Destroy(_material); Destroy(_sprayMaterial);
        _objects.Clear(); _meshes.Clear(); _spray.Clear(); _mist.Clear(); _lipSpray.Clear();
    }

    static void Destroy(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}

