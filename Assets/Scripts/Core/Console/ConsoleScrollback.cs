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
    public readonly long Id;
    public readonly string Text;
    public readonly ConsoleMessageType Type;
    public ConsoleMessage(long id, string text, ConsoleMessageType type) { Id = id; Text = text; Type = type; }
}

public sealed class ConsoleScrollback
{
    readonly List<ConsoleMessage> _lines = new();
    int _capacity = 1000;
    long _nextId = 1;

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

    /// <summary>Append a line and return its stable id (used later by <see cref="Replace"/>).</summary>
    public long Append(string line, ConsoleMessageType type = ConsoleMessageType.Normal)
    {
        long id = _nextId++;
        _lines.Add(new ConsoleMessage(id, line ?? "", type));
        TrimToCapacity();
        Version++;
        return id;
    }

    /// <summary>
    /// Replace the text/type of a previously appended line, identified by its id.
    /// Silent no-op if the id is no longer present (e.g. trimmed by the ring buffer).
    /// </summary>
    public void Replace(long id, string text, ConsoleMessageType type = ConsoleMessageType.Normal)
    {
        // Search from the tail — Replace is almost always called on recently-appended lines
        // (the async spinner). O(1) for the common case; degrades to O(n) only if the target
        // has been pushed far back by high-volume output.
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            if (_lines[i].Id == id)
            {
                _lines[i] = new ConsoleMessage(id, text ?? "", type);
                Version++;
                return;
            }
        }
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
