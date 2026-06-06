using System.Threading;
using UnityEngine;

public enum TestEnumOption
{
    Alpha,
    Beta,
    Gamma,
    Delta,
    Epsilon,
    Zeta,
}

/// <summary>
/// Interactive proof-of-life commands for verifying console subsystems. Live under the
/// <c>test.console.*</c> prefix. Other subsystems should add their own
/// <c>Test&lt;System&gt;Commands.cs</c> files (e.g. <c>test.weather.*</c>, <c>test.grass.*</c>)
/// colocated with the system being tested.
/// </summary>
[CommandPrefix("test.console")]
public static class TestConsoleCommands
{
    [ConsoleCommand("colors", "Print sample lines in each color type, plus inline markup.")]
    public static void Colors()
    {
        if (!ServiceLocator.TryGet<IConsoleService>(out var c)) return;
        c.PrintLine("Normal line (default)");
        c.PrintLine("Print line (output color)");
        c.PrintWarning("Warning line (amber)");
        c.PrintError("Error line (red)");
        c.PrintLine("Inline: <color=red>red</color>, <color=cyan>cyan</color>, <color=yellow>yellow</color>, <color=green>green</color>");
        c.PrintLine("Hex: <color=#ff8800>orange</color>, <color=#00ff88>mint</color>, <color=#aa55ff>violet</color>");
        c.PrintLine("Escape: literal \\<color\\> stays plain text");
    }

    [ConsoleCommand("async", "Sleep N seconds then complete. Exercises the async spinner.")]
    public static async Awaitable Async(float seconds = 2f)
    {
        await DelayUnscaledAsync(seconds);
    }

    [ConsoleCommand("async-result", "Sleep N seconds then return a string. Exercises Awaitable<T>.")]
    public static async Awaitable<string> AsyncResult(float seconds = 2f)
    {
        await DelayUnscaledAsync(seconds);
        return $"slept {seconds:F1}s and returned this value";
    }

    [ConsoleCommand("async-fail", "Sleep N seconds then throw. Exercises async error UX.")]
    public static async Awaitable AsyncFail(float seconds = 2f)
    {
        await DelayUnscaledAsync(seconds);
        throw new System.InvalidOperationException("test async failure (this is expected)");
    }

    [ConsoleCommand("async-cancellable", "Cancellable async sleep — try both console.abandon and console.cancel against it.")]
    public static async Awaitable AsyncCancellable(float seconds = 5f, CancellationToken ct = default)
    {
        float endTime = Time.unscaledTime + Mathf.Max(0f, seconds);
        while (Time.unscaledTime < endTime)
        {
            ct.ThrowIfCancellationRequested();
            await Awaitable.NextFrameAsync();
        }
    }

    [ConsoleCommand("enum", "Print the chosen enum option. Tests enum parameter completion popup.")]
    public static string SelectEnum(TestEnumOption option)
    {
        return $"chose {option}";
    }

    [ConsoleCommand("error", "Throw a synchronous exception. Exercises sync error handling.")]
    public static void Error()
    {
        throw new System.InvalidOperationException("test sync failure (this is expected)");
    }

    [ConsoleCommand("types", "Echo back each typed argument. Exercises all built-in parsers.")]
    public static string Types(int i, float f, bool b, Vector3 v, Color c)
    {
        return $"int={i}, float={f}, bool={b}, vec3={v}, color={c}";
    }

    [ConsoleCommand("spam", "Print N lines fast. Exercises scrollback ring trim and scrollbar.")]
    public static void Spam(int count = 200)
    {
        if (!ServiceLocator.TryGet<IConsoleService>(out var con)) return;
        for (int i = 0; i < count; i++)
            con.PrintLine($"spam line {i + 1}/{count}");
    }

    static async Awaitable DelayUnscaledAsync(float seconds)
    {
        float endTime = Time.unscaledTime + Mathf.Max(0f, seconds);
        while (Time.unscaledTime < endTime)
            await Awaitable.NextFrameAsync();
    }
}
