using UnityEngine;

/// <summary>
/// The placeholder brain: a wander that keeps a creature near its home range. It is an
/// <see cref="IInputProvider"/> like the keyboard is, which is the whole point - the driver below it cannot
/// tell an animal from a player, and an authority process with no input device drives both the same way.
/// </summary>
/// <remarks>
/// Deterministic in (seed, tick), so two processes ticking the same creature from the same state agree without
/// exchanging anything. <see cref="Observe"/> is the perception step: the brain is told where it is before it
/// is asked what it wants, rather than reaching for the world itself.
/// </remarks>
// ponytail: wander only. Real behaviour - fleeing, aggro, packs - is a different brain behind this same
// interface, and is explicitly out of the first slice (design doc section 12).
public sealed class CreatureWanderInput : IInputProvider
{
    // Heading turns by at most this much per second while wandering, so the path curves rather than jitters.
    const float TurnDegreesPerSecond = 45f;
    const float HeadingChangeSeconds = 4f;

    readonly uint _seed;
    readonly float _homeRangeMeters;

    float _headingDegrees;      // signed turn intent, re-drawn every HeadingChangeSeconds
    float _homeBearingDegrees;  // signed angle from current facing to home, in the tangent plane
    float _homePull01;          // 0 inside the home range, ramping to 1 at twice it
    bool _resting;

    public CreatureWanderInput(int seed, float homeRangeMeters)
    {
        _seed = ScatterHash.Mix(unchecked((uint)seed));
        _homeRangeMeters = Mathf.Max(1f, homeRangeMeters);
    }

    /// <summary>Perception. Called once per tick before <see cref="Sample"/>.</summary>
    public void Observe(Vector3 position, Vector3 forward, Vector3 up, Vector3 home, float surfaceDistanceToHome)
    {
        _homePull01 = Mathf.Clamp01((surfaceDistanceToHome - _homeRangeMeters) / _homeRangeMeters);

        Vector3 toHome = home - position;
        if (!CharacterMath.TryProjectOntoTangent(toHome, up, out Vector3 homeDir) ||
            !CharacterMath.TryProjectOntoTangent(forward, up, out Vector3 face))
        {
            _homeBearingDegrees = 0f;
            return;
        }
        _homeBearingDegrees = Vector3.SignedAngle(face, homeDir, up);
    }

    public ActorIntent Sample(uint tick)
    {
        // One draw per HeadingChangeSeconds of a 50 Hz tick: the same creature makes the same decisions in the
        // same order however the frame rate varies, which a per-frame draw would not give.
        uint bucket = tick / (uint)Mathf.Max(1, Mathf.RoundToInt(HeadingChangeSeconds * 50f));
        uint h = ScatterHash.Mix(_seed ^ (bucket * 0x9e3779b1u));
        _headingDegrees = (ScatterHash.To01(h) * 2f - 1f) * TurnDegreesPerSecond;
        _resting = ScatterHash.To01(ScatterHash.Slot(h, 3)) < 0.25f;

        // Beyond the home range the wander is overruled by the pull home; inside it, the pull is zero and the
        // creature is free. This is the drift-home decision at full fidelity - the same intent the coarse
        // fast-forward applies in one step.
        float turn = Mathf.Lerp(_headingDegrees, _homeBearingDegrees, _homePull01);
        float forward = _resting && _homePull01 <= 0f ? 0f : 1f;

        return new ActorIntent(new Vector2(0f, forward), new Vector2(turn, 0f), ActorButtons.None, tick);
    }
}
