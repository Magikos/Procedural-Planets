public interface IConsoleService
{
    bool IsOpen { get; }
    ConsoleAnchor Anchor { get; set; }

    void Open();
    void Close();
    void Toggle();

    void RunCommand(string commandLine);
    void Print(string text);
    void PrintLine(string text);
    void PrintWarning(string text);
    void PrintError(string text);
    void Clear();
}
