using UnityEngine;

// Listener-wide filtering includes existing creature and thunder voices without rerouting their sources.
[DisallowMultipleComponent]
public sealed class WaterListenerFilter : MonoBehaviour
{
    AudioLowPassFilter _filter;
    bool _ownsFilter, _originalEnabled;
    float _originalCutoff;

    void OnEnable()
    {
        if (_filter == null)
        {
            _filter = GetComponent<AudioLowPassFilter>();
            _ownsFilter = _filter == null;
            if (_ownsFilter)
            {
                _filter = gameObject.AddComponent<AudioLowPassFilter>();
                _filter.enabled = false;
            }
        }
        _originalEnabled = _filter.enabled;
        _originalCutoff = _filter.cutoffFrequency;
    }

    public void SetImmersion(float immersion)
    {
        if (_filter == null || !float.IsFinite(immersion)) return;
        float wet = Mathf.Clamp01(immersion);
        _filter.cutoffFrequency = Mathf.Lerp(_originalCutoff, Mathf.Min(_originalCutoff, 850f), wet);
        _filter.enabled = _originalEnabled || wet > .001f;
    }

    void OnDisable()
    {
        if (_filter != null)
        {
            _filter.cutoffFrequency = _originalCutoff;
            _filter.enabled = _originalEnabled;
        }
    }

    void OnDestroy()
    {
        if (!_ownsFilter || _filter == null) return;
        if (Application.isPlaying) Destroy(_filter);
        else DestroyImmediate(_filter);
    }
}
