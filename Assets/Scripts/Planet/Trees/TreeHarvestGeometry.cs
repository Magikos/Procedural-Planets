using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Harvest parts share the standing skeleton and a common cut pivot.</summary>
public static class TreeHarvestGeometry
{
    public const int MaxSections = 16;

    public static void BuildSupport(GeneratedTree tree)
    {
        Vector3[] Sample(params Mesh[] meshes)
        {
            var points = new List<Vector3>();
            foreach (var mesh in meshes)
            {
                if (mesh == null || mesh.vertexCount == 0) continue;
                var vertices = mesh.vertices;
                // Use referenced vertices: conifer groups retain only a subset of the source triangles.
                var indices = mesh.triangles;
                int step = Mathf.Max(1, indices.Length / 48);
                for (int i = 0; i < indices.Length; i += step) points.Add(vertices[indices[i]]);
            }
            return points.ToArray();
        }
        tree.LogSupport = Sample(tree.Log);
        tree.BranchSupport = new Vector3[tree.BranchBark.Length][];
        for (int i = 0; i < tree.BranchSupport.Length; i++)
            tree.BranchSupport[i] = Sample(tree.BranchBark[i], tree.BranchFoliage[i]);
    }

    public static void Build(TreeSkeleton sk, GeneratedTree tree)
    {
        if (sk.Trunk == null || sk.Trunk.Centerline.Count < 2) return;
        TreeBranch trunk = sk.Trunk;
        // A height in metres keeps the axe contact reachable on tall trees.
        float cutHeight = Mathf.Min(0.75f, trunk.Tip.y * 0.2f);
        float cut = ParameterAtHeight(trunk, cutHeight);
        trunk.Sample(cut, out tree.CutPosition, out _, out _, out _);
        tree.ChopFraction = cut;
        tree.Stump = Slice(trunk, 0f, cut, Vector3.zero);
        tree.Log = Slice(trunk, cut, 1f, tree.CutPosition);
        tree.TrunkPoints = trunk.Centerline.ToArray();
        tree.TrunkRadii = trunk.Girth.ToArray();
        tree.IsSapling = tree.TrunkBaseGirth <= 0.20f && tree.Height <= 8f;

        float length = 0f;
        for (int i = 1; i < trunk.Centerline.Count; i++)
            length += Vector3.Distance(trunk.Centerline[i - 1], trunk.Centerline[i]);
        int count = Mathf.Clamp(Mathf.CeilToInt(length * (1f - cut) / 3.5f), 1, MaxSections);
        tree.LogSections = new Mesh[count];
        tree.SectionAnchors = new Vector3[count];
        float volume = 0f;
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.Lerp(cut, 1f, i / (float)count);
            float b = Mathf.Lerp(cut, 1f, (i + 1f) / count);
            tree.LogSections[i] = Slice(trunk, a, b, tree.CutPosition);
            trunk.Sample((a + b) * 0.5f, out Vector3 p, out _, out _, out float r);
            tree.SectionAnchors[i] = p - tree.CutPosition;
            volume += Mathf.PI * r * r * length * (b - a);
        }
        tree.WoodYield = tree.IsSapling ? Mathf.Clamp(Mathf.CeilToInt(volume * 4f), 1, 3)
            : Mathf.Max(count, Mathf.CeilToInt(volume * 3f));
        tree.ChopHp = tree.IsSapling ? 1 : Mathf.Clamp(Mathf.CeilToInt(tree.TrunkBaseGirth * 5f), 3, 12);

        const int groups = 3;
        var skeletons = new TreeSkeleton[groups];
        for (int i = 0; i < groups; i++) skeletons[i] = new TreeSkeleton();
        int Group(TreeBranch branch)
        {
            while (branch?.Parent != null && branch.Parent != trunk) branch = branch.Parent;
            float t = branch == null || branch == trunk ? 0.95f : branch.PositionOnParent;
            return Mathf.Clamp((int)(t * groups), 0, groups - 1);
        }
        foreach (TreeBranch branch in sk.Branches)
            if (!branch.IsTrunk) skeletons[Group(branch)].Branches.Add(branch);
        foreach (TreeSprout sprout in sk.Sprouts) skeletons[Group(sprout.Parent)].Sprouts.Add(sprout);
        tree.BranchBark = new Mesh[groups];
        tree.BranchFoliage = new Mesh[groups];
        tree.BranchAnchors = new Vector3[groups];
        for (int i = 0; i < groups; i++)
        {
            tree.BranchBark[i] = TreeTubeMesher.Build(skeletons[i]);
            Translate(tree.BranchBark[i], -tree.CutPosition);
            tree.BranchFoliage[i] = TreeLeafMesher.Build(skeletons[i]);
            Translate(tree.BranchFoliage[i], -tree.CutPosition);
            trunk.Sample((i + 0.5f) / groups, out Vector3 p, out _, out _, out _);
            tree.BranchAnchors[i] = p - tree.CutPosition;
        }
    }

    // Conifer foliage has no sprouts. Keep its existing triangles, partitioned into three height bands.
    public static void SplitConiferFoliage(GeneratedTree tree)
    {
        Mesh source = tree.Foliage;
        if (source == null || source.vertexCount == 0) return;
        Vector3[] vertices = source.vertices;
        int[] triangles = source.triangles;
        var indices = new List<int>[] { new(), new(), new() };
        Bounds bounds = source.bounds;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            float y = (vertices[triangles[i]].y + vertices[triangles[i + 1]].y + vertices[triangles[i + 2]].y) / 3f;
            int group = Mathf.Clamp((int)(Mathf.InverseLerp(bounds.min.y, bounds.max.y, y) * 3f), 0, 2);
            indices[group].Add(triangles[i]); indices[group].Add(triangles[i + 1]); indices[group].Add(triangles[i + 2]);
        }
        for (int i = 0; i < 3; i++)
        {
            Destroy(tree.BranchFoliage[i]);
            var mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = "Conifer branch group";
            mesh.SetTriangles(indices[i], 0);
            Translate(mesh, -tree.CutPosition);
            tree.BranchFoliage[i] = mesh;
        }
    }

    public static float ParameterAtHeight(TreeBranch trunk, float height)
    {
        for (int i = 1; i < trunk.Centerline.Count; i++)
            if (trunk.Centerline[i].y >= height)
                return (i - 1f + Mathf.InverseLerp(trunk.Centerline[i - 1].y, trunk.Centerline[i].y, height))
                    / (trunk.Centerline.Count - 1f);
        return 0.2f;
    }

    public static Mesh Slice(TreeBranch trunk, float from, float to, Vector3 origin)
    {
        var points = new List<Vector3>();
        var radii = new List<float>();
        trunk.Sample(from, out Vector3 first, out _, out _, out float radius);
        points.Add(first); radii.Add(radius);
        for (int i = 1; i < trunk.Centerline.Count - 1; i++)
        {
            float t = i / (float)(trunk.Centerline.Count - 1);
            if (t <= from || t >= to) continue;
            points.Add(trunk.Centerline[i]); radii.Add(trunk.Girth[i]);
        }
        trunk.Sample(to, out Vector3 last, out _, out _, out radius);
        points.Add(last); radii.Add(radius);
        float distance = 0f;
        float parameter = from * (trunk.Centerline.Count - 1);
        int complete = Mathf.FloorToInt(parameter);
        for (int i = 1; i <= complete; i++) distance += Vector3.Distance(trunk.Centerline[i - 1], trunk.Centerline[i]);
        if (complete < trunk.Centerline.Count - 1) distance += Vector3.Distance(trunk.Centerline[complete], first);
        Mesh mesh = TreeTubeMesher.BuildCappedTube(points, radii, trunk.RadialSides, distance);
        Translate(mesh, first - origin);
        return mesh;
    }

    public static void Translate(Mesh mesh, Vector3 offset)
    {
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] += offset;
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
    }

    static void Destroy(UnityEngine.Object obj)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
        else UnityEngine.Object.DestroyImmediate(obj);
    }
}
