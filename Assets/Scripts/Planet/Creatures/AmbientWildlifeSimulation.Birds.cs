using System.Collections.Generic;
using UnityEngine;

public sealed partial class AmbientWildlifeSimulation
{
    sealed class Flock
    {
        public Vector3 Position, Forward;
        public float Age;
        public bool Formation;
        public readonly List<Animal> Birds = new();
    }
    readonly List<Flock> _flocks = new();

    void TickFlocks(Vector3 observer, float sun, long now, float dt)
    {
        AmbientSwarmProfile profile = null;
        foreach (var candidate in _profiles)
            if (candidate.Kind == AmbientSwarmKind.Birds) { profile = candidate; break; }
        if (profile == null) return;
        int wanted = profile.CountAt(sun);
        for (int i = _flocks.Count - 1; i >= 0; i--)
        {
            bool nearby = false;
            foreach (Animal bird in _flocks[i].Birds)
                if (Vector3.Distance(bird.Position, observer) < 250f) { nearby = true; break; }
            // A resting member keeps its flock alive until it can take off and rejoin.
            if (i < wanted && nearby) continue;
            foreach (Animal bird in _flocks[i].Birds) bird.ReleaseTarget();
            _flocks.RemoveAt(i);
        }
        while (_flocks.Count < wanted)
        {
            if (!_place(profile, observer, out Vector3 position) || Vector3.Distance(position, observer) >= 250f) break;
            uint seed = ScatterHash.Mix(++_sequence);
            Vector3 up = (position - _center).normalized;
            var flock = new Flock { Position = position, Forward =
                Quaternion.AngleAxis(ScatterHash.To01(seed) * 360f, up) * CharacterMath.ArbitraryTangent(up),
                Formation = (seed & 1) == 0 };
            int count = (seed % 3) switch { 0 => 1, 1 => 7, _ => 24 };
            for (int i = 0; i < count; i++)
            {
                var bird = new Animal(_ids.Next(), profile, position + BirdSlot(flock, i, up),
                    ScatterHash.Slot(seed, i), up) { Forward = flock.Forward, Alpha = 1f };
                flock.Birds.Add(bird);
            }
            _flocks.Add(flock);
        }

        foreach (Flock flock in _flocks)
        {
            flock.Age += dt;
            Vector3 up = (flock.Position - _center).normalized;
            flock.Forward = Vector3.ProjectOnPlane(Quaternion.AngleAxis(Mathf.Sin(flock.Age * 0.15f) * dt * 5f, up)
                * flock.Forward, up).normalized;
            flock.Position += flock.Forward * (6f * dt);
            up = (flock.Position - _center).normalized;
            if (_surface.TryGetSurfaceRadius(up, out float ground))
                flock.Position = _center + up * (Mathf.Max(ground,
                    Mathf.Max(_seaRadius, _water.RadiusAt(flock.Position, up))) + profile.HeightMeters);
            for (int i = 0; i < flock.Birds.Count; i++) TickBird(flock, i, now, dt);
        }
    }

    void TickBird(Flock flock, int index, long now, float dt)
    {
        Animal bird = flock.Birds[index];
        const float height = 0.35f;
        bird.Age += dt;
        bird.NextLanding -= dt;
        if (bird.Landing.Active && (!bird.Landing.Refresh() || !ValidSite(bird.Landing.Target))) bird.ReleaseTarget();
        if (bird.HasTarget && !bird.Landing.Active &&
            (!_ground.TryFind(bird.Target, height, out Vector3 ground) ||
             Vector3.Distance(ground + (ground - _center).normalized * (height * 0.5f), bird.Target) > 0.05f))
            bird.ReleaseTarget();
        bool threatened = Threatened(bird, 25f, now, out var threat);
        if (!threatened && !bird.HasTarget && bird.NextLanding <= 0f)
        {
            bird.NextLanding = 30f + ScatterHash.To01(bird.Seed) * 30f;
            if (bird.Landing.TryAcquire(_sites, bird.Position, 80f, height, WildlifeLandingUse.Bird,
                bird.PreviousSite, ValidSite))
            { bird.Target = bird.Landing.Target.Position; bird.HasTarget = true; }
            else bird.HasTarget = _ground.TryFind(bird.Position, height, out bird.Target);
            if (bird.HasTarget) bird.Target += (bird.Target - _center).normalized * (height * 0.5f);
        }
        bool arrived = bird.HasTarget && Vector3.Distance(bird.Position, bird.Target) <= 0.05f;
        bird.Brain.Tick(dt, threatened, bird.HasTarget, arrived, 12f);
        if (bird.Brain.VisitComplete) { bird.ReleaseTarget(); bird.NextLanding = 40f; }
        if (bird.Brain.Activity == WildlifeActivity.Escape)
        {
            bird.NextLanding = 15f;
            Escape(bird, threat, 12f, height * 0.5f, dt);
            return;
        }
        if (bird.Brain.Activity == WildlifeActivity.Rest) return;
        Vector3 up = (bird.Position - _center).normalized;
        bool visiting = bird.Brain.Activity == WildlifeActivity.Visit;
        Vector3 target = visiting ? bird.Target : flock.Position + BirdSlot(flock, index, up);
        Vector3 separation = Vector3.zero;
        if (!visiting)
            for (int j = 0; j < flock.Birds.Count; j++)
            {
                if (j == index) continue;
                Vector3 delta = bird.Position - flock.Birds[j].Position;
                if (delta.sqrMagnitude > 0.001f && delta.sqrMagnitude < 2.25f) separation += delta / delta.sqrMagnitude;
            }
        Vector3 direction = target - bird.Position + separation * 2f;
        // Limit ascent and descent independently of horizontal flock steering.
        Vector3 horizontal = Vector3.ProjectOnPlane(direction, up);
        Vector3 forward = horizontal.sqrMagnitude > 0.001f
            ? Vector3.RotateTowards(bird.Forward, horizontal.normalized, dt * 2f, 0f) : bird.Forward;
        float speed = visiting ? 6f : 8f;
        float alignment = horizontal.sqrMagnitude > 0.001f ? Mathf.Max(0f, Vector3.Dot(forward, horizontal.normalized)) : 0f;
        bird.Forward = forward;
        Vector3 next = bird.Position + forward * (Mathf.Min(horizontal.magnitude, speed * dt) * alignment) +
            up * Mathf.Clamp(Vector3.Dot(direction, up), -3f * dt, 3f * dt);
        Move(bird, next, height * 0.5f, dt);
    }

    static Vector3 BirdSlot(Flock flock, int index, Vector3 up)
    {
        Vector3 right = Vector3.Cross(up, flock.Forward).normalized;
        int row = (index + 1) / 2;
        float side = (index & 1) == 0 ? -1f : 1f;
        return flock.Formation
            ? right * (row * side * 1.8f) - flock.Forward * (row * 1.3f)
            : right * ((index % 5 - 2) * 2.5f) - flock.Forward * (index / 5 * 3f) + up * Mathf.Sin(index * 2.4f);
    }
}
