using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Prototype anatomical scaffold fitted to the accepted rig, with separate flesh and age controls.</summary>
public sealed class CreatureCorpsePresentation : IDisposable
{
    readonly Transform _root;
    readonly List<(SkinnedMeshRenderer renderer, Mesh original, Mesh mesh, int[][] triangles, float[][] order)> _flesh = new();
    readonly Material _boneMaterial;
    GameObject _skeleton;
    Mesh _boneMesh;
    readonly MaterialPropertyBlock _tint = new();
    CorpseStage? _lastStage;
    int _lastCoverage = -1;

    public CreatureCorpsePresentation(Transform root)
    {
        _root = root;
        _boneMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        _boneMaterial.color = new Color(.72f, .66f, .48f); _boneMaterial.SetFloat("_Smoothness", 0f);
    }
    public void Tick(CreatureCorpseResource carrion, bool deathPoseSettled)
        => Tick(carrion.MeatFraction, carrion.Stage, deathPoseSettled);

    public void Tick(double meatFraction, CorpseStage stage, bool deathPoseSettled)
    {
        // Simulation can advance while animation is disabled. Only capture an evaluated death pose.
        if (_skeleton == null && deathPoseSettled) Build();
        if (_skeleton == null) return;
        int coverage = Mathf.CeilToInt(Mathf.Clamp01((float)meatFraction) * 20f);
        _skeleton.SetActive(coverage < 20 && stage != CorpseStage.Gone);
        if (coverage != _lastCoverage)
        {
            foreach (var part in _flesh)
            {
                for (int s = 0; s < part.triangles.Length; s++)
                {
                    var kept = new List<int>();
                    for (int t = 0; t < part.order[s].Length; t++)
                        if (coverage > 0 && part.order[s][t] <= coverage / 20f)
                        { kept.Add(part.triangles[s][t * 3]); kept.Add(part.triangles[s][t * 3 + 1]); kept.Add(part.triangles[s][t * 3 + 2]); }
                    part.mesh.SetTriangles(kept, s, false);
                }
            }
            _lastCoverage = coverage;
        }
        if (_lastStage == stage) return;
        Color tint = stage >= CorpseStage.Rotting ? new(.36f, .32f, .22f) :
            stage == CorpseStage.Bloated ? new(.65f, .6f, .47f) : Color.white;
        _tint.SetColor("_BaseColor", tint);
        foreach (var part in _flesh) part.renderer.SetPropertyBlock(_tint);
        _lastStage = stage;
    }
    void Build()
    {
        _skeleton = new GameObject("Skeletal scaffold"); _skeleton.transform.SetParent(_root, false);
        foreach (var renderer in _root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Mesh original = renderer.sharedMesh;
            var mesh = UnityEngine.Object.Instantiate(original);
            var vertices = original.vertices;
            var triangles = new int[original.subMeshCount][]; var order = new float[triangles.Length][];
            var bounds = original.bounds;
            for (int s = 0; s < triangles.Length; s++)
            {
                triangles[s] = original.GetTriangles(s); order[s] = new float[triangles[s].Length / 3];
                for (int t = 0; t < order[s].Length; t++)
                {
                    Vector3 p = (vertices[triangles[s][t * 3]] + vertices[triangles[s][t * 3 + 1]] + vertices[triangles[s][t * 3 + 2]]) / 3f;
                    Vector3 n = p - bounds.center;
                    float d = new Vector3(n.x / Mathf.Max(.01f, bounds.extents.x), n.y / Mathf.Max(.01f, bounds.extents.y),
                        n.z / Mathf.Max(.01f, bounds.extents.z)).magnitude;
                    order[s][t] = Mathf.Clamp01(1f - d / 1.8f);
                }
            }
            NormalizeCoverage(vertices, triangles, order);
            renderer.sharedMesh = mesh; _flesh.Add((renderer, original, mesh, triangles, order));
        }
        var rig = _root.GetComponentInChildren<ProceduralRigDefinition>();
        if (rig == null) return;
        float size = PosedSize();
        float thickness = size * .008f;
        bool legless = rig.Feet.Length == 0 && rig.SurfaceChains.Length > 0;
        if (legless)
            foreach (var chain in rig.SurfaceChains)
                for (int i = 1; i < chain.Bones.Length; i++) Bone(chain.Bones[i - 1].position, chain.Bones[i].position, thickness);
        foreach (var foot in rig.Feet)
            for (int i = 1; i < foot.Bones.Length; i++) Bone(foot.Bones[i - 1].position, foot.Bones[i].position, thickness);
        var axial = rig.Spine.Concat(rig.Look).Distinct().ToArray();
        if (!legless)
            for (int i = 1; i < axial.Length; i++) Bone(axial[i - 1].position, axial[i].position, thickness * 1.5f);
        if (rig.Feet.Length >= 2 && rig.Spine.Length >= 2 && axial.Length >= 2)
        {
            Vector3 back = axial[0].position, front = axial[Mathf.Min(2, axial.Length - 1)].position;
            Vector3 forward = (front - back).normalized;
            // Imported bone axes need not match anatomical axes. Fit ribs toward the posed feet.
            Vector3 feet = Vector3.zero;
            foreach (var foot in rig.Feet) feet += foot.Bones[^1].position;
            feet /= rig.Feet.Length;
            Vector3 down = VentralDirection(forward, feet - (back + front) * .5f, -_root.up);
            Vector3 side = Vector3.Cross(down, forward).normalized;
            for (int rib = 0; rib < 8; rib++)
            {
                float t = (rib + .5f) / 8f; Vector3 center = Vector3.Lerp(back, front, t);
                float width = size * .07f * Mathf.Sin(.3f + t * 2.4f);
                foreach (float sign in new[] { -1f, 1f })
                {
                    Vector3 previous = center;
                    for (int j = 1; j <= 5; j++)
                    {
                        float angle = j / 5f * Mathf.PI;
                        Vector3 point = center + side * (sign * Mathf.Sin(angle) * width) + down * ((1 - Mathf.Cos(angle)) * size * .07f);
                        Bone(previous, point, thickness * .65f); previous = point;
                    }
                }
            }
        }
        if (!legless && rig.Look.Length > 0)
        {
            var head = rig.Look[^1];
            var skull = GameObject.CreatePrimitive(PrimitiveType.Sphere); skull.name = "Skull blockout";
            skull.transform.SetParent(_skeleton.transform, true); skull.transform.SetPositionAndRotation(head.position, head.rotation);
            skull.transform.localScale = new Vector3(size * .055f, size * .07f, size * .12f);
            DestroyOwned(skull.GetComponent<Collider>()); skull.GetComponent<Renderer>().sharedMaterial = _boneMaterial;
        }
        CombineSkeleton();
    }

    static void NormalizeCoverage(Vector3[] vertices, int[][] triangles, float[][] order)
    {
        var ranked = new List<(int submesh, int triangle, float score, double area)>();
        double total = 0d;
        for (int s = 0; s < triangles.Length; s++)
            for (int t = 0; t < order[s].Length; t++)
            {
                int i = t * 3;
                double area = Vector3.Cross(vertices[triangles[s][i + 1]] - vertices[triangles[s][i]],
                    vertices[triangles[s][i + 2]] - vertices[triangles[s][i]]).magnitude * .5d;
                ranked.Add((s, t, order[s][t], area)); total += area;
            }
        ranked.Sort((a, b) => {
            int comparison = a.score.CompareTo(b.score);
            if (comparison != 0) return comparison;
            comparison = a.submesh.CompareTo(b.submesh);
            return comparison != 0 ? comparison : a.triangle.CompareTo(b.triangle);
        });
        double retained = 0d;
        foreach (var triangle in ranked)
        {
            // Midpoint thresholds select the closest whole-triangle surface area at each coverage step.
            order[triangle.submesh][triangle.triangle] = total > 0d
                ? (float)((retained + triangle.area * .5d) / total) : 1f;
            retained += triangle.area;
        }
    }

    static Vector3 VentralDirection(Vector3 forward, Vector3 towardFeet, Vector3 fallback)
    {
        Vector3 down = Vector3.ProjectOnPlane(towardFeet, forward);
        if (down.sqrMagnitude < .000001f) down = Vector3.ProjectOnPlane(fallback, forward);
        if (down.sqrMagnitude < .000001f) down = Vector3.Cross(forward, Vector3.right);
        return down.normalized;
    }

    void CombineSkeleton()
    {
        var parts = _skeleton.GetComponentsInChildren<MeshFilter>();
        if (parts.Length == 0) return;
        var combine = new CombineInstance[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            combine[i] = new CombineInstance { mesh = parts[i].sharedMesh,
                transform = _skeleton.transform.worldToLocalMatrix * parts[i].transform.localToWorldMatrix };
        _boneMesh = new Mesh { name = "Carcass skeleton", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        _boneMesh.CombineMeshes(combine, true, true);
        foreach (var part in parts) { part.gameObject.SetActive(false); DestroyOwned(part.gameObject); }
        _skeleton.AddComponent<MeshFilter>().sharedMesh = _boneMesh;
        _skeleton.AddComponent<MeshRenderer>().sharedMaterial = _boneMaterial;
    }

    float PosedSize()
    {
        var mesh = new Mesh();
        bool found = false;
        Bounds bounds = default;
        try
        {
            foreach (var renderer in _root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                // Imported culling bounds can contain large empty volumes. Fit anatomy to evaluated vertices instead.
                renderer.BakeMesh(mesh, true);
                foreach (var vertex in mesh.vertices)
                {
                    Vector3 local = _root.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                    if (!found) { bounds = new Bounds(local, Vector3.zero); found = true; }
                    else bounds.Encapsulate(local);
                }
            }
            return found ? Vector3.Scale(bounds.size, _root.lossyScale).magnitude : 2f;
        }
        finally { DestroyOwned(mesh); }
    }

    static void DestroyOwned(UnityEngine.Object value)
    { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    void Bone(Vector3 start, Vector3 end, float radius)
    {
        if ((end - start).sqrMagnitude < .00001f) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = "Bone";
        go.transform.SetParent(_skeleton.transform, true);
        go.transform.SetPositionAndRotation((start + end) * .5f, Quaternion.FromToRotation(Vector3.up, end - start));
        go.transform.localScale = new Vector3(radius * 2f, (end - start).magnitude * .5f, radius * 2f);
        DestroyOwned(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial = _boneMaterial;
    }
    public void Dispose()
    {
        foreach (var p in _flesh) { if (p.renderer != null) p.renderer.sharedMesh = p.original; DestroyOwned(p.mesh); }
        _flesh.Clear(); DestroyOwned(_boneMaterial);
        if (_boneMesh != null) DestroyOwned(_boneMesh);
        if (_skeleton != null) { _skeleton.SetActive(false); DestroyOwned(_skeleton); }
    }
}
