using UnityEngine;

public enum ConsoleAnchor
{
    Top = 0,
    Bottom = 1,
    Left = 2,
    Right = 3,
}

public static class ConsoleAnchorExtensions
{
    public static Vector4 GetBoundsRect(this ConsoleAnchor anchor) => anchor switch
    {
        ConsoleAnchor.Top    => new Vector4(0f,    0.66f, 1f,    1f),
        ConsoleAnchor.Bottom => new Vector4(0f,    0f,    1f,    0.34f),
        ConsoleAnchor.Left   => new Vector4(0f,    0f,    0.34f, 1f),
        ConsoleAnchor.Right  => new Vector4(0.66f, 0f,    1f,    1f),
        _                    => new Vector4(0f,    0.66f, 1f,    1f),
    };

    public static Vector2 GetInputLineOrigin(this ConsoleAnchor anchor)
    {
        Vector4 r = anchor.GetBoundsRect();
        const float padX = 0.008f;
        const float padY = 0.014f;   // leaves room for cursor descenders inside the backdrop
        return new Vector2(r.x + padX, r.y + padY);
    }
}
