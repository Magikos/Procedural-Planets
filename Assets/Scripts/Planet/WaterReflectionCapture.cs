using System;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class WaterReflectionCapture : IDisposable
{
    static readonly int CubeId = Shader.PropertyToID(ShaderGlobalIds.WaterReflectionCube);
    static readonly int PreviousCubeId = Shader.PropertyToID(ShaderGlobalIds.WaterReflectionPreviousCube);
    static readonly int BlendId = Shader.PropertyToID(ShaderGlobalIds.WaterReflectionBlend);
    static readonly int OriginId = Shader.PropertyToID(ShaderGlobalIds.WaterReflectionOrigin);
    ReflectionProbe _probe;
    RenderTexture _front, _back, _previous;
    int _renderId = -1;
    float _nextCapture;
    float _blendStarted;
    Vector3 _captureOrigin, _publishedOrigin;
    bool _valid;
    public int CompletedCaptures { get; private set; }
    public int Resolution => _front != null ? _front.width : 0;

    public void Tick(WaterSample surface, Vector3 cameraPosition, WaterQualityProfile quality, float time)
    {
        if (quality.ProbeResolution <= 0 || Mathf.Abs(surface.SignedDepth) > 100f)
        { Dispose(); return; }
        EnsureResources(quality.ProbeResolution);
        if (_renderId >= 0 && _probe.IsFinishedRendering(_renderId))
        {
            (_previous, _front, _back) = (_front, _back, _previous);
            _front.GenerateMips();
            _publishedOrigin = _captureOrigin;
            _blendStarted = _valid ? time : time - .35f;
            _valid = true;
            _renderId = -1;
            CompletedCaptures++;
            Shader.SetGlobalTexture(CubeId, _front);
            Shader.SetGlobalTexture(PreviousCubeId, _previous);
        }
        Shader.SetGlobalFloat(BlendId, WaterMotion.Smooth(0f, .35f, time - _blendStarted));
        float distanceFade = _valid ? 1f - WaterMotion.Smooth(35f, 100f, Vector3.Distance(cameraPosition, _publishedOrigin)) : 0f;
        Shader.SetGlobalVector(OriginId, new Vector4(_publishedOrigin.x, _publishedOrigin.y, _publishedOrigin.z, distanceFade));
        if (_renderId >= 0 || time < _nextCapture) return;
        _captureOrigin = surface.SurfacePoint + surface.Normal * 3f;
        _probe.transform.position = _captureOrigin;
        _renderId = _probe.RenderProbe(_back);
        _nextCapture = time + quality.ProbeInterval;
    }

    void EnsureResources(int resolution)
    {
        if (_probe != null && Resolution == resolution) return;
        Dispose();
        var host = new GameObject("Water reflection capture") { hideFlags = HideFlags.HideAndDontSave };
        _probe = host.AddComponent<ReflectionProbe>();
        _probe.mode = ReflectionProbeMode.Realtime;
        _probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        _probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
        _probe.resolution = resolution;
        _probe.hdr = true;
        _probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
        _probe.backgroundColor = Color.clear;
        _probe.nearClipPlane = .2f;
        _probe.farClipPlane = 350f;
        _probe.size = Vector3.one; // This probe feeds water explicitly, not unrelated materials.
        _probe.cullingMask = ~(1 << LayerMask.NameToLayer("Water"));
        _front = CreateCube(resolution, "Water reflection front");
        _back = CreateCube(resolution, "Water reflection back");
        _previous = CreateCube(resolution, "Water reflection previous");
        _nextCapture = 0f;
    }

    static RenderTexture CreateCube(int size, string name)
    {
        var cube = new RenderTexture(size, size, 16, RenderTextureFormat.ARGBHalf)
        {
            name = name, dimension = TextureDimension.Cube, useMipMap = true,
            autoGenerateMips = false, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.HideAndDontSave
        };
        cube.Create();
        return cube;
    }

    public void Dispose()
    {
        Shader.SetGlobalVector(OriginId, Vector4.zero);
        Shader.SetGlobalTexture(CubeId, null);
        Shader.SetGlobalTexture(PreviousCubeId, null);
        Shader.SetGlobalFloat(BlendId, 1f);
        if (_probe != null) UnityEngine.Object.Destroy(_probe.gameObject);
        _probe = null;
        if (_front != null) { _front.Release(); UnityEngine.Object.Destroy(_front); }
        if (_back != null) { _back.Release(); UnityEngine.Object.Destroy(_back); }
        if (_previous != null) { _previous.Release(); UnityEngine.Object.Destroy(_previous); }
        _front = _back = _previous = null;
        _renderId = -1;
        _valid = false;
    }
}
