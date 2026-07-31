using System.Text;
using UnityEngine;

// Dev harness: fly Camera.main along a great circle at a fixed speed and log the scatter stream's
// load-lag (queued+in-flight pairs and resident instance count) plus frame timing, so "fly too fast and
// outrun the scatter" becomes a measurable, repeatable number instead of a feeling. Optionally captures
// evenly-spaced screenshots along the path for LOD-transition inspection.
//
// Not shipping gameplay: it drives the camera directly (disables FreeCameraController while running) and
// resolves the tile cache through the console registry. Usage from the debug console or a driver:
//   var b = FindOrAdd();  b.StartBench(speedMps, seconds);   // e.g. 120 m/s for 20 s
// Results land in the Unity log ("[FlyBench] ...") and in LastReport.
[DisallowMultipleComponent]
public sealed class ScatterFlyBench : MonoBehaviour
{
    [Header("Flight")]
    [Tooltip("Ground speed along the great circle, metres/second. Raise until the stream can't keep up.")]
    public float SpeedMps = 120f;
    public float DurationSec = 20f;
    [Tooltip("Load-lag / frame-time samples per second.")]
    public float SampleHz = 10f;
    [Tooltip("Pitch below the travel direction, degrees (so LOD transitions ahead are in frame).")]
    public float LookDownDeg = 12f;

    [Header("Capture")]
    public bool CaptureScreens = false;
    public int CaptureCount = 6;
    public string CaptureFolder = "Assets/Screenshots";

    public bool Running => _running;
    public string LastReport { get; private set; } = "";

    bool _running;
    float _t0, _tPrev, _nextSample, _sampleDt;
    int _shotsTaken, _shotEvery, _shotStride;
    Vector3 _axis, _dir;      // great-circle rotation axis + current surface direction (unit)
    float _radius;            // fixed camera radius (start altitude)
    double _angPerSec;        // radians/sec = speed / radius
    Behaviour _camController;

    ScatterTileCache _cache;
    readonly StringBuilder _csv = new();
    int _samples, _maxPending, _sumPending, _minLive, _maxLive;
    float _worstFrameMs;

    public void StartBench(float speedMps, float seconds)
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[FlyBench] no Camera.main"); return; }

        _cache = ConsoleRegistry.GetInstance(typeof(ScatterTileCache)) as ScatterTileCache;
        if (_cache == null) Debug.LogWarning("[FlyBench] ScatterTileCache not registered — load-lag will read 0 (generate a planet first)");

        SpeedMps = Mathf.Max(1f, speedMps);
        DurationSec = Mathf.Max(1f, seconds);

        Vector3 p = cam.transform.position;
        _radius = p.magnitude;
        _dir = p / _radius;                                   // planet-centre is world origin on this planet
        // Great-circle axis: perpendicular to the current up and the camera's forward-on-surface, so we
        // travel roughly where the camera already looks.
        Vector3 fwd = cam.transform.forward;
        Vector3 tangent = fwd - _dir * Vector3.Dot(fwd, _dir);
        if (tangent.sqrMagnitude < 1e-5f) tangent = Vector3.Cross(_dir, Vector3.up);
        tangent.Normalize();
        _axis = Vector3.Cross(_dir, tangent).normalized;      // rotating _dir about this moves along tangent
        _angPerSec = SpeedMps / _radius;

        // Freeze the free-cam so it doesn't fight us.
        _camController = cam.GetComponent("FreeCameraController") as Behaviour;
        if (_camController != null) _camController.enabled = false;

        _sampleDt = 1f / Mathf.Max(1f, SampleHz);
        _shotEvery = CaptureScreens ? Mathf.Max(1, CaptureCount) : 0;
        _shotStride = _shotEvery > 0 ? Mathf.Max(1, Mathf.RoundToInt(DurationSec / _sampleDt / _shotEvery)) : 0;
        _shotsTaken = 0;
        _samples = 0; _maxPending = 0; _sumPending = 0; _minLive = int.MaxValue; _maxLive = 0; _worstFrameMs = 0f;
        _csv.Clear();
        _csv.AppendLine("t_s,traveled_m,speed_mps,frame_ms,pending_pairs,live_instances,live_tiles");

        _t0 = Time.realtimeSinceStartup;
        _tPrev = _t0;
        _nextSample = 0f;
        _running = true;
        Debug.Log($"[FlyBench] start: {SpeedMps:F0} m/s for {DurationSec:F0}s (sample {SampleHz:F0} Hz) at radius {_radius:F0}");
    }

    public void StopBench() => Finish("stopped");

    void Update()
    {
        if (!_running) return;

        float now = Time.realtimeSinceStartup;
        float dt = now - _tPrev;
        _tPrev = now;
        float elapsed = now - _t0;

        // Advance along the great circle.
        double dAng = _angPerSec * dt;
        _dir = (Quaternion.AngleAxis((float)(dAng * Mathf.Rad2Deg), _axis) * _dir).normalized;
        var cam = Camera.main;
        if (cam == null) { Finish("camera lost"); return; }
        cam.transform.position = _dir * _radius;
        Vector3 travelTangent = Vector3.Cross(_axis, _dir).normalized;
        Vector3 fwd = (travelTangent * Mathf.Cos(LookDownDeg * Mathf.Deg2Rad)
                       - _dir * Mathf.Sin(LookDownDeg * Mathf.Deg2Rad)).normalized;
        cam.transform.rotation = Quaternion.LookRotation(fwd, _dir);

        if (elapsed >= _nextSample)
        {
            _nextSample += _sampleDt;
            int pending = _cache != null ? _cache.PendingPairCount : 0;
            int live = _cache != null ? _cache.LiveInstanceCount : 0;
            int tiles = _cache != null ? _cache.LiveTileCount : 0;
            float frameMs = dt * 1000f;
            _samples++;
            _maxPending = Mathf.Max(_maxPending, pending);
            _sumPending += pending;
            _minLive = Mathf.Min(_minLive, live);
            _maxLive = Mathf.Max(_maxLive, live);
            _worstFrameMs = Mathf.Max(_worstFrameMs, frameMs);
            _csv.AppendLine($"{elapsed:F2},{elapsed * SpeedMps:F0},{SpeedMps:F0},{frameMs:F1},{pending},{live},{tiles}");

            if (_shotStride > 0 && (_samples % _shotStride) == 0 && _shotsTaken < _shotEvery)
            {
                string path = $"{CaptureFolder}/flybench_{SpeedMps:F0}mps_{_shotsTaken:00}.png";
                ScreenCapture.CaptureScreenshot(path);
                _shotsTaken++;
            }
        }

        if (elapsed >= DurationSec) Finish("done");
    }

    void Finish(string why)
    {
        _running = false;
        if (_camController != null) _camController.enabled = true;
        float avgPending = _samples > 0 ? (float)_sumPending / _samples : 0f;
        // "Outrun" heuristic: the stream is chasing the camera whenever pending pairs stay above zero.
        string verdict = _maxPending == 0
            ? "stream kept up (no backlog)"
            : $"OUTRUN — backlog peaked at {_maxPending} pairs (avg {avgPending:F0}); raise stream throughput or lower speed";
        LastReport =
            $"[FlyBench] {why}: {SpeedMps:F0} m/s, {_samples} samples over {DurationSec:F0}s\n" +
            $"  load-lag: max {_maxPending} / avg {avgPending:F0} pending pairs; live instances {_minLive}..{_maxLive}\n" +
            $"  worst frame {_worstFrameMs:F1} ms\n" +
            $"  verdict: {verdict}";
        Debug.Log(LastReport);
        Debug.Log("[FlyBench] CSV\n" + _csv);
    }
}
