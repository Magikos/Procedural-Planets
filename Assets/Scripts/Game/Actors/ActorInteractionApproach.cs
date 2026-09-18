using System;
using UnityEngine;

/// <summary>Aligns an actor's comfortable local contact with a world target before an action starts.</summary>
public static class ActorInteractionApproach
{
    public static CharacterPose ForContact(CharacterPose actor, Vector3 contact, Vector3 facing, Vector3 localContact)
    {
        if (!CharacterMath.IsFinite(contact) || !CharacterMath.IsFinite(facing) || !CharacterMath.IsFinite(localContact))
            throw new ArgumentException("Interaction approach geometry must be finite.");
        facing = Vector3.ProjectOnPlane(facing, actor.Up);
        if (facing.sqrMagnitude < 1e-8f) throw new ArgumentException("Interaction approach facing must have a tangent direction.");
        facing.Normalize();
        var rotation = Quaternion.LookRotation(facing, actor.Up);
        Vector3 delta = Vector3.ProjectOnPlane(contact - actor.Position - rotation * localContact, actor.Up);
        return new CharacterPose(actor.Position + delta, actor.Up, facing);
    }
}
