using System.Text;
using UnityEngine;

/// <summary>
/// Cursor-aware text buffer for the console input line.
/// Cursor sits between glyphs: position 0 is before the first char, position Length is after the last.
/// </summary>
public sealed class ConsoleInputBuffer
{
    readonly StringBuilder _sb = new();
    int _cursor;

    public string Text => _sb.ToString();
    public int Length => _sb.Length;
    public int CursorPos => _cursor;

    public void Insert(char c)
    {
        _sb.Insert(_cursor, c);
        _cursor++;
    }

    public void Insert(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        _sb.Insert(_cursor, s);
        _cursor += s.Length;
    }

    /// <summary>Replace the entire content and place the cursor at the end.</summary>
    public void Set(string s)
    {
        _sb.Clear();
        if (!string.IsNullOrEmpty(s)) _sb.Append(s);
        _cursor = _sb.Length;
    }

    public void Backspace()
    {
        if (_cursor > 0)
        {
            _sb.Remove(_cursor - 1, 1);
            _cursor--;
        }
    }

    public void Delete()
    {
        if (_cursor < _sb.Length)
            _sb.Remove(_cursor, 1);
    }

    public void MoveLeft() => _cursor = Mathf.Max(0, _cursor - 1);
    public void MoveRight() => _cursor = Mathf.Min(_sb.Length, _cursor + 1);
    public void MoveHome() => _cursor = 0;
    public void MoveEnd() => _cursor = _sb.Length;

    public void Clear()
    {
        _sb.Clear();
        _cursor = 0;
    }
}
