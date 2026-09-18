using UnityEngine;

public sealed partial class AmbientWildlifeSimulation
{
    void TickPollinators(Vector3 observer, float sun, long now, float dt)
    {
        _spawnIn -= dt;
        bool spawn = _spawnIn <= 0f;
        if (spawn) _spawnIn = 0.25f;
        foreach (AmbientSwarmProfile profile in _profiles)
        {
            if (profile.Kind != AmbientSwarmKind.Butterflies && profile.Kind != AmbientSwarmKind.Bees) continue;
            int want = profile.CountAt(sun), live = 0;
            foreach (Animal insect in _insects)
            {
                if (insect.Profile.Kind != profile.Kind || insect.Retiring) continue;
                if (Vector3.Distance(insect.Position, observer) > 60f || live >= want)
                { insect.Retiring = true; insect.ReleaseTarget(); }
                else live++;
            }
            if (spawn && live < want && _place(profile, observer, out Vector3 anchor) &&
                Vector3.Distance(anchor, observer) <= 60f)
            {
                var insect = new Animal(_ids.Next(), profile, anchor, ScatterHash.Mix(++_sequence), (anchor - _center).normalized);
                if (ChoosePollinatorLanding(insect, observer)) _insects.Add(insect);
            }
        }

        for (int i = _insects.Count - 1; i >= 0; i--)
        {
            Animal insect = _insects[i];
            insect.Age += dt;
            insect.Alpha = Mathf.MoveTowards(insect.Alpha, insect.Retiring ? 0f : 1f, dt * 0.5f);
            if (insect.Retiring)
            {
                insect.ReleaseTarget();
                insect.Brain.Tick(dt, false, false, false, 1f);
                if (insect.Alpha <= 0f) _insects.RemoveAt(i);
                else Move(insect, insect.Position + insect.Forward * (insect.Profile.SpeedMps * dt), 0.035f, dt);
                continue;
            }
            if (insect.Landing.Active && (!insect.Landing.Refresh() || !ValidSite(insect.Landing.Target)))
                insect.ReleaseTarget();
            if (insect.HasTarget && !insect.Landing.Active &&
                (!_ground.TryFind(insect.Target, insect.Profile.ParticleSize, out Vector3 ground) ||
                 Vector3.Distance(ground + (ground - _center).normalized * 0.035f, insect.Target) > 0.05f))
                insect.ReleaseTarget();

            bool threatened = Threatened(insect, insect.Profile.ScatterRadiusMeters, now, out var threat);
            if (!threatened && !insect.HasTarget && !ChoosePollinatorLanding(insect, observer))
                insect.Retiring = true;
            bool arrived = insect.HasTarget && insect.Travel >= 1f;
            insect.Brain.Tick(dt, threatened, insect.HasTarget, arrived,
                Mathf.Lerp(2f, 6f, ScatterHash.To01(insect.Seed)));
            if (insect.Brain.VisitComplete) insect.ReleaseTarget();
            if (insect.Brain.Activity == WildlifeActivity.Escape)
            {
                Escape(insect, threat, insect.Profile.SpeedMps * 2f, 0.035f, dt);
                continue;
            }
            if (insect.Brain.Activity != WildlifeActivity.Visit) continue;
            insect.Travel = Mathf.Min(1f, insect.Travel + dt / insect.Duration);
            float t = insect.Travel, arc = Mathf.Sin(t * Mathf.PI);
            Vector3 up = (insect.Position - _center).normalized;
            Vector3 side = Vector3.Cross(up, insect.Target - insect.From).normalized;
            float flutter = insect.Profile.Kind == AmbientSwarmKind.Bees ? 0.06f : 0.3f;
            Vector3 next = Vector3.Lerp(insect.From, insect.Target, t) + up * arc * 1.2f +
                side * (Mathf.Sin(insect.Age * 5f + insect.Seed % 31) * flutter * arc);
            if (!Move(insect, next, 0.035f, dt)) { insect.Retiring = true; insect.ReleaseTarget(); }
        }
    }

    bool ChoosePollinatorLanding(Animal insect, Vector3 observer)
    {
        var use = insect.Profile.Kind == AmbientSwarmKind.Bees ? WildlifeLandingUse.Bee : WildlifeLandingUse.Butterfly;
        bool claimed = insect.Landing.TryAcquire(_flowers, insect.Position, 60f, insect.Profile.ParticleSize,
            use, insect.PreviousSite, ValidSite) ||
            insect.Landing.TryAcquire(_sites, insect.Position, 60f, insect.Profile.ParticleSize, use, insect.PreviousSite, ValidSite);
        if (claimed)
            insect.Target = insect.Landing.Target.Position + (insect.Landing.Target.Position - _center).normalized * 0.035f;
        else
        {
            // A bee can revisit its only flower after completing the previous visit.
            if (insect.Profile.Kind == AmbientSwarmKind.Bees &&
                (insect.Landing.TryAcquire(_flowers, insect.Position, 60f, insect.Profile.ParticleSize, use, accepts: ValidSite) ||
                 insect.Landing.TryAcquire(_sites, insect.Position, 60f, insect.Profile.ParticleSize, use, accepts: ValidSite)))
                insect.Target = insect.Landing.Target.Position + (insect.Landing.Target.Position - _center).normalized * 0.035f;
            else if (insect.Profile.Kind == AmbientSwarmKind.Bees ||
                !_place(insect.Profile, observer, out Vector3 anchor) ||
                !_ground.TryFind(anchor, insect.Profile.ParticleSize, out insect.Target)) return false;
            else insect.Target += (insect.Target - _center).normalized * 0.035f;
        }
        insect.HasTarget = true;
        insect.From = insect.Position;
        insect.Travel = 0f;
        insect.Seed = ScatterHash.Mix(insect.Seed + 1);
        insect.Duration = Mathf.Max(1f, Vector3.Distance(insect.From, insect.Target) / Mathf.Max(0.01f, insect.Profile.SpeedMps));
        return true;
    }
}
