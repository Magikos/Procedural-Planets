using System;
using System.Collections.Generic;
using UnityEngine;

public class EventBusProcessor : MonoBehaviour
{
    static EventBusProcessor _instance;
    static readonly List<Action> _processors = new();

    void Awake()
    {
        if (_instance != null)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
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
                Debug.LogException(ex);
            }
        }
    }

    public static void RegisterProcessor(Action processor)
    {
        if (!_processors.Contains(processor))
            _processors.Add(processor);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        var go = new GameObject("[EventBusProcessor]");
        go.AddComponent<EventBusProcessor>();
    }
}
