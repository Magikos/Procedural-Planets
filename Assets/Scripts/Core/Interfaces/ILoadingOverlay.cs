using System;
using System.Threading;
using UnityEngine;

public interface ILoadingOverlay : IDisposable
{
    Awaitable FadeInAsync(CancellationToken ct);
    Awaitable FadeOutAsync(CancellationToken ct);
    void SetProgress(float value, string message);
    void SetAlpha(float alpha);
    void ShowFatalError(string message);
}
