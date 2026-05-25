using System;
using System.Reflection;
using UnityEngine;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class HandleEventBusAttribute : Attribute
{
    public Type EventType { get; set; }
    public bool Deferred { get; set; } = false;
    public bool OneTime { get; set; } = false;

    public HandleEventBusAttribute(Type eventType)
    {
        EventType = eventType;
    }
}

/// <summary>
/// Marker interface for classes that want to automatically bind to the EventBus.
/// Implementing this interface allows the EventBusAutoBinder to scan for methods with the HandleEventBusAttribute and register them as event handlers without manual setup.
/// </summary>
/// <example>
/// Call BindEvents on the instance, typically in Awake or Start: this will bind all methods marked with [HandleEventBus] attributes to the appropriate EventBus.
/// <code>
/// public class MyComponent : MonoBehaviour, IAutoEventBind
/// {
///     void Awake()
///     {
///         this.BindEvents();
///     }
/// 
///     [HandleEventBus(typeof(MyEvent))]
///     private void OnMyEvent(MyEvent evt) { ... }
/// }
/// </code>
/// </example>
public interface IAutoEventBind { }

public static class EventBusExtensions
{
    public static T BindEvents<T>(this T instance) where T : IAutoEventBind
    {
        EventBusAutoBinder.Bind(instance);
        return instance;
    }

    public static T UnbindEvents<T>(this T instance) where T : IAutoEventBind
    {
        EventBusAutoBinder.Unbind(instance);
        return instance;
    }
}

public static class EventBusAutoBinder
{
    public static void Bind(object target)
    {
        BindOrUnbind(target, bind: true);
    }

    public static void Unbind(object target)
    {
        BindOrUnbind(target, bind: false);
    }

    static void BindOrUnbind(object target, bool bind)
    {
        if (target == null) return;

        var type = target.GetType();
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        foreach (var method in methods)
        {
            var attributes = method.GetCustomAttributes(inherit: true);
            foreach (var attr in attributes)
            {
                if (!(attr is HandleEventBusAttribute eventAttribute))
                    continue;

                var eventType = eventAttribute.EventType;
                if (!IsValidEventType(eventType, type, method))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != eventType)
                {
                    LoggerProvider.Log(LogLevel.Error, "EventBusAutoBinder", $"Method '{method.Name}' must take exactly one parameter of type '{eventType.Name}'");
                    continue;
                }

                var delegateType = typeof(Action<>).MakeGenericType(eventType);
                var handler = Delegate.CreateDelegate(delegateType, target, method);
                var busType = typeof(EventBus<>).MakeGenericType(eventType);

                if (bind)
                {
                    string methodName = eventAttribute.OneTime
                        ? (eventAttribute.Deferred ? "ListenOnceDeferred" : "ListenOnce")
                        : (eventAttribute.Deferred ? "ListenDeferred" : "Listen");

                    MethodInfo listenMethod = FindBusMethod(busType, methodName, delegateType, parameterCount: 2);
                    if (listenMethod == null)
                    {
                        LoggerProvider.Log(LogLevel.Error, "EventBusAutoBinder", $"Could not find {busType.Name}.{methodName} for {eventType.Name}");
                        continue;
                    }

                    listenMethod.Invoke(null, new object[] { handler, null });

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    LoggerProvider.Log(LogLevel.Debug, "EventBusAutoBinder", $"Bound {type.Name}.{method.Name} to {busType.Name}.{methodName}()");
#endif
                }
                else
                {
                    MethodInfo unlistenMethod = FindBusMethod(busType, "Unlisten", delegateType, parameterCount: 1);
                    if (unlistenMethod == null)
                    {
                        LoggerProvider.Log(LogLevel.Error, "EventBusAutoBinder", $"Could not find {busType.Name}.Unlisten for {eventType.Name}");
                        continue;
                    }

                    unlistenMethod.Invoke(null, new object[] { handler });

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    LoggerProvider.Log(LogLevel.Debug, "EventBusAutoBinder", $"Unbound {type.Name}.{method.Name} from {busType.Name}.Unlisten()");
#endif
                }
            }
        }
    }

    static bool IsValidEventType(Type eventType, Type targetType, MethodInfo method)
    {
        if (eventType == null)
        {
            LoggerProvider.Log(LogLevel.Error, "EventBusAutoBinder", $"{targetType.Name}.{method.Name} has a null event type.");
            return false;
        }

        if (!eventType.IsValueType || !typeof(IGameEvent).IsAssignableFrom(eventType))
        {
            LoggerProvider.Log(LogLevel.Error, "EventBusAutoBinder", $"{eventType.Name} must be a struct implementing IGameEvent.");
            return false;
        }

        return true;
    }

    static MethodInfo FindBusMethod(Type busType, string methodName, Type delegateType, int parameterCount)
    {
        var methods = busType.GetMethods(BindingFlags.Public | BindingFlags.Static);
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].Name != methodName)
                continue;

            var parameters = methods[i].GetParameters();
            if (parameters.Length != parameterCount)
                continue;

            if (parameters[0].ParameterType == delegateType)
                return methods[i];
        }

        return null;
    }
}
