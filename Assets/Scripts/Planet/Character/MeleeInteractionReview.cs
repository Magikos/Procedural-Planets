using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Training fixture for shared melee timing, health, authored playback, and held prop anchors.</summary>
public sealed class MeleeInteractionReview : MonoBehaviour
{
    public HumanoidAnimationPrototype Actor;
    public ActorInteractionDefinition Definition;
    public Transform Stand, Target;
    public HeldToolGrip Sword, Shield;
    public ActorMelee Combat { get; private set; }
    public ActorHealth Health { get; private set; }
    public ActorHealth TargetHealth { get; private set; }
    public string Status { get; private set; } = "Ready";
    readonly ActorAttackDefinition _attack = new(20, 1.65f, 55, .3f, .15f, .35f, .1, 0, 0);
    bool _playing;
    Transform _previousLook;

    void OnEnable()
    {
        ResetCombat();
        if (Actor == null) return;
        _previousLook = Actor.LookTarget;
        Actor.PrepareInteraction += Tick;
        Actor.PresentInteraction += Present;
    }

    void ResetCombat()
    {
        Combat = new ActorMelee(_attack, .5f);
        Combat.Strike += Strike;
        Health = new ActorHealth(100); TargetHealth = new ActorHealth(100);
        Status = "Ready";
    }

    public void ResetReview()
    {
        ResetCombat(); _playing = false;
        Actor.InteractionMovementLocked = false;
        Actor.InteractionPosition = Actor.InteractionForward = null;
        Actor.ResetActor(); Actor.VisitStation(0);
    }

    public bool Attack() => Health.Alive && Combat.Attack();
    public void Guard(bool held) => Combat.Guard(held && Health.Alive);
    public void Cancel() { Combat.Cancel(); Status = "Cancelled"; }
    public void Receive(float bearing = 0)
    {
        Status = Combat.Receive(Health, 10, bearing) ? "Blocked" : "Hit";
    }

    void Strike()
    {
        if (Target == null || !Target.gameObject.activeInHierarchy || !TargetHealth.Alive)
        { Status = "No target"; return; }
        Vector3 delta = Vector3.ProjectOnPlane(Target.position - Actor.Actor.position, Actor.Actor.up);
        float bearing = Vector3.SignedAngle(Actor.Actor.forward, delta, Actor.Actor.up);
        bool contact = _attack.InContact(delta.magnitude, bearing);
        if (contact && Physics.Linecast(Actor.Actor.position + Actor.Actor.up, Target.position + Actor.Actor.up, out var hit))
            contact = hit.transform == Target || hit.transform.IsChildOf(Target);
        if (contact) { TargetHealth.Damage(_attack.Damage); Status = "Training target hit"; }
        else Status = "Miss";
    }

    void Tick(float dt)
    {
        if (Actor.View == null || Stand == null || Definition == null) return;
        bool available = Health.Alive && Actor.Motor.Grounded && !Actor.Motor.Swimming;
        if (!available)
        {
            Combat.Cancel();
            if (_playing) Actor.View.EndInteraction();
            _playing = false; Actor.InteractionMovementLocked = false;
            Actor.InteractionPosition = Actor.InteractionForward = null;
            Actor.LookTarget = _previousLook; return;
        }
        if (!_playing) _playing = Actor.View.BeginInteraction(Definition.Action);
        if (!_playing) return;
        Combat.Tick(dt);
        string phase = Combat.State.ToString();
        var data = System.Array.Find(Definition.Phases, p => p.Animation.Name == phase);
        Actor.View.SetInteractionPhase(phase, Combat.Elapsed / data.Seconds);
        Actor.InteractionMovementLocked = true;
        Actor.InteractionPosition = Stand.position; Actor.InteractionForward = Stand.forward;
        Actor.LookTarget = Target;
    }

    void Present(float dt)
    {
        Fit(Sword); Fit(Shield);
    }

    void Fit(HeldToolGrip grip)
    {
        if (grip != null && Actor.View != null && Actor.View.Pose.TryGetInteractionContactPose(grip.PrimaryId, out var palm) &&
            grip.TryFit(palm.position, palm.rotation, out var fitted))
            grip.transform.SetPositionAndRotation(fitted.position, fitted.rotation);
    }

    void Update()
    {
        var key = Keyboard.current;
        if (key == null) return;
        Guard(key.bKey.isPressed);
        if (key.eKey.wasPressedThisFrame) Attack();
        if (key.hKey.wasPressedThisFrame) Receive();
        if (key.jKey.wasPressedThisFrame) Receive(180);
        if (key.escapeKey.wasPressedThisFrame) Cancel();
    }

    void OnDisable()
    {
        if (Actor == null) return;
        Actor.PrepareInteraction -= Tick; Actor.PresentInteraction -= Present;
        if (_playing) Actor.View?.EndInteraction();
        Actor.InteractionMovementLocked = false;
        Actor.InteractionPosition = Actor.InteractionForward = null;
        Actor.LookTarget = _previousLook;
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 340, 10, 330, 150), GUI.skin.box);
        GUILayout.Label("Sword and shield training");
        GUILayout.Label("E attack · hold B guard · H front hit · J rear hit · Esc cancel");
        GUILayout.Label(Status + " | Health " + Health.Current + " | Target " + TargetHealth.Current);
        GUILayout.Label("Incoming hits use test input. Equip and combat movement remain open.");
        if (GUILayout.Button("Reset training")) ResetReview();
        GUILayout.EndArea();
    }
}
