using System;

public struct ChunkCoord : IEquatable<ChunkCoord>
{
    public int Face;
    public int X;
    public int Y;

    public ChunkCoord(int face, int x, int y)
    {
        Face = face;
        X = x;
        Y = y;
    }

    public bool Equals(ChunkCoord other) => Face == other.Face && X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Face, X, Y);
    public override string ToString() => $"({Face}:{X},{Y})";

    public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);
    public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);
}
