using UnityEngine;

public enum BiomeAssignmentMode
{
    Voronoi,
    DiagnosticGrid,
}

readonly struct BiomeAssignmentSample
{
    public readonly byte PrimaryId;
    public readonly byte SecondaryId;
    public readonly float SecondaryWeight;

    public BiomeAssignmentSample(byte primaryId, byte secondaryId, float secondaryWeight)
    {
        PrimaryId = primaryId;
        SecondaryId = secondaryId;
        SecondaryWeight = secondaryId != primaryId ? Mathf.Clamp01(secondaryWeight) : 0f;
    }
}

interface IBiomeAssignmentField
{
    BiomeAssignmentSample Evaluate(Vector3 pointOnUnitSphere);
    byte EvaluatePrimaryId(Vector3 pointOnUnitSphere);
}
