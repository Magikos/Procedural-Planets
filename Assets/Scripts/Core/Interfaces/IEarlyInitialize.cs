using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public interface IEarlyInitialize
{
    int EarlyPriority => 0;
    IReadOnlyList<Type> EarlyDependencies => Array.Empty<Type>();
    Awaitable EarlyInitialize(CancellationToken cancellationToken);
}
