using UnityEngine;

/// <summary>Complete action recipes using the existing three-stage creature capture fixture.</summary>
public static class CreatureContinuityReviewCapture
{
    public static string Capture(string directory, string species = "Wolf", bool swim = false) =>
        CreatureQualityReviewCapture.Capture(directory, species, swim ? "swim" : "continuity");

    internal static string Configure(CreatureAnimationView view, float seconds, bool swim)
    {
        view.LookTarget = view.Root.position + view.Root.right * 2f + view.Root.forward * 2f;
        if (swim)
        {
            view.Swimming = seconds >= 1f && seconds < 3f && !(seconds >= 1.1f && seconds < 1.2f);
            return seconds < 1f ? "supported lead-in" : seconds < 1.1f ? "swim entry" : seconds < 1.2f ? "entry interrupted"
                : seconds < 3f ? "swim retrigger and hold" : "return to support";
        }
        view.Resting = seconds >= 1f && seconds < 2f;
        view.Sleeping = seconds >= 2.15f && seconds < 3.4f;
        view.Drinking = seconds >= 6f && seconds < 7f && !(seconds >= 6.2f && seconds < 6.3f);
        if (view.Pose != null)
            view.Pose.ChainsEnabled = !(seconds >= 4.4f && seconds < 4.55f || seconds >= 5.05f && seconds < 5.4f);
        return seconds < 1f ? "idle lead-in" : seconds < 2f ? "rest entry and hold" : seconds < 2.15f ? "rest interrupted"
            : seconds < 3.4f ? "sleep retrigger" : seconds < 4.4f ? "sleep recovery"
            : seconds < 6f ? "secondary disable and retrigger" : seconds < 7f ? "drink interruption and retrigger" : "drink recovery";
    }
}
