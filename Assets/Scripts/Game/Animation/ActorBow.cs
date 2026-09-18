/// <summary>Bow action intent. The caller owns playback, contact, and physical item custody.</summary>
public sealed class ActorBow
{
    public enum Motion { Stowed, Equipping, Ready, Retrieving, Drawing, Holding, Releasing, Lowering, ReturningArrow, Stowing }
    public Motion State { get; private set; }
    public int Direction { get; private set; } = 1;

    public bool Equip(bool bowStowed)
    {
        if (State != Motion.Stowed || !bowStowed) return false;
        Change(Motion.Equipping); return true;
    }

    public bool Retrieve(bool arrowAvailable)
    {
        if (State != Motion.Ready || !arrowAvailable) return false;
        Change(Motion.Retrieving); return true;
    }

    public bool Release()
    {
        if (State != Motion.Holding) return false;
        Change(Motion.Releasing); return true;
    }

    public bool Stow()
    {
        if (State != Motion.Ready) return false;
        Change(Motion.Stowing); return true;
    }

    /// <summary>Retain the arrow when storage is blocked. The caller maps reverse reach progress to 1 - progress.</summary>
    public bool ReturnFailed()
    {
        if (State != Motion.ReturningArrow) return false;
        return Change(Motion.Retrieving);
    }

    /// <summary>On reversal retain the current playback cursor; on phase changes blend from the current pose.</summary>
    public void Cancel(bool bowHeld, bool arrowHeld)
    {
        switch (State)
        {
            case Motion.Equipping:
                if (!bowHeld) Direction = -1;
                break;
            case Motion.Retrieving:
                if (arrowHeld) Change(Motion.ReturningArrow);
                else Direction = -1;
                break;
            case Motion.Drawing:
            case Motion.Holding:
                Change(Motion.Lowering);
                break;
            case Motion.Stowing:
                if (bowHeld) Direction = -1;
                break;
        }
    }

    /// <summary>Call at the playback endpoint with observed custody after any confirmed contact.</summary>
    public bool CompletePhase(bool bowHeld, bool arrowHeld, bool arrowStowed)
    {
        if (arrowHeld && arrowStowed) return false;
        switch (State)
        {
            case Motion.Equipping:
                if (Direction > 0 && bowHeld) return Change(Motion.Ready);
                if (Direction < 0 && !bowHeld) return Change(Motion.Stowed);
                break;
            case Motion.Retrieving:
                if (!bowHeld) break;
                if (Direction > 0 && arrowHeld) return Change(Motion.Drawing);
                if (Direction < 0 && arrowStowed) return Change(Motion.Ready);
                break;
            case Motion.Drawing:
                if (bowHeld && arrowHeld) return Change(Motion.Holding);
                break;
            case Motion.Releasing:
                if (bowHeld && !arrowHeld && !arrowStowed) return Change(Motion.Ready);
                break;
            case Motion.Lowering:
                if (bowHeld && arrowHeld) return Change(Motion.ReturningArrow);
                break;
            case Motion.ReturningArrow:
                if (bowHeld && arrowStowed) return Change(Motion.Ready);
                break;
            case Motion.Stowing:
                if (Direction > 0 && !bowHeld && !arrowHeld) return Change(Motion.Stowed);
                if (Direction < 0 && bowHeld) return Change(Motion.Ready);
                break;
        }
        return false;
    }

    bool Change(Motion motion) { State = motion; Direction = 1; return true; }
}
