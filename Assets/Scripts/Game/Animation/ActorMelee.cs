using System;

/// <summary>Attack and guard timing. The caller resolves contact against its authoritative target.</summary>
public sealed class ActorMelee
{
    public enum Motion { Ready, Attack, Block, BlockHit, Hit }
    readonly ActorAttackDefinition _attack;
    readonly float _reactionSeconds;
    bool _contactSent;
    bool _guard;
    public Motion State { get; private set; }
    public float Elapsed { get; private set; }
    public bool Guarding => _guard && (State == Motion.Block || State == Motion.BlockHit);
    public event Action Strike;

    public ActorMelee(ActorAttackDefinition attack, float reactionSeconds)
    {
        _attack = attack ?? throw new ArgumentNullException(nameof(attack));
        if (!float.IsFinite(reactionSeconds) || reactionSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(reactionSeconds));
        _reactionSeconds = reactionSeconds;
    }

    public bool Attack()
    {
        if (State != Motion.Ready) return false;
        Change(Motion.Attack); _contactSent = false; return true;
    }

    public void Guard(bool held)
    {
        _guard = held;
        if (State == Motion.Ready && held) Change(Motion.Block);
        else if (State == Motion.Block && !held) Change(Motion.Ready);
    }

    public bool Receive(ActorHealth health, double damage, float bearing)
    {
        if (health == null) throw new ArgumentNullException(nameof(health));
        if (!double.IsFinite(damage) || damage < 0d || !float.IsFinite(bearing))
            throw new ArgumentOutOfRangeException(nameof(damage));
        if (!health.Alive || damage == 0d) return false;
        bool blocked = Guarding && Math.Abs(bearing) <= 60f;
        if (!blocked) health.Damage(damage);
        Change(blocked ? Motion.BlockHit : Motion.Hit);
        return blocked;
    }

    public void Cancel()
    {
        _guard = false;
        Change(Motion.Ready);
    }

    public void Tick(float dt)
    {
        if (!float.IsFinite(dt) || dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
        Elapsed += dt;
        if (State == Motion.Attack)
        {
            if (!_contactSent && Elapsed >= _attack.Windup)
            {
                _contactSent = true;
                Strike?.Invoke();
                if (State != Motion.Attack) return;
            }
            if (Elapsed >= _attack.Duration + _attack.RecoverySeconds)
                Change(_guard ? Motion.Block : Motion.Ready);
        }
        else if ((State == Motion.BlockHit || State == Motion.Hit) && Elapsed >= _reactionSeconds)
            Change(_guard ? Motion.Block : Motion.Ready);
    }

    void Change(Motion motion) { State = motion; Elapsed = 0f; }
}
