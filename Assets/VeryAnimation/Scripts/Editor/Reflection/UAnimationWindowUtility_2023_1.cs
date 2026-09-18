namespace VeryAnimation
{
    internal sealed class UAnimationWindowUtility_2023_1 : UAnimationWindowUtility
    {
        public override bool IsNodeLeftOverCurve(object state, object node)
        {
            return (bool)mi_IsNodeLeftOverCurve.Invoke(null, new object[] { state, node });
        }
    }
}
