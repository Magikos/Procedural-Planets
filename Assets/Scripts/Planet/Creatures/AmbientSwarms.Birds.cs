using System.Collections.Generic;
using UnityEngine;

public sealed partial class AmbientSwarms
{
    sealed class BirdFlock
    {
        public Vector3 Position, Forward;
        public float Age;
        public bool Formation;
        public readonly List<BirdAnimationView> Birds = new();
        public void Dispose() { foreach (var bird in Birds) bird.Dispose(); }
    }

    readonly List<BirdFlock> _birdFlocks = new();
    uint _flockSequence;

    void TickBirdFlocks(Vector3 observer, float sun, long now)
    {
        AmbientSwarmProfile profile = null;
        foreach (var candidate in _profiles)
            if (candidate.Kind == AmbientSwarmKind.Birds) { profile = candidate; break; }
        if (profile == null) return;
        int wanted = profile.CountAt(sun);
        for (int i = _birdFlocks.Count - 1; i >= 0; i--)
        {
            BirdFlock flock = _birdFlocks[i];
            if (i < wanted && Vector3.Distance(flock.Position, observer) < 250f) continue;
            flock.Dispose();
            _birdFlocks.RemoveAt(i);
        }
        while (_birdFlocks.Count < wanted)
        {
            if (!TryPlaceAmbientAnchor(profile, observer, out Vector3 position)) break;
            uint seed = ScatterHash.Mix(++_flockSequence);
            Vector3 up = (position - _center).normalized;
            var flock = new BirdFlock { Position = position, Forward =
                Quaternion.AngleAxis(ScatterHash.To01(seed) * 360f, up) * CharacterMath.ArbitraryTangent(up),
                Formation = (seed & 1) == 0 };
            int count = (seed % 3) switch { 0 => 1, 1 => 7, _ => 24 };
            for (int b = 0; b < count; b++)
            {
                var bird = new BirdAnimationView(_parent, ((ulong)seed << 32) | (uint)b, 0.35f);
                flock.Birds.Add(bird);
                bird.Root.SetPositionAndRotation(position + BirdSlot(flock, b, up), Quaternion.LookRotation(flock.Forward, up));
            }
            _birdFlocks.Add(flock);
        }
        float dt = Mathf.Max(0f, Time.deltaTime);
        foreach (BirdFlock flock in _birdFlocks)
        {
            flock.Age += dt;
            Vector3 up = (flock.Position - _center).normalized;
            flock.Forward = Vector3.ProjectOnPlane(Quaternion.AngleAxis(Mathf.Sin(flock.Age * 0.15f) * dt * 5f, up)
                * flock.Forward, up).normalized;
            flock.Position += flock.Forward * (6f * dt);
            up = (flock.Position - _center).normalized;
            if (_sampler.TryGetSurfaceRadius(up, out float ground))
                flock.Position = _center + up * (Mathf.Max(ground, _seaLevelRadius) + profile.HeightMeters);
            for (int i = 0; i < flock.Birds.Count; i++)
            {
                var bird = flock.Birds[i];
                Vector3 target = flock.Position + BirdSlot(flock, i, up);
                ThreatSource threat = default;
                bool fleeing = _threats != null && _threats.TryFindThreat(bird.Root.position, default,
                    CreatureFaction.Wildlife, 25f, now, out threat);
                if (fleeing)
                    target = bird.Root.position + (bird.Root.position - threat.Position).normalized * 20f + up * 8f;
                Vector3 separation = Vector3.zero;
                for (int j = 0; j < flock.Birds.Count; j++)
                {
                    if (j == i) continue;
                    Vector3 delta = bird.Root.position - flock.Birds[j].Root.position;
                    if (delta.sqrMagnitude > 0.001f && delta.sqrMagnitude < 2.25f)
                        separation += delta / delta.sqrMagnitude;
                }
                Vector3 direction = target - bird.Root.position + separation * 2f;
                if (direction.sqrMagnitude > 0.001f)
                {
                    Vector3 forward = Vector3.RotateTowards(bird.Root.forward, direction.normalized, dt * 2f, 0f);
                    bird.Root.position += forward * Mathf.Min(direction.magnitude, (fleeing ? 12f : 8f) * dt);
                    Vector3 radial = bird.Root.position - _center;
                    if (_sampler.TryGetSurfaceRadius(radial.normalized, out float radius) && radial.magnitude < radius + 2f)
                        bird.Root.position = _center + radial.normalized * (radius + 2f);
                    bird.Root.rotation = Quaternion.LookRotation(forward, up);
                }
                bird.Tick(false, fleeing, dt);
            }
        }
    }

    static Vector3 BirdSlot(BirdFlock flock, int index, Vector3 up)
    {
        Vector3 right = Vector3.Cross(up, flock.Forward).normalized;
        int row = (index + 1) / 2;
        float side = (index & 1) == 0 ? -1f : 1f;
        return flock.Formation
            ? right * (row * side * 1.8f) - flock.Forward * (row * 1.3f)
            : right * ((index % 5 - 2) * 2.5f) - flock.Forward * (index / 5 * 3f) + up * Mathf.Sin(index * 2.4f);
    }
}
