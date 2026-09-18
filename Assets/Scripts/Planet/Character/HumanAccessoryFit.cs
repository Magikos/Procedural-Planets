using UnityEngine;

// Baked garment-surface movement in the attachment parent's local coordinates.
public sealed class HumanAccessoryFit : MonoBehaviour
{
    public HumanBodyReview Body;
    public Vector3 NeutralLocalPosition;
    public Vector3 MuscularOffset;
    public Vector3 HeavyOffset;
    public Vector3 SkinnyOffset;
    public Vector3 FeminineOffset;

    void LateUpdate() => Apply();

    public void Apply()
    {
        if (Body == null) return;
        transform.localPosition = NeutralLocalPosition + EvaluateOffset(
            new Vector4(Body.Muscular, Body.Heavy, Body.Skinny, Body.Feminine), Body.SkeletonFit);
    }

    public Vector3 EvaluateOffset(Vector4 weights, float fit)
    {
        return (MuscularOffset * weights.x + HeavyOffset * weights.y
            + SkinnyOffset * weights.z + FeminineOffset * weights.w) * (fit / 10000f);
    }
}
