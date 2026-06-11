using System;
using System.Collections.Generic;
using UnityEngine;

public class EventBusProcessor : MonoBehaviour
{
    static EventBusProcessor _instance;
    static readonly List<Action> _processors = new();

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
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
        EnsureProcessorExists();
        if (!_processors.Contains(processor))
            _processors.Add(processor);
    }

    public static void ClearProcessors() => _processors.Clear();

    static void EnsureProcessorExists()
    {
        if (_instance != null) return;

        _instance = FindAnyObjectByType<EventBusProcessor>();
        if (_instance != null) return;

        var go = new GameObject("[EventBusProcessor]");
        go.AddComponent<EventBusProcessor>();
    }
}
