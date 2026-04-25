using System;
using System.Collections.Generic;

public static class ServiceLocator
{
    static readonly Dictionary<Type, object> _services = new();

    public static void Register<T>(T service) where T : class
    {
        _services[typeof(T)] = service ?? throw new ArgumentNullException(nameof(service));
    }

    public static T Get<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service))
            return (T)service;

        throw new InvalidOperationException($"Service {typeof(T).Name} not registered.");
    }

    public static bool TryGet<T>(out T service) where T : class
    {
        if (_services.TryGetValue(typeof(T), out var obj))
        {
            service = (T)obj;
            return true;
        }

        service = null;
        return false;
    }

    public static void Clear() => _services.Clear();
}
