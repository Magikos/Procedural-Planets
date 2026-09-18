using UnityEngine;

public sealed class HumanCapeClothReview : MonoBehaviour
{
    public HumanStyleReview Review;
    public HumanBodyReview Body;
    public Cloth[] Capes = System.Array.Empty<Cloth>();
    public bool Simulate = true;
    int _motion = -1;
    float _phase;

    public bool SupportsBody => Body == null || (Mathf.Approximately(Body.SkeletonFit, 100)
        && Body.Muscular == 0 && Body.Heavy == 0 && Body.Skinny == 0 && Body.Feminine == 0);

    void LateUpdate()
    {
        bool enabled = Simulate && SupportsBody;
        bool reset = Review != null && (_motion != Review.MotionIndex
            || (Review.Paused && Mathf.Abs(_phase - Review.Phase) > .0001f));
        foreach (var cape in Capes)
        {
            if (cape == null) continue;
            if (reset && cape.enabled) cape.enabled = false;
            if (cape.enabled != enabled) cape.enabled = enabled;
            if (reset && enabled) cape.ClearTransformMotion();
        }
        if (Review != null) { _motion = Review.MotionIndex; _phase = Review.Phase; }
    }

    public void ResetCloth()
    {
        foreach (var cape in Capes)
        {
            if (cape == null) continue;
            cape.enabled = false;
            cape.enabled = Simulate && SupportsBody;
            if (cape.enabled) cape.ClearTransformMotion();
        }
    }

    void OnDisable()
    {
        foreach (var cape in Capes) if (cape != null) cape.enabled = false;
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 290, 370, 278, 140), GUI.skin.box);
        GUILayout.Label("CAPE CLOTH TRIAL");
        Simulate = GUILayout.Toggle(Simulate, "Simulate cape cloth");
        GUILayout.Label(SupportsBody ? "Pause stops the pose; cloth can settle." : "Reset body sliders for cloth. Other shapes use the rigid cape.");
        if (GUILayout.Button("Reset cape cloth")) ResetCloth();
        GUILayout.EndArea();
    }
}
