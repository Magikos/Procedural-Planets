using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EventBusProcessor : MonoBehaviour
{
    static EventBusProcessor _instance;
    static readonly List<Action> _processors = new();

    void Awake()
    {
        if (_instance != null && _instance != this)
            throw new InvalidOperationException(
                "Only one EventBusProcessor may exist. GameBootstrap owns the application event processor.");

        _instance = this;
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    void LateUpdate()
    {
        for (int i = 0; i < _processors.Count; i++)
        {
            try
            {
                _processors[i]?.Invoke();
            }
            catch (Exception ex)
            {
                LoggerProvider.LogException("EventBus", ex);
            }
        }
    }

    public static void RegisterProcessor(Action processor)
    {
        if (_instance == null)
        {
            throw new InvalidOperationException(
                "EventBusProcessor is unavailable. GameBootstrap must initialize before deferred event listeners are registered.");
        }

        if (!_processors.Contains(processor))
            _processors.Add(processor);
    }

    public static void ClearProcessors() => _processors.Clear();
}
