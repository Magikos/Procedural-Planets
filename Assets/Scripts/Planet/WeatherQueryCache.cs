using UnityEngine;
using UnityEngine.Rendering;

sealed class WeatherQueryCache
{
    bool _pending;
    bool _error;
    float _nextTime;
    int _nextFace;
    int _lastFace = -1;
    int _faceMask;

    public bool Error => _error;
    public int LastFace => _lastFace;
    public int FaceCount
    {
        get
        {
            int count = 0;
            int mask = _faceMask;
            while (mask != 0) { count += mask & 1; mask >>= 1; }
            return count;
        }
    }

    public void Reset()
    {
        _pending = false;
        _error = false;
        _nextTime = 0f;
        _nextFace = 0;
        _lastFace = -1;
        _faceMask = 0;
    }

    public void Tick(SphericalWeatherGrid grid, bool enabled, float interval,
                     bool showDiagnostics, WeatherDiagnostics diagnostics)
    {
        if (!enabled || grid == null || grid.Texture == null)
            return;

        if (_pending || Time.unscaledTime < _nextTime)
            return;

        int face = _nextFace;
        _nextFace = (_nextFace + 1) % 6;
        _lastFace = face;
        _pending = true;
        _error = false;
        _nextTime = Time.unscaledTime + Mathf.Max(interval, 0.05f);
        int resolution = grid.Resolution;
        AsyncGPUReadback.Request(grid.Texture, 0,
            0, resolution,
            0, resolution,
            face, 1,
            TextureFormat.RGBAFloat,
            request => OnReadback(request, face, grid, showDiagnostics, diagnostics));
    }

    void OnReadback(AsyncGPUReadbackRequest request, int face, SphericalWeatherGrid grid,
                    bool showDiagnostics, WeatherDiagnostics diagnostics)
    {
        _pending = false;

        if (request.hasError)
        {
            _error = true;
            return;
        }

        var data = request.GetData<Color>();
        grid?.ApplyWeatherFaceReadback(face, data);
        if (grid != null && grid.DynamicsTexture != null)
        {
            AsyncGPUReadback.Request(grid.DynamicsTexture, 0,
                0, grid.Resolution,
                0, grid.Resolution,
                face, 1,
                TextureFormat.RGBAFloat,
                req => OnDynamicsReadback(req, face, grid));
        }
        _faceMask |= 1 << face;

        if (showDiagnostics)
            diagnostics.OnQueryCacheFaceData(face, data);
    }

    void OnDynamicsReadback(AsyncGPUReadbackRequest request, int face, SphericalWeatherGrid grid)
    {
        if (request.hasError)
            return;

        grid?.ApplyDynamicsFaceReadback(face, request.GetData<Color>());
    }
}
