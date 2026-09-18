using UnityEngine;

/// <summary>Commits campfire construction and ignition at interaction contact markers.</summary>
public sealed class CampfireInteraction : MonoBehaviour
{
    public Transform WoodVisual;
    public Light FireLight;
    public ParticleSystem Flames;
    public bool Built { get; private set; }
    public bool Lit { get; private set; }
    float _woodWeight;

    public bool CanUse(ActorInteractionDefinition definition)
    {
        if (definition == null) return false;
        foreach (var phase in definition.Phases)
        {
            if (phase.Marker == "BuildCampfire") return !Built;
            if (phase.Marker == "LightCampfire") return Built && !Lit;
        }
        return Lit;
    }

    public void ApplyMarker(string marker)
    {
        if (marker == "BuildCampfire") Built = true;
        if (marker == "LightCampfire" && Built) Lit = true;
    }

    public void Tick(float dt)
    {
        _woodWeight = Mathf.MoveTowards(_woodWeight, Built ? 1f : 0f, dt * 2f);
        if (WoodVisual != null) WoodVisual.localScale = Vector3.one * _woodWeight;
        if (FireLight != null) FireLight.intensity = Mathf.MoveTowards(FireLight.intensity, Lit ? 2f : 0f, dt * 4f);
        if (Flames != null && Lit && !Flames.isPlaying) Flames.Play();
    }

    public void ResetState()
    {
        Built = Lit = false;
        _woodWeight = 0f;
        if (WoodVisual != null) WoodVisual.localScale = Vector3.zero;
        if (FireLight != null) FireLight.intensity = 0f;
        if (Flames != null) Flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void Awake() => ResetState();
}
