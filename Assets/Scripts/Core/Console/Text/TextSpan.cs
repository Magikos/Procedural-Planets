using UnityEngine;

public readonly struct TextSpan
{
    public readonly Color Color;
    public readonly string Text;

    public TextSpan(Color color, string text)
    {
        Color = color;
        Text = text ?? "";
    }
}
