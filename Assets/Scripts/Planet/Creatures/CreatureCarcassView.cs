using System;
using UnityEngine;

/// <summary>Frozen species death pose with shared flesh/decay presentation. Owns no resource stock.</summary>
public sealed class CreatureCarcassView : IDisposable
{
    readonly CreatureAnimationView _animation;
    readonly CreatureCorpsePresentation _decay;
    readonly IGroundingProvider _grounding;
    readonly Vector3 _up;
    public Transform Root => _animation.Root;
    public bool Settled => _animation.DeathPoseSettled;

    public CreatureCarcassView(Transform parent, CreatureCorpse corpse, CreatureSpeciesDto species, IGroundingProvider grounding)
    {
        _animation = new CreatureAnimationView(parent, corpse.AppearanceIdentity, species.Visuals, species.BodyHeightMeters);
        Root.name = species.DisplayName + " carcass " + corpse.Id.Value.ToString("X");
        Root.SetPositionAndRotation(corpse.Position, corpse.Rotation);
        Vector3 up = corpse.Rotation * Vector3.up;
        _up = up;
        _grounding = grounding;
        _animation.Dead = true;
        _animation.Tick(Vector3.zero, up, (species.Visuals.Death?.length ?? 0f) + .01f, grounding);
        if (species.Visuals.Death == null)
        {
            Root.rotation = corpse.Rotation * Quaternion.Euler(0f, 0f, 90f);
            ActorSurfaceFit.ClearMeshPenetration(Root, Root, grounding, up);
        }
        _decay = new CreatureCorpsePresentation(Root);
    }

    public void Sync(double meatFraction, CorpseStage stage, bool advanceSettlement = true)
    {
        if (!Settled && advanceSettlement) _animation.Tick(Vector3.zero, _up, 1f / 30f, _grounding);
        _decay.Tick(meatFraction, stage, Settled);
    }
    public void Dispose() { _decay.Dispose(); _animation.Dispose(); }
}
