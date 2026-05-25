using System;
using System.Collections.Generic;
using UnityEngine;

public static class ServiceLocator
{
    static readonly Dictionary<Type, object> _services = new();

    public static void Register<T>(T service) where T : class
    {
        if (service == null)
            throw new ArgumentNullException(nameof(service));

        Type type = typeof(T);
        if (_services.TryGetValue(type, out object existing) && IsAlive(existing) && !ReferenceEquals(existing, service))
            throw new InvalidOperationException(
                $"Service {type.Name} already registered by {Describe(existing)}; cannot register {Describe(service)}.");

        _services[type] = service;
    }

    public static T Get<T>() where T : class
    {
        if (TryGet(out T service))
            return service;

        throw new InvalidOperationException($"Service {typeof(T).Name} not registered.");
    }

    public static bool TryGet<T>(out T service) where T : class
    {
        Type type = typeof(T);
        if (_services.TryGetValue(type, out object obj))
        {
            if (!IsAlive(obj))
            {
                _services.Remove(type);
                service = null;
                return false;
            }

            service = (T)obj;
            return true;
        }

        service = null;
        return false;
    }

    public static void Unregister<T>(T service) where T : class
    {
        Type type = typeof(T);
        if (!_services.TryGetValue(type, out object existing))
            return;

        if (ReferenceEquals(existing, service) || !IsAlive(existing))
            _services.Remove(type);
    }

    public static void Clear() => _services.Clear();

    static bool IsAlive(object service)
    {
        if (service == null)
            return false;

        return service is not UnityEngine.Object unityObject || unityObject != null;
    }

    static string Describe(object service)
    {
        if (service == null)
            return "null";

        if (service is Component component)
            return $"{service.GetType().Name} on {component.gameObject.name}";

        return service.GetType().Name;
    }
}
