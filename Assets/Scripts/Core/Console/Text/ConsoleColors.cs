using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared named-color table used by both <see cref="ConsoleColorTagParser"/> (for
/// inline <c>&lt;color=name&gt;</c> markup) and the <c>ColorParser</c> in
/// <c>ConsoleArgumentParsers</c> (for <c>Color</c>-typed command arguments).
/// </summary>
public static class ConsoleColors
{
    public static readonly Dictionary<string, Color> Named = new(System.StringComparer.OrdinalIgnoreCase)
    {
        { "red",     Color.red },
        { "green",   Color.green },
        { "blue",    Color.blue },
        { "yellow",  Color.yellow },
        { "cyan",    Color.cyan },
        { "magenta", Color.magenta },
        { "white",   Color.white },
        { "black",   Color.black },
        { "grey",    Color.grey },
        { "gray",    Color.grey },
        { "orange",  new Color(1f, 0.6f, 0.1f, 1f) },
    };
}
