using System;
using UnityEngine;

public static class DebugConsoleBootstrap
{
    public const string AllowDebugFlag = "--allowDebug";

    public static ConsoleController Initialize()
    {
        if (!IsConsoleAllowed())
        {
            LoggerProvider.Get().Log(LogLevel.Info, "DebugConsole",
                "Debug console disabled (release build without --allowDebug).");
            return null;
        }

        if (ServiceLocator.TryGet<IConsoleService>(out var existing) && existing is ConsoleController existingController)
            return existingController;

        int commandCount = ConsoleRegistry.Scan();

        var go = new GameObject("[DebugConsole]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        var controller = go.AddComponent<ConsoleController>();
        ServiceLocator.Register<IConsoleService>(controller);
        LoggerProvider.Get().Log(LogLevel.Info, "DebugConsole",
            $"Debug console initialized. Toggle with ` (backtick). {commandCount} commands registered.");
        return controller;
    }

    public static bool IsConsoleAllowed()
    {
        if (Debug.isDebugBuild || Application.isEditor)
            return true;

        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, AllowDebugFlag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
