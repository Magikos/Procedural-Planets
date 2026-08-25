using System;
using UnityEngine;

/// <summary>
/// One tick of an actor's intent, decoupled from where it came from. A local device, a deserialized network
/// command, an AI behaviour and a replay log all produce this same value, which is what lets an authority
/// process drive an actor with no input device, no camera and no local player present.
/// </summary>
public readonly struct ActorIntent
{
    /// <summary>Planar movement in the actor's look basis: y walks along the view, x strafes.</summary>
    public readonly Vector2 Move;

    /// <summary>Look delta (yaw, pitch) in raw device units; zero when the actor is not looking this tick.</summary>
    public readonly Vector2 Look;

    public readonly ActorButtons Buttons;
    public readonly uint Tick;

    public ActorIntent(Vector2 move, Vector2 look, ActorButtons buttons, uint tick)
    {
        Move = move;
        Look = look;
        Buttons = buttons;
        Tick = tick;
    }

    public bool Held(ActorButtons button) => (Buttons & button) != 0;
}

[Flags]
public enum ActorButtons : uint
{
    None = 0,
    Jump = 1,
    Sprint = 2,
    Crouch = 4,
    Interact = 8,

    // planned: 16 and 32 are reserved for PrimaryCast/SecondaryCast, docs/design/2026-08-20-magikos-game-architecture.md
    LookHold = 64,
}
