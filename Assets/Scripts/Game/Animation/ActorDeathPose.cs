using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Settles an evaluated death pose against analytic support. This is not an active ragdoll.</summary>
public sealed class ActorDeathPose
{
    /// <summary>Local display snapshot with scene transform handles; not a network or persistence payload.</summary>
    public readonly struct LocalPose
    {
        public readonly Transform Bone;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public LocalPose(Transform bone) { Bone = bone; Position = bone.localPosition; Rotation = bone.localRotation; }
        public void Apply() { if (Bone != null) Bone.SetLocalPositionAndRotation(Position, Rotation); }
    }

    readonly Transform _body;
    readonly LocalPose[] _authored;
    readonly LocalPose[] _frozen;
    readonly IReadOnlyList<LocalPose> _snapshot;
    readonly Transform[] _support;
    readonly LimbPoseSolver[] _limbs;
    readonly FootSupport[] _feet;
    readonly struct FootSupport
    {
        public readonly Transform Contact;
        public readonly float JointLimit;
        public readonly Vector3 BendDirection;
        public FootSupport(FootDefinition foot, Transform tip)
        { Contact = foot.Contact != null ? foot.Contact : tip; JointLimit = foot.JointLimit; BendDirection = foot.BendDirection; }
    }
    readonly float _height;
    Quaternion _rotation = Quaternion.identity;
    Vector3 _offset;
    int _ticks, _stable;
    public bool Settled { get; private set; }
    public bool HasSupport { get; private set; }
    public IReadOnlyList<LocalPose> FrozenPose => Settled ? _snapshot : null;

    public ActorDeathPose(Transform frame, ProceduralRigDefinition rig, float height)
    {
        if (frame == null || rig == null || rig.Body == null || rig.Body == frame || !rig.Body.IsChildOf(frame))
            throw new ArgumentException("Death settling requires a visual body below its frame.");
        if (!float.IsFinite(height) || height <= 0f) throw new ArgumentOutOfRangeException(nameof(height));
        _height = height;
        _body = rig.Body;
        var bones = _body.GetComponentsInChildren<Transform>();
        if (bones.Length > 256) throw new ArgumentException("Death settling supports at most 256 bones.", nameof(rig));
        _authored = new LocalPose[bones.Length]; _frozen = new LocalPose[bones.Length];
        for (int i = 0; i < bones.Length; i++) _authored[i] = new LocalPose(bones[i]);
        _snapshot = Array.AsReadOnly(_frozen);
        var support = new List<Transform> { _body };
        foreach (var bone in rig.Spine)
            if (bone != null && bone.IsChildOf(_body) && !support.Contains(bone) && support.Count < 6) support.Add(bone);
        _support = support.ToArray();
        var feet = new List<FootDefinition>();
        foreach (var foot in rig.Feet)
            if (feet.Count < 8 && foot?.Bones != null && foot.Bones.Length >= 2 && foot.Bones[0] != _body &&
                foot.Bones[0] != null && foot.Bones[0].IsChildOf(_body)) feet.Add(foot);
        _feet = new FootSupport[feet.Count]; _limbs = new LimbPoseSolver[feet.Count];
        for (int i = 0; i < _feet.Length; i++)
        {
            _limbs[i] = new LimbPoseSolver(feet[i].Bones);
            _feet[i] = new FootSupport(feet[i], _limbs[i].Tip);
        }
    }

    public void Reset()
    {
        foreach (var pose in _authored) pose.Apply();
        _rotation = Quaternion.identity; _offset = Vector3.zero;
        _ticks = _stable = 0; Settled = HasSupport = false;
    }

    public void Tick(IGroundingProvider ground, Vector3 up, float dt)
    {
        if (Settled || !float.IsFinite(dt) || dt <= 0f) return;
        _ticks++;
        if (!CharacterMath.IsFinite(up) || up.sqrMagnitude < .5f || ground == null)
        { if (_ticks >= 90) Freeze(); return; }
        up.Normalize();
        foreach (var pose in _authored) pose.Apply();
        Vector3 origin = _body.position;
        Quaternion authoredRotation = _body.rotation;
        Vector3 normal = Vector3.zero;
        int samples = 0;
        foreach (var bone in _support)
            if (TrySupport(ground, bone.position, up, out var hit)) { normal += hit.Normal.normalized; samples++; }
        HasSupport = samples > 0 && normal.sqrMagnitude > .001f;
        if (!HasSupport)
        {
            _body.SetPositionAndRotation(origin + _offset, _rotation * authoredRotation);
            if (_ticks >= 90) Freeze();
            return;
        }
        normal = Vector3.RotateTowards(up, normal.normalized, 60f * Mathf.Deg2Rad, 0f);
        Quaternion targetRotation = Quaternion.FromToRotation(up, normal);
        float blend = 1f - Mathf.Exp(-12f * Mathf.Min(dt, .05f));
        _rotation = Quaternion.Slerp(_rotation, targetRotation, blend);
        _body.rotation = _rotation * authoredRotation;
        float lift = float.NegativeInfinity;
        foreach (var bone in _support)
            if (TrySupport(ground, bone.position, up, out var hit))
                lift = Mathf.Max(lift, Vector3.Dot(hit.Position - bone.position, up) + _height * .06f);
        Vector3 desired = float.IsFinite(lift) ? up * Mathf.Clamp(lift, -_height, _height) : _offset;
        _offset = Vector3.Lerp(_offset, desired, blend);
        _body.position = origin + _offset;
        for (int i = 0; i < _limbs.Length; i++)
        {
            var foot = _feet[i];
            Transform tip = _limbs[i].Tip;
            Transform contact = foot.Contact;
            if (TrySupport(ground, contact.position, up, out var hit) &&
                Vector3.Distance(hit.Position, contact.position) <= _height * .35f)
            {
                Vector3 target = hit.Position + up * .005f - (contact.position - tip.position);
                _limbs[i].Solve(target, 1f, foot.JointLimit, tip.rotation, _body.TransformDirection(foot.BendDirection));
            }
        }
        bool stable = Vector3.Distance(_offset, desired) < _height * .001f && Quaternion.Angle(_rotation, targetRotation) < .1f;
        _stable = stable ? _stable + 1 : 0;
        if (_stable >= 4 || _ticks >= 90) Freeze();
    }

    static bool TrySupport(IGroundingProvider ground, Vector3 position, Vector3 up, out GroundResult hit) =>
        ground.TryGround(position, -up, 0f, out hit) && CharacterMath.IsFinite(hit.Position) &&
        CharacterMath.IsFinite(hit.Normal) && hit.Normal.sqrMagnitude > .5f && Vector3.Dot(hit.Normal, up) > .1f;

    void Freeze()
    {
        Settled = true;
        CaptureFrozenPose();
    }

    /// <summary>Refreshes local display transforms after a final renderer clearance adjustment.</summary>
    public void CaptureFrozenPose()
    {
        if (!Settled) throw new InvalidOperationException("The death pose must settle before capturing its frozen pose.");
        for (int i = 0; i < _authored.Length; i++) _frozen[i] = new LocalPose(_authored[i].Bone);
    }
}
