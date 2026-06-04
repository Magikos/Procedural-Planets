using System.Collections.Generic;

/// <summary>Message classification used for output colouring.</summary>
public enum ConsoleMessageType
{
    Normal,
    Input,      // echoed command ("> echo hello")
    Output,     // command return value / Print
    Warning,
    Error,
    Exception,
    Log,        // Unity Debug.Log passthrough
}

public readonly struct ConsoleMessage
{
    public readonly string Text;
    public readonly ConsoleMessageType Type;
    public ConsoleMessage(string text, ConsoleMessageType type) { Text = text; Type = type; }
}

public sealed class ConsoleScrollback
{
    readonly List<ConsoleMessage> _lines = new();
    int _capacity = 1000;

    /// <summary>Incremented on every mutation. Renderer caches this to skip rebuilds when unchanged.</summary>
    public int Version { get; private set; }

    public int Count => _lines.Count;

    public int Capacity
    {
        get => _capacity;
        set
        {
            if (value < 1) value = 1;
            _capacity = value;
            TrimToCapacity();
        }
    }

    public IReadOnlyList<ConsoleMessage> Lines => _lines;

    public void Append(string line, ConsoleMessageType type = ConsoleMessageType.Normal)
    {
        _lines.Add(new ConsoleMessage(line ?? "", type));
        TrimToCapacity();
        Version++;
    }

    public void Clear()
    {
        if (_lines.Count == 0) return;
        _lines.Clear();
        Version++;
    }

    /// <summary>
    /// Returns a slice of <paramref name="count"/> messages ending at
    /// <c>Count - 1 - scrollOffset</c> (so offset 0 = newest at bottom).
    /// </summary>
    public IReadOnlyList<ConsoleMessage> GetWindow(int count, int scrollOffset)
    {
        if (_lines.Count == 0 || count <= 0) return System.Array.Empty<ConsoleMessage>();
        int endExclusive = _lines.Count - scrollOffset;
        if (endExclusive <= 0) return System.Array.Empty<ConsoleMessage>();
        int start = System.Math.Max(0, endExclusive - count);
        int take = endExclusive - start;
        return _lines.GetRange(start, take);
    }

    void TrimToCapacity()
    {
        int over = _lines.Count - _capacity;
        if (over > 0)
            _lines.RemoveRange(0, over);
    }
}
