using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class ConsoleRegistry
{
    static readonly Dictionary<string, CommandData> _commands = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<Type, object> _instances = new();

    public static IReadOnlyDictionary<string, CommandData> Commands => _commands;

    public static bool TryGet(string alias, out CommandData data) => _commands.TryGetValue(alias, out data);

    public static void RegisterInstance<T>(T instance) where T : class
    {
        _instances[typeof(T)] = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    public static void UnregisterInstance(Type type) => _instances.Remove(type);

    public static object GetInstance(Type type) => _instances.TryGetValue(type, out object o) ? o : null;

    public static void Clear()
    {
        _commands.Clear();
        _instances.Clear();
    }

    public static int Scan()
    {
        _commands.Clear();
        int added = 0;

        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch
            {
                continue;
            }

            foreach (Type type in types)
            {
                if (type == null) continue;
                added += ScanType(type);
            }
        }

        return added;
    }

    static int ScanType(Type type)
    {
        string prefix = type.GetCustomAttribute<CommandPrefixAttribute>()?.Prefix;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                 | BindingFlags.Static | BindingFlags.Instance
                                 | BindingFlags.DeclaredOnly;
        int added = 0;
        MethodInfo[] methods;
        try { methods = type.GetMethods(flags); }
        catch { return 0; }

        foreach (MethodInfo method in methods)
        {
            object[] attrs;
            try { attrs = method.GetCustomAttributes(typeof(ConsoleCommandAttribute), false); }
            catch { continue; }

            foreach (ConsoleCommandAttribute attr in attrs)
            {
                string alias = string.IsNullOrEmpty(prefix) ? attr.Alias : $"{prefix}.{attr.Alias}";
                CommandData data = Build(alias, attr, method, type);
                if (_commands.ContainsKey(alias))
                {
                    Debug.LogWarning($"[ConsoleRegistry] duplicate command alias '{alias}' — keeping first registration; ignoring {type.FullName}.{method.Name}");
                    continue;
                }
                _commands[alias] = data;
                added++;
            }
        }

        return added;
    }

    static CommandData Build(string alias, ConsoleCommandAttribute attr, MethodInfo method, Type declaringType)
    {
        ParameterInfo[] paramInfos = method.GetParameters();
        var paramData = new ParameterData[paramInfos.Length];
        for (int i = 0; i < paramInfos.Length; i++)
        {
            ParameterInfo p = paramInfos[i];
            paramData[i] = new ParameterData
            {
                Name = p.Name,
                Type = p.ParameterType,
                HasDefault = p.HasDefaultValue,
                DefaultValue = p.HasDefaultValue ? p.DefaultValue : null,
                Description = p.GetCustomAttribute<ParamDescriptionAttribute>()?.Description ?? "",
                CompletionProvider = p.GetCustomAttribute<CompletionSourceAttribute>()?.ProviderType,
            };
        }

        Type returnType = method.ReturnType;
        bool isAsync = returnType == typeof(UnityEngine.Awaitable)
                    || (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(UnityEngine.Awaitable<>));

        return new CommandData
        {
            Alias = alias,
            Description = attr.Description,
            TargetType = attr.TargetType,
            DeclaringType = declaringType,
            Method = method,
            Parameters = paramData,
            ReturnType = returnType,
            IsAsync = isAsync,
        };
    }
}
