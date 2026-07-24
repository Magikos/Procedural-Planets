using UnityEngine;

public readonly struct ScatterInstance
{
    public readonly ulong Id;
    public readonly Vector3 PositionWS;
    public readonly Quaternion Rotation;
    public readonly float Scale;
    public readonly int PrototypeIndex;

    public ScatterInstance(ulong id, Vector3 positionWS, Quaternion rotation, float scale, int prototypeIndex)
    {
        Id = id;
        PositionWS = positionWS;
        Rotation = rotation;
        Scale = scale;
        PrototypeIndex = prototypeIndex;
    }
}
