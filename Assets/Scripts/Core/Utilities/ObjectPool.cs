using System;
using System.Collections.Generic;

// FUTURE: Generic object pool for reusable instances (mesh chunks, particles, etc.).
// Will be used when LOD system or entity spawning is implemented.
public interface IObjectPool<T> where T : class
{
    T Get();
    void Return(T instance);
    void Prewarm(int count);
    int CountActive { get; }
    int CountInactive { get; }
}

public class ObjectPool<T> : IObjectPool<T> where T : class
{
    readonly Func<T> _createFunc;
    readonly Action<T> _onGet;
    readonly Action<T> _onReturn;
    readonly Stack<T> _pool = new();

    public int CountActive { get; private set; }
    public int CountInactive => _pool.Count;

    public ObjectPool(Func<T> createFunc, Action<T> onGet = null, Action<T> onReturn = null, int initialCapacity = 0)
    {
        _createFunc = createFunc ?? throw new ArgumentNullException(nameof(createFunc));
        _onGet = onGet;
        _onReturn = onReturn;

        if (initialCapacity > 0)
            Prewarm(initialCapacity);
    }

    public T Get()
    {
        T instance = _pool.Count > 0 ? _pool.Pop() : _createFunc();
        CountActive++;
        _onGet?.Invoke(instance);
        return instance;
    }

    public void Return(T instance)
    {
        if (instance == null) return;
        CountActive--;
        _onReturn?.Invoke(instance);
        _pool.Push(instance);
    }

    public void Prewarm(int count)
    {
        for (int i = 0; i < count; i++)
            _pool.Push(_createFunc());
    }
}
