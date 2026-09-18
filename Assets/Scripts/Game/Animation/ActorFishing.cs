using System;

/// <summary>Fishing decisions around the shared interaction clock. Catch rewards occur once at the catch marker.</summary>
public sealed class ActorFishing
{
    public ActorInteractionSession Session { get; } = new();
    public bool Active => Session.Active;
    public string Status { get; private set; } = "Ready to cast.";
    public int Catches { get; private set; }
    public event Action Caught;
    bool _hooked, _rewarded;
    float _phaseTime;
    string _phase;

    public ActorFishing() => Session.Marker += marker =>
    {
        if (marker != "Catch" || !_hooked || _rewarded) return;
        _rewarded = true; Catches++; Status = "Caught a fish."; Caught?.Invoke();
    };

    public void Begin(ActorInteractionPlan plan)
    {
        if (Active) throw new InvalidOperationException("Finish or cancel the current cast first.");
        if (plan == null || plan.Count != 7) throw new ArgumentException("Fishing requires seven phases.");
        var names = new[] { "Cast", "Wait", "Nibble", "Bite", "Pull", "Catch", "Recover" };
        for (int i = 0; i < names.Length; i++)
            if (plan[i].Name != names[i]) throw new ArgumentException("Fishing phase order is invalid.");
        if (!plan[1].RepeatUntilInput || !plan[3].WaitForInput || !plan[4].RepeatUntilInput || plan[5].Marker != "Catch")
            throw new ArgumentException("Fishing requires a waiting phase, bite window, repeated pull, and catch marker.");
        _hooked = _rewarded = false; _phase = null; _phaseTime = 0;
        Session.Begin(plan); Status = "Casting.";
    }

    public bool Hook()
    {
        if (!Active || Session.Phase.Name != "Bite" || _hooked) return false;
        _hooked = true; Session.Continue(); Status = "Pulling in the fish."; return true;
    }

    public void Cancel()
    {
        if (!Active || Session.Phase.Name == "Recover") return;
        _hooked = false;
        Session.Begin(Session.Plan, 6); _phase = null; Status = "Reeling in without a catch.";
    }

    public void Tick(float dt, bool targetValid = true, bool readyToLand = true)
    {
        if (!float.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        if (!Active) return;
        if (!targetValid && Session.Phase.Name != "Recover") Cancel();
        // Bounded steps preserve the bite window and catch ordering during a frame hitch.
        while (dt > 0 && Active)
        {
            float step = Math.Min(dt, 1f / 60f); dt -= step;
            string phase = Session.Phase.Name;
            if (_phase != phase) { _phase = phase; _phaseTime = 0; }
            _phaseTime += step;
            if (phase == "Wait" && _phaseTime + step >= Session.Phase.Seconds) Session.Continue();
            if (phase == "Bite")
            {
                Status = "Bite! Press E to hook.";
                if (_phaseTime >= 2f) { Cancel(); Status = "Missed the bite. Reeling in."; }
            }
            if (phase == "Pull" && readyToLand && _phaseTime >= 2.9f) Session.Continue();
            // Catch already contains the authored recovery; do not play it twice.
            if (phase == "Catch" && Session.Elapsed + step >= Session.Phase.Seconds)
            { Session.Advance(step); Session.Cancel(); break; }
            Session.Advance(step);
        }
    }
}
