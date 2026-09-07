using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Skinned fish presentation. The population owns positions and water constraints.</summary>
public sealed class FishView : IDisposable
{
    sealed class Art
    {
        public GameObject Model;
        public AnimationClip Clip;
        public Material Material;
        public Bounds Bounds;
        public bool Measured;
    }

    sealed class Body
    {
        public Transform Root;
        public PlayableGraph Graph;
        public AnimationClipPlayable Clip;
        public void Dispose() { if (Graph.IsValid()) Graph.Destroy(); if (Root != null) Release(Root.gameObject); }
    }

    readonly Transform _parent;
    readonly Dictionary<string, Art> _art = new();
    readonly Dictionary<ulong, Body[]> _groups = new();
    readonly HashSet<ulong> _live = new();
    readonly List<ulong> _stale = new();
    public int BodyCount { get; private set; }

    public FishView(Transform parent) => _parent = parent;

    public void Sync(IReadOnlyList<FishPopulation.Group> groups, float dt)
    {
        _live.Clear();
        BodyCount = 0;
        foreach (var group in groups)
        {
            _live.Add(group.Id.Value);
            if (!_groups.TryGetValue(group.Id.Value, out var bodies))
            {
                bodies = new Body[group.School.Positions.Count];
                try
                {
                    for (int i = 0; i < bodies.Length; i++) bodies[i] = Create(group.Species, group.Id.Value, i);
                    _groups.Add(group.Id.Value, bodies);
                }
                catch { foreach (var body in bodies) body?.Dispose(); throw; }
            }
            for (int i = 0; i < bodies.Length; i++)
            {
                Body body = bodies[i];
                body.Root.SetPositionAndRotation(group.School.Positions[i],
                    Quaternion.LookRotation(group.School.Headings[i], group.School.Normals[i]));
                body.Graph.Evaluate(Mathf.Max(0f, dt));
                BodyCount++;
            }
        }
        _stale.Clear();
        foreach (var group in _groups) if (!_live.Contains(group.Key)) _stale.Add(group.Key);
        foreach (ulong id in _stale) { foreach (Body body in _groups[id]) body.Dispose(); _groups.Remove(id); }
    }

    Body Create(FishSpecies species, ulong id, int index)
    {
        if (!_art.TryGetValue(species.Model, out Art art))
        {
            string path = "Wildlife/Fish/" + species.Model;
            var model = Resources.Load<GameObject>(path);
            var clip = Resources.Load<AnimationClip>(path + "_Swim");
            var texture = Resources.Load<Texture2D>(path);
            var shader = Shader.Find("Planet/PropLit");
            if (model == null || clip == null || texture == null || shader == null)
                throw new InvalidOperationException("Fish art is incomplete: " + path);
            art = new Art { Model = model, Clip = clip, Material = new Material(shader) };
            art.Material.SetTexture("_BaseMap", texture);
            art.Material.SetColor("_BaseColor", Color.white);
            _art.Add(species.Model, art);
        }
        var body = new Body { Root = new GameObject(species.Name + " " + id + ":" + index).transform };
        body.Root.SetParent(_parent, false);
        // Species dimensions and water clearances are world metres, even under a scaled planet.
        Vector3 parentScale = _parent != null ? _parent.lossyScale : Vector3.one;
        float largestScale = Mathf.Max(Mathf.Abs(parentScale.x), Mathf.Abs(parentScale.y), Mathf.Abs(parentScale.z));
        body.Root.localScale = Vector3.one / Mathf.Max(0.0001f, largestScale);
        try
        {
            GameObject model = UnityEngine.Object.Instantiate(art.Model, body.Root);
            // The catalog fish faces +X; the shark and swimming controller face +Z.
            if (species.Model == "SmallFish") model.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Animator animator = model.GetComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = art.Material;
            body.Graph = PlayableGraph.Create("Fish swim " + id + ":" + index);
            body.Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            body.Clip = AnimationClipPlayable.Create(body.Graph, art.Clip);
            var output = AnimationPlayableOutput.Create(body.Graph, "Swim", animator);
            output.SetSourcePlayable(body.Clip);
            body.Graph.Play();
            if (!art.Measured)
            {
                // Imported skinned bounds are relative to the root bone, not the renderer transform.
                // Measure their animated envelope once per model without applying the import scale twice.
                bool first = true;
                for (int frame = 0; frame < 16; frame++)
                {
                    body.Clip.SetTime(art.Clip.length * frame / 16f);
                    body.Graph.Evaluate(0f);
                    foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        Transform space = skin.rootBone != null ? skin.rootBone : skin.transform;
                        Bounds bounds = skin.localBounds;
                        for (int corner = 0; corner < 8; corner++)
                        {
                            Vector3 offset = Vector3.Scale(bounds.extents, new Vector3(
                                (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                            Vector3 point = body.Root.InverseTransformPoint(space.TransformPoint(bounds.center + offset));
                            if (first) { art.Bounds = new Bounds(point, Vector3.zero); first = false; }
                            else art.Bounds.Encapsulate(point);
                        }
                    }
                }
                art.Measured = true;
            }
            float scale = Mathf.Min(species.Length / Mathf.Max(0.001f, art.Bounds.size.z),
                species.Clearance * 0.95f / Mathf.Max(0.001f, art.Bounds.extents.magnitude));
            model.transform.localScale *= scale;
            model.transform.localPosition -= art.Bounds.center * scale;
            body.Clip.SetTime(((id % 997UL) / 997f + index * 0.137f) * art.Clip.length);
            body.Graph.Evaluate(0f);
            return body;
        }
        catch { body.Dispose(); throw; }
    }

    public void Clear()
    {
        foreach (var group in _groups.Values) foreach (Body body in group) body.Dispose();
        _groups.Clear();
        _live.Clear();
        BodyCount = 0;
    }

    public void Dispose()
    {
        Clear();
        foreach (Art art in _art.Values) Release(art.Material);
        _art.Clear();
    }

    static void Release(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj);
    }
}
