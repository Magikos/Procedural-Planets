using System.Collections.Generic;
using UnityEngine;

public sealed partial class PredatorEncounterPrototype
{
    [Header("Home sites (restart to apply)")]
    public bool HomeSitesEnabled = true;
    public Vector3 PackDenPosition = new(-14, 0, -9), SoloRestPosition = new(19, 0, -15);
    [Min(1f)] public float GatherWaitSeconds = 8f, ResourceMemorySeconds = 90f;
    readonly List<ActorHomeSite> _homeSites = new();

    public void FocusHome(bool solo)
    {
        foreach (var site in _homeSites)
        {
            if ((site.OwnerGroup == 0) != solo || Camera.main == null) continue;
            FollowCamera = false;
            Camera.main.transform.position = site.Position + new Vector3(2, 10, -10);
            Camera.main.transform.LookAt(site.Position);
            return;
        }
    }

    void CreateHomeSites()
    {
        if (!HomeSitesEnabled || !Ecosystem) return;
        var den = new ActorHomeSite(2001ul, HomeAnchor(PackDenPosition), Vector3.up, 3f, 8, 1, 0, true);
        _homeSites.Add(den);
        if (_soloWolf != null)
            _homeSites.Add(new ActorHomeSite(2002ul, HomeAnchor(SoloRestPosition), Vector3.up, 2f, 1, 0, _soloWolf.Id, false));
        foreach (var site in _homeSites)
        {
            Marker(site.Sheltered ? "Pack den" : "Solo resting site", site.Position,
                site.Sheltered ? new Color(.24f, .17f, .10f) : new Color(.35f, .32f, .18f), new Vector3(site.Radius * 2, .03f, site.Radius * 2));
            if (site.Sheltered)
                for (int i = 0; i < 5; i++)
                {
                    Vector3 stone = site.Position + Quaternion.AngleAxis(-80 + i * 40, Vector3.up) * Vector3.forward * (site.Radius + .5f);
                    Marker("Den rock", stone, new Color(.23f, .24f, .21f), new Vector3(1.1f, .9f, 1.1f));
                }
            foreach (var a in _actors)
                if (site.TryReserve(a.Id, a.Group) && site.TryRestPosition(a.Id, out var rest))
                { a.HomeSite = site; a.Home = site.Position; Place(a.View.Root, rest, a.Height * .5f); }
        }
    }

    static Vector3 HomeAnchor(Vector3 position) => position + Vector3.up * CreatureAnimationPrototype.GroundHeight(position.x, position.z);

    void ObserveKnowledge()
    {
        foreach (var a in _actors)
        {
            if (!Available(a)) continue;
            a.Knowledge.Expire(_elapsed);
            if (a.Threat != null && Observation(a, a.Threat, out var threat))
                a.Knowledge.Remember(new ActorKnowledge.Observation(threat.Id, ActorObservationKind.Threat,
                    threat.Position, threat.Expires, _elapsed, threat.Sense, threat.Uncertainty,
                    threat.Confidence, threat.IdentityConfidence, threat.UncertaintyGrowth), _elapsed);
            if (a.HomeSite != null && FlatDistance(a.View.Root.position, a.HomeSite.Position) <= a.HomeSite.Radius)
            {
                a.Knowledge.ShareWith(a.HomeSite.Knowledge, _elapsed);
                a.HomeSite.Knowledge.ShareWith(a.Knowledge, _elapsed);
            }
        }
        for (int i = 0; i < _actors.Count; i++)
        for (int j = i + 1; j < _actors.Count; j++)
        {
            var a = _actors[i]; var b = _actors[j];
            if (!Available(a) || !Available(b) || !ActorGroup.Allied(a.Group, b.Group) ||
                FlatDistance(a.View.Root.position, b.View.Root.position) > PackSupportRadius) continue;
            a.Knowledge.ShareWith(b.Knowledge, _elapsed); b.Knowledge.ShareWith(a.Knowledge, _elapsed);
        }
    }

    bool HomeSafe(ActorHomeSite site) => !site.Knowledge.ThreatNear(site.Position, site.Radius + 6f, _elapsed);
    void UpdateHomeSites()
    {
        foreach (var site in _homeSites)
        {
            int willing = 0, present = 0; bool requested = false, hunting = false;
            foreach (var a in _actors)
            {
                if (a.HomeSite != site || !Available(a)) continue;
                hunting |= a.Brain.Objective == CreatureObjective.Hunt;
                if (!WillSupport(a) || a.Endurance.NeedsSleep || a.Needs.Thirst >= .8 || _elapsed < a.HomeBlockedUntil) continue;
                willing++;
                float distance = FlatDistance(a.View.Root.position, site.Position);
                if (distance <= site.Radius) present++;
                requested |= a.Needs.Hunger >= .65 && a.Needs.Hunger < .9 && distance <= PackSupportRadius && a.Food == null;
            }
            site.UpdateGather(_elapsed, requested && !hunting, willing, present, HomeSafe(site), Mathf.Max(1, GatherWaitSeconds));
        }
    }

    void ObserveHome(Actor a)
    {
        var site = a.HomeSite;
        if (site == null || _elapsed < a.HomeBlockedUntil || !site.TryRestPosition(a.Id, out var rest)) return;
        rest.y += CreatureAnimationPrototype.GroundHeight(rest.x, rest.z) -
            CreatureAnimationPrototype.GroundHeight(site.Position.x, site.Position.z) + a.Height * .5f;
        a.Senses.HasHomeSite = true; a.Senses.HomeId = new EntityId(site.Id); a.Senses.RestPosition = rest;
        a.Senses.HomeSafe = HomeSafe(site) && !a.Knowledge.ThreatNear(site.Position, site.Radius + 6f, _elapsed);
        a.Senses.HomeSheltered = site.Sheltered;
        a.Senses.AtHome = FlatDistance(a.View.Root.position, rest) <= .6f;
        a.Senses.GatherAtHome = site.Gathering && a.Needs.Hunger >= .35 && a.Needs.Thirst < .8 && !a.Endurance.NeedsSleep;
        a.Senses.SeparatedFromGroup = a.Group != 0 && a.Support == 0 && !a.GroupHuntCommitted;
    }
}
