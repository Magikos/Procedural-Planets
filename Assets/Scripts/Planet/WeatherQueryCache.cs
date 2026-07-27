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
    int _dynamicsFaceMask;
    int _epoch; // bumped on Reset; readbacks captured against an older epoch are ignored

    public bool Error => _error;
    public int LastFace => _lastFace;
    public int FaceCount
    {
        get
        {
            return CountBits(_faceMask);
        }
    }
    public int DynamicsFaceCount => CountBits(_dynamicsFaceMask);

    public void Reset()
    {
        _pending = false;
        _error = false;
        _nextTime = 0f;
        _nextFace = 0;
        _lastFace = -1;
        _faceMask = 0;
        _dynamicsFaceMask = 0;
        _epoch++;
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
        int epoch = _epoch;
        AsyncGPUReadback.Request(grid.Texture, 0,
            0, resolution,
            0, resolution,
            face, 1,
            TextureFormat.RGBAFloat,
            request => OnReadback(request, face, epoch, grid, showDiagnostics, diagnostics));
    }

    void OnReadback(AsyncGPUReadbackRequest request, int face, int epoch, SphericalWeatherGrid grid,
                    bool showDiagnostics, WeatherDiagnostics diagnostics)
    {
        // A reset (new weather grid) bumps the epoch; ignore readbacks issued against the old grid so
        // a stale callback can't clear _pending or mark a face cached on the replacement cache (F07).
        if (epoch != _epoch)
            return;

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
                req => OnDynamicsReadback(req, face, epoch, grid));
        }
        _faceMask |= 1 << face;

        if (showDiagnostics)
            diagnostics.OnQueryCacheFaceData(face, data);
    }

    void OnDynamicsReadback(AsyncGPUReadbackRequest request, int face, int epoch, SphericalWeatherGrid grid)
    {
        if (epoch != _epoch)
            return;
        if (request.hasError)
            return;

        grid?.ApplyDynamicsFaceReadback(face, request.GetData<Color>());
        _dynamicsFaceMask |= 1 << face;
    }

    static int CountBits(int mask)
    {
        int count = 0;
        while (mask != 0) { count += mask & 1; mask >>= 1; }
        return count;
    }
}
