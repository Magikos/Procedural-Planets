using System.Text;

public sealed class ConsoleInputBuffer
{
    readonly StringBuilder _sb = new();

    public string Text => _sb.ToString();
    public int Length => _sb.Length;

    public void Append(char c) => _sb.Append(c);
    public void Append(string s) => _sb.Append(s);

    public void Backspace()
    {
        if (_sb.Length > 0)
            _sb.Length--;
    }

    public void Clear() => _sb.Clear();
}
