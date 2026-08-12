using NUnit.Framework;
using UnityEngine;

public class AssetBenchPlacementTests
{
    const float Eps = 1e-4f;

    [Test]
    public void BuildTangentBasis_IsOrthonormalToUp()
    {
        Vector3 up = new Vector3(0.3f, 0.9f, -0.2f).normalized;
        AssetBenchPlacement.BuildTangentBasis(up, out Vector3 right, out Vector3 forward);

        Assert.AreEqual(1f, right.magnitude, Eps, "right not unit length");
        Assert.AreEqual(1f, forward.magnitude, Eps, "forward not unit length");
        Assert.AreEqual(0f, Vector3.Dot(right, up), Eps, "right not perpendicular to up");
        Assert.AreEqual(0f, Vector3.Dot(forward, up), Eps, "forward not perpendicular to up");
        Assert.AreEqual(0f, Vector3.Dot(right, forward), Eps, "basis not orthogonal");
    }

    [Test]
    public void BuildTangentBasis_HandlesPolarUp()
    {
        // A naive Cross(up, Vector3.up) degenerates at the pole; the seed must swap axes there.
        AssetBenchPlacement.BuildTangentBasis(Vector3.up, out Vector3 right, out Vector3 forward);

        Assert.AreEqual(1f, right.magnitude, Eps);
        Assert.AreEqual(1f, forward.magnitude, Eps);
        Assert.AreEqual(0f, Vector3.Dot(right, Vector3.up), Eps);
        Assert.AreEqual(0f, Vector3.Dot(forward, Vector3.up), Eps);
    }

    [Test]
    public void SlotDirection_ProducesUnitDirections()
    {
        BenchSlot slot = AssetBenchPlacement.SlotDirection(Vector3.forward, 0, 0.01f, 0.003f, 0.005f);

        Assert.AreEqual(1f, slot.CandidateDir.magnitude, Eps);
        Assert.AreEqual(1f, slot.ReferenceDir.magnitude, Eps);
    }

    [Test]
    public void SlotDirection_SeparatesConsecutiveIndices()
    {
        BenchSlot a = AssetBenchPlacement.SlotDirection(Vector3.forward, 0, 0.01f, 0.003f, 0.005f);
        BenchSlot b = AssetBenchPlacement.SlotDirection(Vector3.forward, 1, 0.01f, 0.003f, 0.005f);

        Assert.Greater(Vector3.Angle(a.CandidateDir, b.CandidateDir), 0.1f,
            "consecutive slots must not overlap");
    }

    [Test]
    public void SlotDirection_PairGapIsSmallerThanStride()
    {
        BenchSlot a = AssetBenchPlacement.SlotDirection(Vector3.forward, 0, 0.01f, 0.003f, 0.005f);
        BenchSlot b = AssetBenchPlacement.SlotDirection(Vector3.forward, 1, 0.01f, 0.003f, 0.005f);

        float withinPair = Vector3.Angle(a.CandidateDir, a.ReferenceDir);
        float betweenPairs = Vector3.Angle(a.CandidateDir, b.CandidateDir);

        Assert.Less(withinPair, betweenPairs, "a pair must read as closer together than two pairs");
    }

    [Test]
    public void SlotDirection_IsStableAtThePole()
    {
        BenchSlot slot = AssetBenchPlacement.SlotDirection(Vector3.up, 3, 0.01f, 0.003f, 0.005f);

        Assert.IsFalse(float.IsNaN(slot.CandidateDir.x), "NaN at pole");
        Assert.AreEqual(1f, slot.CandidateDir.magnitude, Eps);
        Assert.AreEqual(1f, slot.ReferenceDir.magnitude, Eps);
    }

    [Test]
    public void ViewpointDir_SitsBackFromTheSlot()
    {
        BenchSlot slot = AssetBenchPlacement.SlotDirection(Vector3.forward, 2, 0.01f, 0.003f, 0.005f);
        Vector3 view = AssetBenchPlacement.ViewpointDir(slot, 0.004f);

        Assert.AreEqual(1f, view.magnitude, Eps);

        Vector3 mid = (slot.CandidateDir + slot.ReferenceDir).normalized;
        Assert.Greater(Vector3.Angle(view, mid), 0.05f, "viewpoint must be offset from the pair it frames");
    }

    [Test]
    public void ViewpointDir_IsEquidistantFromBothMembersOfThePair()
    {
        BenchSlot slot = AssetBenchPlacement.SlotDirection(Vector3.forward, 1, 0.01f, 0.003f, 0.005f);
        Vector3 view = AssetBenchPlacement.ViewpointDir(slot, 0.004f);

        float toCandidate = Vector3.Angle(view, slot.CandidateDir);
        float toReference = Vector3.Angle(view, slot.ReferenceDir);

        // Framing must be symmetric or the comparison is biased toward whichever is nearer the camera.
        Assert.AreEqual(toCandidate, toReference, 0.01f, "viewpoint is not centred on the pair");
    }
}
