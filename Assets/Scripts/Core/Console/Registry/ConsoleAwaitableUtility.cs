using System;
using System.Reflection;
using System.Threading;
using UnityEngine;

public static class ConsoleAwaitableUtility
{
    public static async Awaitable<object> AwaitResultAsync(object awaitableObj, CancellationToken cancellation = default)
    {
        if (awaitableObj == null)
            return null;

        Type type = awaitableObj.GetType();
        MethodInfo getAwaiter = type.GetMethod("GetAwaiter", BindingFlags.Public | BindingFlags.Instance);
        if (getAwaiter == null)
            throw new InvalidOperationException($"{type.Name} has no GetAwaiter()");

        object awaiter = getAwaiter.Invoke(awaitableObj, null);
        Type awaiterType = awaiter.GetType();
        MethodInfo getResult = awaiterType.GetMethod("GetResult");
        MethodInfo unsafeOnCompleted = awaiterType.GetMethod("UnsafeOnCompleted")
                                      ?? awaiterType.GetMethod("OnCompleted");
        if (getResult == null || unsafeOnCompleted == null)
            throw new InvalidOperationException($"{awaiterType.Name} is not a valid awaiter");

        bool done = false;
        object result = null;
        Exception caught = null;
        Action continuation = () =>
        {
            try
            {
                result = getResult.Invoke(awaiter, null);
            }
            catch (TargetInvocationException tex)
            {
                caught = tex.InnerException ?? tex;
            }
            catch (Exception ex)
            {
                caught = ex;
            }
            done = true;
        };

        PropertyInfo isCompleted = awaiterType.GetProperty("IsCompleted", BindingFlags.Public | BindingFlags.Instance);
        bool alreadyCompleted = isCompleted != null
            && isCompleted.PropertyType == typeof(bool)
            && (bool)isCompleted.GetValue(awaiter);

        if (alreadyCompleted)
            continuation();
        else
            unsafeOnCompleted.Invoke(awaiter, new object[] { continuation });

        while (!done)
        {
            cancellation.ThrowIfCancellationRequested();
            await Awaitable.NextFrameAsync(cancellation);
        }

        if (caught != null)
            throw caught;

        return result;
    }
}
