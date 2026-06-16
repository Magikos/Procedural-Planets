using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public interface IWorldContext : IDisposable
{
    CancellationToken LifetimeToken { get; }
    ISettingsService Settings { get; }
    WorldLoadRequest LoadRequest { get; }
    bool IsDisposed { get; }

    T Register<T>(T service) where T : class;
    T Get<T>() where T : class;
    bool TryGet<T>(out T service) where T : class;
    void Unregister<T>(T service) where T : class;
    void ValidateRequired(IReadOnlyList<Type> serviceTypes);
    void ApplySettingsOverrides();
    void TrackInitializer(object initializer);
    void Teardown();
    void Cancel();
}

public interface IWorldServiceRegistrar
{
    void RegisterWorldServices(IWorldContext context);
}

public interface IWorldSettingsRegistrar
{
    IReadOnlyList<Type> RequiredSettingsTypes { get; }
    void RegisterWorldSettings(ISettingsService settings);
}

public interface IWorldTeardown
{
    void TeardownWorld();
}

public interface ICloudRuntime
{
}

public interface IAtmosphereRuntime
{
}

public sealed class WorldContext : IWorldContext
{
    readonly Dictionary<Type, object> _services = new();
    readonly List<IWorldTeardown> _initialized = new();
    readonly CancellationTokenSource _lifetime = new();
    bool _teardownComplete;
    bool _settingsOverridesApplied;

    public CancellationToken LifetimeToken => _lifetime.Token;
    public ISettingsService Settings { get; }
    public WorldLoadRequest LoadRequest { get; }
    public bool IsDisposed { get; private set; }

    public WorldContext(WorldLoadRequest loadRequest = null)
    {
        LoadRequest = loadRequest;
        Settings = new SettingsService();
        Register<ISettingsService>(Settings);
    }

    public T Register<T>(T service) where T : class
    {
        ThrowIfDisposed();
        if (service == null)
            throw new ArgumentNullException(nameof(service));

        Type type = typeof(T);
        if (_services.TryGetValue(type, out object existing)
            && ServiceLocator.IsAlive(existing)
            && !ReferenceEquals(existing, service))
        {
            throw new InvalidOperationException(
                $"World service {type.Name} already registered by {ServiceLocator.Describe(existing)}; " +
                $"cannot register {ServiceLocator.Describe(service)}.");
        }

        _services[type] = service;
        return service;
    }

    public T Get<T>() where T : class
    {
        if (TryGet(out T service))
            return service;

        throw new InvalidOperationException($"World service {typeof(T).Name} not registered.");
    }

    public bool TryGet<T>(out T service) where T : class
    {
        if (IsDisposed)
        {
            service = null;
            return false;
        }

        Type type = typeof(T);
        if (_services.TryGetValue(type, out object existing))
        {
            if (!ServiceLocator.IsAlive(existing))
            {
                _services.Remove(type);
                service = null;
                return false;
            }

            service = (T)existing;
            return true;
        }

        service = null;
        return false;
    }

    public void Unregister<T>(T service) where T : class
    {
        if (IsDisposed)
            return;

        Type type = typeof(T);
        if (!_services.TryGetValue(type, out object existing))
            return;

        if (ReferenceEquals(existing, service) || !ServiceLocator.IsAlive(existing))
            _services.Remove(type);
    }

    public void ValidateRequired(IReadOnlyList<Type> serviceTypes)
    {
        ThrowIfDisposed();
        for (int i = 0; i < serviceTypes.Count; i++)
        {
            Type type = serviceTypes[i];
            if (!_services.TryGetValue(type, out object service) || !ServiceLocator.IsAlive(service))
                throw new InvalidOperationException($"Required world service {type.Name} is not registered.");
        }
    }

    public void ApplySettingsOverrides()
    {
        ThrowIfDisposed();
        if (_settingsOverridesApplied)
            return;

        IReadOnlyList<IWorldSettingsOverride> overrides = LoadRequest?.SettingsOverrides;
        if (overrides != null)
        {
            for (int i = 0; i < overrides.Count; i++)
            {
                IWorldSettingsOverride settingsOverride = overrides[i]
                    ?? throw new InvalidOperationException($"World settings override at index {i} is null.");
                settingsOverride.Apply(Settings);
            }
        }

        _settingsOverridesApplied = true;
    }

    public void TrackInitializer(object initializer)
    {
        ThrowIfDisposed();
        if (initializer is not IWorldTeardown teardown)
            return;

        for (int i = _initialized.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_initialized[i], teardown))
                _initialized.RemoveAt(i);
        }

        _initialized.Add(teardown);
    }

    public void Teardown()
    {
        if (_teardownComplete)
            return;

        _teardownComplete = true;
        Cancel();
        List<Exception> failures = null;
        for (int i = _initialized.Count - 1; i >= 0; i--)
        {
            IWorldTeardown teardown = _initialized[i];
            if (!ServiceLocator.IsAlive(teardown))
                continue;

            try
            {
                teardown.TeardownWorld();
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(
                    new InvalidOperationException(
                        $"World teardown failed in {ServiceLocator.Describe(teardown)}.", ex));
            }
        }

        _initialized.Clear();
        if (failures != null)
            throw new AggregateException("One or more world teardown operations failed.", failures);
    }

    public void Cancel()
    {
        if (!IsDisposed && !_lifetime.IsCancellationRequested)
            _lifetime.Cancel();
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        Exception teardownFailure = null;
        try
        {
            Teardown();
        }
        catch (Exception ex)
        {
            teardownFailure = ex;
        }
        finally
        {
            _services.Clear();
            _lifetime.Dispose();
            IsDisposed = true;
        }

        if (teardownFailure != null)
            throw teardownFailure;
    }

    void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(WorldContext));
    }
}

public readonly struct WorldReadyEvent : IGameEvent
{
    public readonly IWorldContext Context;

    public WorldReadyEvent(IWorldContext context)
    {
        Context = context;
    }
}

public static class ServiceLocator
{
    static readonly Dictionary<Type, object> _services = new();
    static IWorldContext _activeWorld;

    public static bool HasActiveWorld => _activeWorld != null && !_activeWorld.IsDisposed;

    public static IWorldContext GetWorld()
    {
        if (HasActiveWorld)
            return _activeWorld;

        throw new InvalidOperationException("No active world context.");
    }

    public static void ActivateWorld(IWorldContext world)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (world.IsDisposed)
            throw new ObjectDisposedException(nameof(world));
        if (HasActiveWorld && !ReferenceEquals(_activeWorld, world))
            throw new InvalidOperationException("A world context is already active.");

        _activeWorld = world;
    }

    public static void DeactivateWorld(IWorldContext world)
    {
        if (ReferenceEquals(_activeWorld, world))
            _activeWorld = null;
    }

    public static T RegisterWorld<T>(T service) where T : class =>
        GetWorld().Register(service);

    public static void UnregisterWorld<T>(T service) where T : class
    {
        if (HasActiveWorld)
            _activeWorld.Unregister(service);
    }

    public static T Register<T>(T service) where T : class
    {
        if (service == null)
            throw new ArgumentNullException(nameof(service));

        Type type = typeof(T);
        if (_services.TryGetValue(type, out object existing) && IsAlive(existing) && !ReferenceEquals(existing, service))
            throw new InvalidOperationException(
                $"Service {type.Name} already registered by {Describe(existing)}; cannot register {Describe(service)}.");

        _services[type] = service;
        return service;
    }

    public static T Get<T>() where T : class
    {
        if (TryGet(out T service))
            return service;

        throw new InvalidOperationException($"Service {typeof(T).Name} not registered.");
    }

    public static bool TryGet<T>(out T service) where T : class
    {
        if (HasActiveWorld && _activeWorld.TryGet(out service))
            return true;

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
        {
            _services.Remove(type);
            if (ReferenceEquals(existing, service) && existing is IDisposable disposable)
                disposable.Dispose();
        }
    }

    public static void Clear()
    {
        if (_activeWorld != null)
        {
            IWorldContext world = _activeWorld;
            _activeWorld = null;
            if (!world.IsDisposed)
                world.Dispose();
        }

        foreach (var service in _services.Values)
        {
            if (service is IDisposable disposable)
                disposable.Dispose();
        }
        _services.Clear();
    }

    internal static bool IsAlive(object service)
    {
        if (service == null)
            return false;

        return service is not UnityEngine.Object unityObject || unityObject != null;
    }

    internal static string Describe(object service)
    {
        if (service == null)
            return "null";

        if (service is Component component)
            return $"{service.GetType().Name} on {component.gameObject.name}";

        return service.GetType().Name;
    }
}
