using System.Threading;
using UnityEngine;

public interface IEarlyInitialize
{
    int EarlyPriority => 0;
    Awaitable EarlyInitialize(CancellationToken cancellationToken);
}
