using System.Threading;
using UnityEngine;

public interface ILateInitialize
{
    int LatePriority => 0;
    Awaitable LateInitialize(CancellationToken cancellationToken);
}
