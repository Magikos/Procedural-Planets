using UnityEngine;

/// <summary>
/// Samples the local player's devices into an <see cref="ActorIntent"/>. This is the only place the character
/// path touches <see cref="IInputMapService"/> or the UI look blocker, which is what keeps every layer above
/// it — and any future authority process — free of device, camera and local-player concerns.
/// </summary>
public sealed class LocalPlayerInput : IInputProvider
{
    readonly IInputMapService _input;

    ICameraLookBlocker _lookBlocker;

    public LocalPlayerInput(IInputMapService input)
    {
        _input = input;
    }

    public ActorIntent Sample(uint tick)
    {
        if (_input == null)
            return new ActorIntent(Vector2.zero, Vector2.zero, ActorButtons.None, tick);

        ActorButtons buttons = ActorButtons.None;
        if (_input.Sprint.IsPressed()) buttons |= ActorButtons.Sprint;
        if (_input.Crouch.IsPressed()) buttons |= ActorButtons.Crouch;
        if (_input.Jump.WasPressedThisFrame()) buttons |= ActorButtons.Jump;

        bool looking = _input.LookHold.IsPressed() && _input.GameplayEnabled && !LookBlocked();
        if (looking) buttons |= ActorButtons.LookHold;

        if (_input.Interact.WasPressedThisFrame() && _input.GameplayEnabled && !LookBlocked())
            buttons |= ActorButtons.Interact;

        Vector2 look = looking ? _input.Look.ReadValue<Vector2>() : Vector2.zero;
        return new ActorIntent(_input.Move.ReadValue<Vector2>(), look, buttons, tick);
    }

    bool LookBlocked()
    {
        if (!ServiceLocator.IsAlive(_lookBlocker))
            ServiceLocator.TryGet(out _lookBlocker);
        return _lookBlocker != null && _lookBlocker.BlocksCameraLook;
    }
}
