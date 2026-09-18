using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Advances manual clip clocks without sampling a skeleton or dispatching animation events.</summary>
public static class AnimationGraphSampling
{
    public static void AdvanceInputs(AnimationMixerPlayable mixer, float dt)
    {
        for (int i = 0; i < mixer.GetInputCount(); i++)
        {
            Playable input = mixer.GetInput(i);
            if (input.IsValid()) input.SetTime(input.GetTime() + input.GetSpeed() * dt);
        }
    }
}
