using System.Collections.Generic;
using UnityEngine;

public sealed partial class AmbientSwarms
{
    AmbientWildlifeSimulation _wildlife;
    readonly Dictionary<ulong, BirdAnimationView> _birdViews = new();
    readonly HashSet<ulong> _visibleBirds = new();
    readonly List<ulong> _staleBirds = new();

    void SyncWildlifeViews(float dt)
    {
        _visibleBirds.Clear();
        foreach (AmbientWildlifePose pose in _wildlife.Poses)
        {
            if (IsPollinator(pose.Kind)) { DrawInsect(pose, ProfileOf(pose.Kind)); continue; }
            if (pose.Kind != AmbientSwarmKind.Birds) continue;
            _visibleBirds.Add(pose.Id.Value);
            if (!_birdViews.TryGetValue(pose.Id.Value, out var bird))
            {
                bird = new BirdAnimationView(_parent, pose.Id.Value, 0.35f);
                _birdViews.Add(pose.Id.Value, bird);
            }
            Vector3 up = (pose.Position - _center).normalized;
            bird.Root.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(pose.Forward, up));
            bird.Tick(pose.Resting, pose.Fleeing, dt, up, behaviour: CreatureBehaviour.Perch,
                supportPoint: pose.Resting ? pose.SupportPoint ?? pose.Position - up * .175f : null);
        }
        _staleBirds.Clear();
        foreach (var pair in _birdViews)
            if (!_visibleBirds.Contains(pair.Key)) _staleBirds.Add(pair.Key);
        foreach (ulong id in _staleBirds) { _birdViews[id].Dispose(); _birdViews.Remove(id); }
    }
}
