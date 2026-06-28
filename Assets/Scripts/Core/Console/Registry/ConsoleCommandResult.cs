public readonly struct ConsoleCommandResult
{
    public readonly bool Success;
    public readonly string CommandLine;
    public readonly string Alias;
    public readonly string Output;
    public readonly string Error;
    public readonly string Usage;
    public readonly bool IsAsync;
    public readonly bool IsCancellable;

    internal readonly object AwaitableResult;

    internal ConsoleCommandResult(
        bool success,
        string commandLine,
        string alias,
        string output,
        string error,
        string usage,
        bool isAsync,
        bool isCancellable,
        object awaitableResult)
    {
        Success = success;
        CommandLine = commandLine ?? "";
        Alias = alias ?? "";
        Output = output ?? "";
        Error = error ?? "";
        Usage = usage ?? "";
        IsAsync = isAsync;
        IsCancellable = isCancellable;
        AwaitableResult = awaitableResult;
    }

    public bool HasOutput => !string.IsNullOrEmpty(Output);
    public bool HasError => !string.IsNullOrEmpty(Error);

    internal ConsoleCommandResult WithOutput(string output)
    {
        return new ConsoleCommandResult(
            true,
            CommandLine,
            Alias,
            output,
            "",
            Usage,
            false,
            IsCancellable,
            null);
    }

    internal ConsoleCommandResult WithError(string error)
    {
        return new ConsoleCommandResult(
            false,
            CommandLine,
            Alias,
            "",
            error,
            Usage,
            false,
            IsCancellable,
            null);
    }

    internal static ConsoleCommandResult Failed(
        string commandLine,
        string alias,
        string error,
        string usage = "")
    {
        return new ConsoleCommandResult(
            false,
            commandLine,
            alias,
            "",
            error,
            usage,
            false,
            false,
            null);
    }
}
