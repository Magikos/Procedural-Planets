using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the creatures the residency service is simulating. It is a separate object because
/// <see cref="CreatureResidencyService"/> is authority code: a dedicated server runs the residency and has no
/// renderer at all, so nothing about drawing may sit inside it.
/// </summary>
public sealed class CreatureView : System.IDisposable
{
    // A per-material property name, not a global: it cannot collide across shaders, so it stays here.
    static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

    readonly Transform _parent;
    readonly Dictionary<ulong, Transform> _bodies = new();
    readonly Dictionary<ulong, CreatureAnimationView> _animated = new();
    readonly Dictionary<ulong, BirdAnimationView> _birds = new();
    readonly Dictionary<int, Material> _materials = new();
    readonly Dictionary<ulong, Transform> _corpseBodies = new();
    readonly Dictionary<int, Material> _corpseMaterials = new();
    readonly List<CreatureCorpse> _corpseScratch = new();
    readonly List<ulong> _stale = new();

    /// <summary>The find-the-wildlife debug view. Off costs nothing: it is not even hooked to the pipeline.</summary>
    public CreaturePredatorVision PredatorVision { get; } = new();

    bool _visible = true;
    IGroundingProvider _grounding;

    public CreatureView(Transform parent) => _parent = parent;
    public void ConfigureGrounding(IGroundingProvider grounding) => _grounding = grounding;

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

    /// <summary>
    /// Draw the carcasses near the observer. Separate from <see cref="Sync"/> because a body is not a
    /// creature: it has no slot, no brain and no bubble, and it outlives the animal by days.
    /// </summary>
    public void SyncCorpses(CreatureCorpseStore corpses, CreatureLibraryDto library,
        Vector3 observerWorldPos, float radiusMeters)
    {
        if (_parent == null) return;

        if (corpses == null)
        {
            _corpseScratch.Clear();
        }
        else
        {
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            corpses.CollectNear(observerWorldPos, radiusMeters, now, _corpseScratch);

            for (int i = 0; i < _corpseScratch.Count; i++)
            {
                CreatureCorpse c = _corpseScratch[i];
                CorpseStage stage = corpses.StageOf(c, now);
                float height = library?.At(c.SpeciesIndex)?.BodyHeightMeters ?? 1f;

                if (!_corpseBodies.TryGetValue(c.Id.Value, out Transform body) || body == null)
                {
                    body = CreateCorpse(c);
                    _corpseBodies[c.Id.Value] = body;
                }

                // Lying down: the capsule's long axis goes along the surface rather than up it. Stage shrinks
                // and pales it, so bones read as a small light heap without a second mesh.
                float shrink = StageShrink(stage);
                body.SetPositionAndRotation(c.Position, c.Rotation * Quaternion.Euler(90f, 0f, 0f));
                body.localScale = new Vector3(height * 0.35f * shrink, height * 0.5f * shrink, height * 0.35f * shrink);

                if (body.TryGetComponent(out Renderer renderer))
                    renderer.sharedMaterial = EnsureCorpseMaterial(stage, c.SpeciesIndex, library?.At(c.SpeciesIndex));
            }
        }

        _stale.Clear();
        foreach (KeyValuePair<ulong, Transform> kv in _corpseBodies)
        {
            bool stillThere = false;
            for (int i = 0; i < _corpseScratch.Count && !stillThere; i++)
                stillThere = _corpseScratch[i].Id.Value == kv.Key;
            if (!stillThere) _stale.Add(kv.Key);
        }
        for (int i = 0; i < _stale.Count; i++)
        {
            if (_corpseBodies.TryGetValue(_stale[i], out Transform body) && body != null)
                Object.Destroy(body.gameObject);
            _corpseBodies.Remove(_stale[i]);
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
            if (_animated.TryGetValue(c.Id.Value, out CreatureAnimationView animation))
            {
                animation.Resting = c.Behaviour == CreatureBehaviour.Rest;
                animation.Sleeping = c.Behaviour == CreatureBehaviour.Sleep;
                animation.Eating = c.Behaviour == CreatureBehaviour.Feed;
                animation.Drinking = c.Behaviour == CreatureBehaviour.Drink;
                animation.Tick(c.Velocity, c.Up, Time.deltaTime, _grounding);
            }
            if (_birds.TryGetValue(c.Id.Value, out BirdAnimationView bird))
                bird.Tick(c.Behaviour == CreatureBehaviour.Perch && c.AltitudeMeters <= 0.01f,
                    c.Behaviour == CreatureBehaviour.Flee, Time.deltaTime);
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
            if (_birds.Remove(_stale[i], out BirdAnimationView bird))
                bird.Dispose();
            else if (_animated.Remove(_stale[i], out CreatureAnimationView animation))
                animation.Dispose();
            else if (_bodies.TryGetValue(_stale[i], out Transform body) && body != null)
                Object.Destroy(body.gameObject);
            _bodies.Remove(_stale[i]);
        }
    }

    Transform CreateBody(in CreatureResidencyService.LiveCreature c, CreatureSpeciesDto species, float height)
    {
        if (species != null && species.CruiseAltitudeMeters > 0f)
        {
            var bird = new BirdAnimationView(_parent, c.Id.Value, height);
            _birds.Add(c.Id.Value, bird);
            bird.Root.gameObject.SetActive(_visible);
            return bird.Root;
        }
        if (species?.Visuals != null)
        {
            var animation = new CreatureAnimationView(_parent, c.Id.Value, species.Visuals, height);
            _animated.Add(c.Id.Value, animation);
            animation.Root.gameObject.SetActive(_visible);
            return animation.Root;
        }
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

    Transform CreateCorpse(in CreatureCorpse corpse)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Carcass " + corpse.Id.Value.ToString("X");
        go.transform.SetParent(_parent, worldPositionStays: true);
        if (go.TryGetComponent(out Collider collider))
            Object.Destroy(collider);
        go.SetActive(_visible);
        return go.transform;
    }

    // Decay reads as shrinking as well as darkening: a body collapses in on itself, and by the bones stage
    // there is much less of it than there was.
    static float StageShrink(CorpseStage stage) => stage switch
    {
        CorpseStage.Fresh => 1f,
        CorpseStage.Bloated => 1.1f,
        CorpseStage.Rotting => 0.85f,
        _ => 0.5f,
    };

    // ponytail: colour per stage on the placeholder capsule. Real carcass meshes replace this file's shapes
    // wholesale, the same way they replace the standing capsules.
    Material EnsureCorpseMaterial(CorpseStage stage, int speciesIndex, CreatureSpeciesDto species)
    {
        // Keyed by species AND stage. Keying on the stage alone would give a rotting rabbit the deer material
        // that happened to be built first - the same silent wrong result the live-creature cache guards against.
        int key = speciesIndex * 8 + (int)stage;
        if (_corpseMaterials.TryGetValue(key, out Material cached) && cached != null)
            return cached;

        Shader shader = Shader.Find("Planet/PropLit");
        if (shader == null) return null;

        Color live = species?.BodyColor ?? new Color(0.45f, 0.33f, 0.22f);
        Color color = stage switch
        {
            CorpseStage.Fresh => live * 0.8f,
            CorpseStage.Bloated => Color.Lerp(live * 0.7f, new Color(0.55f, 0.45f, 0.35f), 0.5f),
            CorpseStage.Rotting => new Color(0.24f, 0.20f, 0.16f),
            _ => new Color(0.86f, 0.84f, 0.78f),   // bones
        };

        var material = new Material(shader) { name = "Carcass " + stage + " (runtime)" };
        material.SetColor(_baseColorId, color);
        _corpseMaterials[key] = material;
        return material;
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
        foreach (BirdAnimationView bird in _birds.Values) bird.Dispose();
        foreach (CreatureAnimationView animation in _animated.Values)
            animation.Dispose();
        foreach (KeyValuePair<ulong, Transform> kv in _bodies)
            if (!_animated.ContainsKey(kv.Key) && !_birds.ContainsKey(kv.Key) && kv.Value != null) Object.Destroy(kv.Value.gameObject);
        _birds.Clear();
        _animated.Clear();
        _bodies.Clear();
        foreach (KeyValuePair<int, Material> kv in _materials)
            if (kv.Value != null) Object.Destroy(kv.Value);
        _materials.Clear();

        foreach (KeyValuePair<ulong, Transform> kv in _corpseBodies)
            if (kv.Value != null) Object.Destroy(kv.Value.gameObject);
        _corpseBodies.Clear();
        foreach (KeyValuePair<int, Material> kv in _corpseMaterials)
            if (kv.Value != null) Object.Destroy(kv.Value);
        _corpseMaterials.Clear();
    }

    public void Dispose()
    {
        Clear();
        PredatorVision.Dispose();
    }
}
