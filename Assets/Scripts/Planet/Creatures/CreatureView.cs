using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the creatures the residency service is simulating. It is a separate object because
/// <see cref="CreatureResidencyService"/> is authority code: a dedicated server runs the residency and has no
/// renderer at all, so nothing about drawing may sit inside it.
/// </summary>
// ponytail: a capsule per creature, which is the whole of "one placeholder animal". Rigged meshes and
// animation are explicitly out of the first slice (design doc section 12); the swap is this file only, because
// the service hands over a pose and knows nothing about what draws it.
public sealed class CreatureView : System.IDisposable
{
    // A per-material property name, not a global: it cannot collide across shaders, so it stays here.
    static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

    readonly Transform _parent;
    readonly Dictionary<ulong, Transform> _bodies = new();
    readonly Dictionary<int, Material> _materials = new();
    readonly List<ulong> _stale = new();

    /// <summary>The find-the-wildlife debug view. Off costs nothing: it is not even hooked to the pipeline.</summary>
    public CreaturePredatorVision PredatorVision { get; } = new();

    bool _visible = true;

    public CreatureView(Transform parent) => _parent = parent;

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            foreach (KeyValuePair<ulong, Transform> kv in _bodies)
                if (kv.Value != null) kv.Value.gameObject.SetActive(value);
        }
    }

    public void Sync(IReadOnlyList<CreatureResidencyService.LiveCreature> live, CreatureLibraryDto library)
    {
        PredatorVision.Sync(live, library);
        if (_parent == null) return;

        for (int i = 0; i < live.Count; i++)
        {
            CreatureResidencyService.LiveCreature c = live[i];
            CreatureSpeciesDto species = library?.At(c.SpeciesIndex);
            float height = species?.BodyHeightMeters ?? 1.7f;

            if (!_bodies.TryGetValue(c.Id.Value, out Transform body) || body == null)
            {
                body = CreateBody(c, species, height);
                _bodies[c.Id.Value] = body;
            }
            body.SetPositionAndRotation(c.Position, Quaternion.LookRotation(c.Forward, c.Up));
        }

        // A body whose creature is no longer live is destroyed, not hidden: demotion is unbounded in time, and
        // a pool of hidden capsules would grow with every territory the player has ever walked through.
        _stale.Clear();
        foreach (KeyValuePair<ulong, Transform> kv in _bodies)
        {
            bool stillLive = false;
            for (int i = 0; i < live.Count && !stillLive; i++)
                stillLive = live[i].Id.Value == kv.Key;
            if (!stillLive) _stale.Add(kv.Key);
        }
        for (int i = 0; i < _stale.Count; i++)
        {
            if (_bodies.TryGetValue(_stale[i], out Transform body) && body != null)
                Object.Destroy(body.gameObject);
            _bodies.Remove(_stale[i]);
        }
    }

    Transform CreateBody(in CreatureResidencyService.LiveCreature c, CreatureSpeciesDto species, float height)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Creature " + CreatureKey.Describe(c.Id);
        go.transform.SetParent(_parent, worldPositionStays: true);

        // The terrain has no colliders either; a primitive's collider would be the only physics body out here
        // and would fight the analytic grounding rather than help it.
        if (go.TryGetComponent(out Collider collider))
            Object.Destroy(collider);

        // Unity's capsule primitive is 2 m tall with its origin at the centre, which is the offset the driver
        // is grounded with.
        go.transform.localScale = new Vector3(height * 0.35f, height * 0.5f, height * 0.35f);
        go.SetActive(_visible);

        if (go.TryGetComponent(out Renderer renderer))
            renderer.sharedMaterial = EnsureMaterial(c.SpeciesIndex, species);
        return go.transform;
    }

    // One material per species, shared by every body of it. Keyed rather than single because a second species
    // sharing the first one's colour is a silent wrong result, not a missing feature.
    Material EnsureMaterial(int speciesIndex, CreatureSpeciesDto species)
    {
        if (_materials.TryGetValue(speciesIndex, out Material cached) && cached != null)
            return cached;

        Shader shader = Shader.Find("Planet/PropLit");   // planet-aware: the night hemisphere darkens it
        if (shader == null) return null;

        var material = new Material(shader) { name = "Creature " + (species?.DisplayName ?? "?") + " (runtime)" };
        if (species != null) material.SetColor(_baseColorId, species.BodyColor);
        _materials[speciesIndex] = material;
        return material;
    }

    /// <summary>
    /// Drop this world's bodies. Called on regeneration, where <see cref="Dispose"/> would be wrong: it would
    /// silently switch the predator view off, and a debug mode that turns itself off when you regenerate is
    /// worse than one that does not exist.
    /// </summary>
    public void Clear()
    {
        foreach (KeyValuePair<ulong, Transform> kv in _bodies)
            if (kv.Value != null) Object.Destroy(kv.Value.gameObject);
        _bodies.Clear();
        foreach (KeyValuePair<int, Material> kv in _materials)
            if (kv.Value != null) Object.Destroy(kv.Value);
        _materials.Clear();
    }

    public void Dispose()
    {
        Clear();
        PredatorVision.Dispose();
    }
}
