using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class EventBus<TEvent> where TEvent : struct, IGameEvent
{
    class Subscriber
    {
        public WeakAction Handler;
        public WeakFunc Filter;
        public bool OneTime;
        public bool Deferred;

        public bool IsDead =>
            Handler == null || Handler.IsDead || (Filter != null && Filter.IsDead);

        public bool Matches(Action<TEvent> handler) =>
            Handler != null && Handler.Matches(handler);

        public bool TryInvoke(TEvent context)
        {
            if (IsDead)
                return false;

            if (Filter != null && !Filter.Invoke(context))
                return true;

            Handler.Invoke(context);
            return true;
        }
    }

    class WeakAction
    {
        readonly WeakReference _target;
        readonly MethodInfo _method;
        readonly Action<TEvent> _staticHandler;

        public WeakAction(Action<TEvent> handler)
        {
            if (handler.Target == null)
            {
                _staticHandler = handler;
                return;
            }

            _target = new WeakReference(handler.Target);
            _method = handler.Method;
        }

        public bool IsDead => _target != null && IsDeadTarget(_target.Target);

        public bool Matches(Action<TEvent> handler)
        {
            if (handler == null)
                return false;

            if (_staticHandler != null)
                return handler.Target == null && _staticHandler.Method == handler.Method;

            return ReferenceEquals(_target?.Target, handler.Target) && _method == handler.Method;
        }

        public void Invoke(TEvent context)
        {
            if (_staticHandler != null)
            {
                _staticHandler(context);
                return;
            }

            object target = _target.Target;
            if (IsDeadTarget(target))
                return;

            _method.Invoke(target, new object[] { context });
        }
    }

    class WeakFunc
    {
        readonly WeakReference _target;
        readonly MethodInfo _method;
        readonly Func<TEvent, bool> _staticFilter;

        public WeakFunc(Func<TEvent, bool> filter)
        {
            if (filter.Target == null)
            {
                _staticFilter = filter;
                return;
            }

            _target = new WeakReference(filter.Target);
            _method = filter.Method;
        }

        public bool IsDead => _target != null && IsDeadTarget(_target.Target);

        public bool Invoke(TEvent context)
        {
            if (_staticFilter != null)
                return _staticFilter(context);

            object target = _target.Target;
            if (IsDeadTarget(target))
                return false;

            return (bool)_method.Invoke(target, new object[] { context });
        }
    }

    struct DeferredCall
    {
        public Subscriber Subscriber;
        public TEvent Context;
    }

    static readonly List<Subscriber> _subscribers = new();
    static readonly Queue<DeferredCall> _deferredQueue = new();
    static bool _registeredDeferredProcessor;

    public static void Raise(TEvent context)
    {
        for (int i = _subscribers.Count - 1; i >= 0; i--)
        {
            var sub = _subscribers[i];

            if (sub.IsDead)
            {
                _subscribers.RemoveAt(i);
                continue;
            }

            if (sub.Deferred)
            {
                _deferredQueue.Enqueue(new DeferredCall { Subscriber = sub, Context = context });
            }
            else if (!InvokeSubscriber(sub, context))
            {
                _subscribers.RemoveAt(i);
                continue;
            }

            if (sub.OneTime)
                _subscribers.RemoveAt(i);
        }
    }

    public static void Listen(Action<TEvent> handler, Func<TEvent, bool> filter = null)
        => AddSubscriber(handler, filter, oneTime: false, deferred: false);

    public static void ListenDeferred(Action<TEvent> handler, Func<TEvent, bool> filter = null)
        => AddSubscriber(handler, filter, oneTime: false, deferred: true);

    public static void ListenOnce(Action<TEvent> handler, Func<TEvent, bool> filter = null)
        => AddSubscriber(handler, filter, oneTime: true, deferred: false);

    public static void ListenOnceDeferred(Action<TEvent> handler, Func<TEvent, bool> filter = null)
        => AddSubscriber(handler, filter, oneTime: true, deferred: true);

    public static void Unlisten(Action<TEvent> handler)
    {
        if (handler == null) return;

        for (int i = _subscribers.Count - 1; i >= 0; i--)
        {
            if (_subscribers[i].Matches(handler))
                _subscribers.RemoveAt(i);
        }
    }

    public static void ClearAll()
    {
        _subscribers.Clear();
        _deferredQueue.Clear();
    }

    static void AddSubscriber(Action<TEvent> handler, Func<TEvent, bool> filter, bool oneTime, bool deferred)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        _subscribers.Add(new Subscriber
        {
            Handler = new WeakAction(handler),
            Filter = filter != null ? new WeakFunc(filter) : null,
            OneTime = oneTime,
            Deferred = deferred
        });

        if (deferred)
            EnsureDeferredProcessorRegistered();
    }

    static void EnsureDeferredProcessorRegistered()
    {
        if (_registeredDeferredProcessor) return;
        EventBusProcessor.RegisterProcessor(ProcessDeferred);
        _registeredDeferredProcessor = true;
    }

    internal static void ProcessDeferred()
    {
        while (_deferredQueue.Count > 0)
        {
            DeferredCall call = _deferredQueue.Dequeue();
            if (!call.Subscriber.IsDead)
                InvokeSubscriber(call.Subscriber, call.Context);
        }

        PruneDeadSubscribers();
    }

    static bool InvokeSubscriber(Subscriber sub, TEvent context)
    {
        try
        {
            return sub.TryInvoke(context);
        }
        catch (Exception ex)
        {
            LogSubscriberException(ex);
            return true;
        }
    }

    static void PruneDeadSubscribers()
    {
        for (int i = _subscribers.Count - 1; i >= 0; i--)
        {
            if (_subscribers[i].IsDead)
                _subscribers.RemoveAt(i);
        }
    }

    static bool IsDeadTarget(object target)
    {
        if (target == null)
            return true;

        if (target is UnityEngine.Object unityObject && unityObject == null)
            return true;

        return false;
    }

    static void LogSubscriberException(Exception ex)
    {
        if (ex is TargetInvocationException invocationException && invocationException.InnerException != null)
            ex = invocationException.InnerException;

        Debug.LogException(ex);
    }
}
