using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Planet/Creature Audio")]
public sealed class CreatureAudioSettings : ScriptableObject
{
    public AudioClip[] Calls = Array.Empty<AudioClip>();
    public AudioClip[] Alerts = Array.Empty<AudioClip>();
    public AudioClip[] Attacks = Array.Empty<AudioClip>();
    public AudioClip[] Feeding = Array.Empty<AudioClip>();
    [Range(0f, 1f)] public float Volume = .6f;
    [Min(.1f)] public float NearDistance = 2f;
    [Min(1f)] public float AudibleDistance = 80f;
    [Min(1f)] public float MinimumCallSeconds = 25f;
    [Min(1f)] public float MaximumCallSeconds = 60f;
    [Min(.1f)] public float AlertCooldownSeconds = 5f;
    [Min(.1f)] public float AttackCooldownSeconds = 1f;
    [Min(.1f)] public float FeedingIntervalSeconds = 8f;

    public CreatureAudioDto Snapshot()
    {
        if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1
            || !Positive(NearDistance) || !Positive(AudibleDistance) || AudibleDistance < NearDistance
            || !Positive(MinimumCallSeconds) || !Positive(MaximumCallSeconds) || MaximumCallSeconds < MinimumCallSeconds
            || !Positive(AlertCooldownSeconds) || !Positive(AttackCooldownSeconds) || !Positive(FeedingIntervalSeconds))
            throw new ArgumentException($"Invalid creature audio settings: {name}");
        return new CreatureAudioDto(Copy(Calls), Copy(Alerts), Copy(Attacks), Copy(Feeding),
            Volume, NearDistance, AudibleDistance, MinimumCallSeconds, MaximumCallSeconds,
            AlertCooldownSeconds, AttackCooldownSeconds, FeedingIntervalSeconds);
    }

    static bool Positive(float value) => float.IsFinite(value) && value > 0;
    static IReadOnlyList<AudioClip> Copy(AudioClip[] clips) =>
        Array.AsReadOnly(clips == null ? Array.Empty<AudioClip>() : (AudioClip[])clips.Clone());
}

public sealed record CreatureAudioDto(IReadOnlyList<AudioClip> Calls, IReadOnlyList<AudioClip> Alerts,
    IReadOnlyList<AudioClip> Attacks, IReadOnlyList<AudioClip> Feeding, float Volume,
    float NearDistance, float AudibleDistance, float MinimumCallSeconds, float MaximumCallSeconds,
    float AlertCooldownSeconds, float AttackCooldownSeconds, float FeedingIntervalSeconds);
