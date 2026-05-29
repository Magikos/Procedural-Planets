using System.Threading;
using UnityEngine;

public interface ILoadingManager
{
    Awaitable<bool> TransitionToSceneAsync(string sceneName, bool useOverlay = true, CancellationToken cancellationToken = default);
    Awaitable<bool> TransitionToSceneAsync(int buildIndex, bool useOverlay = true, CancellationToken cancellationToken = default);
}
