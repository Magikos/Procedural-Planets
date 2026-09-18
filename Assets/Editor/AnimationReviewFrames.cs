using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Renders three comparison stages from two views into one timestamped sequence frame.</summary>
public sealed class AnimationReviewFrames : IDisposable
{
    readonly Camera _camera;
    readonly RenderTexture _target;
    readonly Texture2D _frame;
    readonly int _size;
    readonly SkinnedMeshRenderer[] _skins;
    readonly bool[] _recalculate;

    public AnimationReviewFrames(int size = 480)
    {
        if (size < 128 || size > 2048) throw new ArgumentOutOfRangeException(nameof(size));
        _size = size;
        _skins = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        _recalculate = new bool[_skins.Length];
        for (int i = 0; i < _skins.Length; i++)
        {
            _recalculate[i] = _skins[i].forceMatrixRecalculationPerRender;
            _skins[i].forceMatrixRecalculationPerRender = true;
        }
        var root = new GameObject("Animation review camera") { hideFlags = HideFlags.HideAndDontSave };
        _camera = root.AddComponent<Camera>();
        _camera.enabled = false;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(.13f, .16f, .2f);
        _camera.nearClipPlane = .05f;
        _camera.farClipPlane = 10f;
        _camera.orthographic = true;
        _camera.orthographicSize = 1.35f;
        var data = root.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        _target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        _target.Create();
        _camera.targetTexture = _target;
        _frame = new Texture2D(size * 3, size * 2, TextureFormat.RGB24, false);
    }

    // Stage centers must share the same pre-correction root trajectory. Camera following is recorded by the caller.
    public void Capture(string path, Vector3[] centers, Quaternion facing, float halfHeight = 1.35f)
    {
        if (centers == null || centers.Length != 3) throw new ArgumentException("Provide A, B, and C centers.", nameof(centers));
        if (!float.IsFinite(halfHeight) || halfHeight <= 0f) throw new ArgumentOutOfRangeException(nameof(halfHeight));
        _camera.orthographicSize = halfHeight;
        var previous = RenderTexture.active;
        try
        {
            for (int view = 0; view < 2; view++)
            for (int stage = 0; stage < 3; stage++)
            {
                Vector3 direction = facing * (view == 0 ? new Vector3(3f, 1f, -4f) : new Vector3(5f, .3f, 0f));
                _camera.transform.SetPositionAndRotation(centers[stage] + direction, Quaternion.LookRotation(-direction));
                _camera.Render();
                RenderTexture.active = _target;
                _frame.ReadPixels(new Rect(0, 0, _size, _size), stage * _size, (1 - view) * _size);
            }
            _frame.Apply();
            File.WriteAllBytes(path, _frame.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; }
    }

    public void Dispose()
    {
        for (int i = 0; i < _skins.Length; i++)
            if (_skins[i] != null) _skins[i].forceMatrixRecalculationPerRender = _recalculate[i];
        if (_camera != null) { _camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(_camera.gameObject); }
        if (_target != null) { _target.Release(); UnityEngine.Object.DestroyImmediate(_target); }
        if (_frame != null) UnityEngine.Object.DestroyImmediate(_frame);
    }
}
