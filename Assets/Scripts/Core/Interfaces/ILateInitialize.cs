using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public interface ILateInitialize
{
    int LatePriority => 0;
    IReadOnlyList<Type> LateDependencies => Array.Empty<Type>();
    Awaitable LateInitialize(CancellationToken cancellationToken);
}
