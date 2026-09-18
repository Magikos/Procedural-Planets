using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Flags]
public enum SoundAuditionEnvironment { None = 0, Coast = 1, ForestDay = 2, ForestNight = 4, Wetland = 8 }

[Serializable]
public sealed class SoundAuditionCandidate
{
    public string Id;
    public string Title;
    public string Category;
    public AudioClip Clip;
    public bool Loop = true;
    public SoundAuditionEnvironment Environments;
    public string Source;
    public string SourceUrl;
    public string License;
    public string ListenFor;
}

/// <summary>Standalone listening review. It does not drive planet ambience.</summary>
public sealed class SoundAudition : MonoBehaviour
{
    public SoundAuditionCandidate[] Candidates = Array.Empty<SoundAuditionCandidate>();
    readonly List<AudioSource> _sources = new();
    readonly List<float> _sourceGains = new();
    readonly List<PlaybackProgress> _progress = new();
    readonly List<string> _categories = new() { "All" };
    SoundReviewStore _store;
    SoundReview _draft;
    int _selected = -1;
    int _category;
    Vector2 _listScroll, _detailScroll;
    float _masterVolume = .35f;
    float _saveAt;
    bool _dirty;
    string _error;
    string _playback = "Stopped";
    GUIStyle _wrapped;

    sealed class PlaybackProgress
    {
        public string Title;
        public int PreviousSample;
        public int Loops;
        public float LastLoopAt = float.NegativeInfinity;
        public bool Started;
        public bool Finished;

        public void Update(AudioSource source)
        {
            if (!source.isPlaying)
            {
                if (Started) Finished = true;
                return;
            }
            int sample = source.timeSamples;
            if (Started && source.loop && sample < PreviousSample)
            {
                Loops++;
                LastLoopAt = Time.unscaledTime;
            }
            Started = true;
            Finished = false;
            PreviousSample = sample;
        }
    }

    void OnEnable()
    {
        _categories.Clear();
        _categories.Add("All");
        _error = null;
        _store = null;
        try
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in Candidates)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.Id) || !ids.Add(candidate.Id)
                    || string.IsNullOrWhiteSpace(candidate.Category))
                    throw new InvalidDataException("The sound candidate catalog has a missing or duplicate ID/category.");
                if (!_categories.Contains(candidate.Category)) _categories.Add(candidate.Category);
            }
            _store = new SoundReviewStore(Path.Combine(Application.persistentDataPath, "SoundAudition", "reviews.json"));
            _category = 0;
            _selected = -1;
            _dirty = false;
            if (Candidates.Length > 0) Select(0);
        }
        catch (Exception exception)
        {
            _store = null;
            Report(exception);
        }
    }

    void Update()
    {
        if (_dirty && Time.unscaledTime >= _saveAt) SaveDraft();
        for (int i = 0; i < _sources.Count; i++) _progress[i].Update(_sources[i]);
    }

    void OnDisable()
    {
        SaveDraft();
        Stop();
    }

    void OnApplicationQuit() => SaveDraft();

    void Report(Exception exception)
    {
        _error = exception.Message;
        LoggerProvider.LogException("SoundAudition", exception);
    }

    bool SaveDraft()
    {
        if (!_dirty) return true;
        if (_store == null) return false;
        try
        {
            _store.Save(_draft);
            _dirty = false;
            _error = null;
            return true;
        }
        catch (Exception exception)
        {
            // Keep the draft and retry only after another edit or an explicit save.
            _saveAt = float.PositiveInfinity;
            Report(exception);
            return false;
        }
    }

    void Changed()
    {
        _dirty = true;
        _saveAt = Time.unscaledTime + .75f;
    }

    void Select(int index)
    {
        if (!SaveDraft()) return;
        Stop();
        _selected = index;
        _draft = _store.Get(Candidates[index].Id, Candidates[index].Loop);
        _detailScroll = Vector2.zero;
    }

    void Stop()
    {
        foreach (var source in _sources)
        {
            if (source == null) continue;
            source.Stop();
            Destroy(source);
        }
        _sources.Clear();
        _sourceGains.Clear();
        _progress.Clear();
        _playback = "Stopped";
    }

    void Play(SoundAuditionCandidate candidate, SoundReview review, float gain)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0;
        source.clip = candidate.Clip;
        source.loop = review.Loop;
        source.volume = gain * _masterVolume;
        _sources.Add(source);
        _sourceGains.Add(gain);
        _progress.Add(new PlaybackProgress { Title = candidate.Title });
        source.Play();
    }

    void PlaySolo()
    {
        Stop();
        var candidate = Candidates[_selected];
        if (candidate.Clip == null) return;
        Play(candidate, _draft, _draft.Volume);
        _playback = "Solo: " + candidate.Title;
    }

    public static bool CanMix(SoundAuditionCandidate candidate, SoundReview review, SoundAuditionEnvironment environment)
        => candidate.Clip != null && review.Decision == SoundReviewDecision.Keep
            && (candidate.Environments & environment) != 0;

    void PlayMix(SoundAuditionEnvironment environment)
    {
        if (!SaveDraft()) return;
        Stop();
        var categories = new HashSet<string>();
        var selected = new List<SoundAuditionCandidate>();
        float total = 0;
        foreach (var candidate in Candidates)
        {
            var review = _store.Get(candidate.Id, candidate.Loop);
            if (!CanMix(candidate, review, environment) || !categories.Add(candidate.Category)) continue;
            selected.Add(candidate);
            total += review.Volume;
        }
        var names = new List<string>();
        foreach (var candidate in selected)
        {
            var review = _store.Get(candidate.Id, candidate.Loop);
            Play(candidate, review, review.Volume / Mathf.Max(1, total));
            names.Add(candidate.Title);
        }
        _playback = names.Count == 0 ? "No kept recordings for this mix yet."
            : environment + ": " + string.Join(", ", names);
    }

    void OnGUI()
    {
        _wrapped ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
        GUILayout.BeginArea(new Rect(12, 12, Mathf.Max(300, Screen.width - 24), Mathf.Max(240, Screen.height - 24)), GUI.skin.box);
        GUILayout.Label("Environment sound audition");
        GUILayout.Label("Choose a recording, press Play, then Keep / Reject / Unsure. Notes and decisions save automatically.", _wrapped);
        if (!string.IsNullOrEmpty(_error)) GUILayout.Label("ERROR: " + _error, _wrapped);
        if (_store == null)
        {
            GUILayout.Label("Review is unavailable. Existing decisions will not be overwritten.", _wrapped);
            GUILayout.EndArea();
            return;
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Stop all", GUILayout.Width(100))) Stop();
        GUILayout.Label("Master volume " + Mathf.RoundToInt(_masterVolume * 100) + "%", GUILayout.Width(150));
        float master = GUILayout.HorizontalSlider(_masterVolume, 0, 1, GUILayout.Width(180));
        if (master != _masterVolume)
        {
            _masterVolume = master;
            for (int i = 0; i < _sources.Count; i++) _sources[i].volume = _sourceGains[i] * master;
        }
        GUILayout.EndHorizontal();
        GUILayout.Label(_playback, _wrapped);
        DrawProgress();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Kept mixes:", GUILayout.Width(90));
        if (GUILayout.Button("Coast")) PlayMix(SoundAuditionEnvironment.Coast);
        if (GUILayout.Button("Forest day")) PlayMix(SoundAuditionEnvironment.ForestDay);
        if (GUILayout.Button("Forest night")) PlayMix(SoundAuditionEnvironment.ForestNight);
        if (GUILayout.Button("Wetland")) PlayMix(SoundAuditionEnvironment.Wetland);
        GUILayout.EndHorizontal();
        _category = GUILayout.SelectionGrid(_category, _categories.ToArray(), Mathf.Max(2, Screen.width / 150));
        GUILayout.BeginHorizontal();
        _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Width(Mathf.Clamp(Screen.width * .34f, 180, 380)));
        for (int i = 0; i < Candidates.Length; i++)
        {
            var candidate = Candidates[i];
            if (_category != 0 && candidate.Category != _categories[_category]) continue;
            var review = i == _selected ? _draft : _store.Get(candidate.Id, candidate.Loop);
            string status = candidate.Clip == null ? "Missing" : review.Decision.ToString();
            if (GUILayout.Button((i == _selected ? "> " : "") + candidate.Title + " [" + status + "]", GUILayout.MinHeight(32))) Select(i);
        }
        GUILayout.EndScrollView();
        _detailScroll = GUILayout.BeginScrollView(_detailScroll);
        if (_selected >= 0) DrawSelection();
        GUILayout.EndScrollView();
        GUILayout.EndHorizontal();
        GUILayout.Label(_dirty ? "Unsaved changes. Use Save now to retry if saving failed." : "Saved locally: " + _store.FilePath, _wrapped);
        if (GUILayout.Button("Save now", GUILayout.Width(120))) SaveDraft();
        GUILayout.EndArea();
    }

    void DrawProgress()
    {
        for (int i = 0; i < _sources.Count; i++)
        {
            var source = _sources[i];
            var progress = _progress[i];
            float duration = source.clip.length;
            float elapsed = progress.Finished ? duration : (float)source.timeSamples / source.clip.frequency;
            bool justLooped = Time.unscaledTime - progress.LastLoopAt < 2f;
            string status = progress.Finished ? "Finished" : justLooped ? "LOOPED" : source.loop ? "Looping" : "Playing once";
            GUILayout.Label($"{progress.Title}  |  {elapsed:F1} / {duration:F1} s  |  Loops completed: {progress.Loops}  |  {status}");
            Rect bar = GUILayoutUtility.GetRect(1, 8, GUILayout.ExpandWidth(true));
            Color previous = GUI.color;
            GUI.color = new Color(.2f, .2f, .2f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            bar.width *= duration > 0 ? Mathf.Clamp01(elapsed / duration) : 0;
            GUI.color = justLooped ? new Color(1f, .75f, .2f) : new Color(.25f, .7f, .9f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }

    void DrawSelection()
    {
        var candidate = Candidates[_selected];
        GUILayout.Label(candidate.Title + " / " + candidate.Category);
        GUILayout.Label(candidate.ListenFor, _wrapped);
        GUILayout.Label("Source: " + candidate.Source, _wrapped);
        GUILayout.Label("License: " + candidate.License, _wrapped);
        if (!string.IsNullOrEmpty(candidate.SourceUrl) && GUILayout.Button("Open source page", GUILayout.Width(160)))
        {
            if (Uri.TryCreate(candidate.SourceUrl, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                Application.OpenURL(uri.AbsoluteUri);
        }
        bool available = candidate.Clip != null;
        if (available) GUILayout.Label($"{candidate.Clip.length:F1} seconds / {candidate.Clip.channels} channels / {candidate.Clip.frequency} Hz");
        else GUILayout.Label("Missing recording. Add notes about what to find.", _wrapped);
        GUILayout.BeginHorizontal();
        GUI.enabled = available;
        if (GUILayout.Button("Play from start")) PlaySolo();
        GUI.enabled = true;
        if (GUILayout.Button("Stop")) Stop();
        GUILayout.EndHorizontal();
        bool loop = GUILayout.Toggle(_draft.Loop, "Loop playback (check the seam)");
        GUILayout.Label("Recording volume: " + Mathf.RoundToInt(_draft.Volume * 100) + "%");
        float volume = GUILayout.HorizontalSlider(_draft.Volume, 0, 1);
        if (loop != _draft.Loop || volume != _draft.Volume)
        {
            _draft.Loop = loop;
            _draft.Volume = volume;
            Changed();
            if (_sources.Count == 1 && _sources[0].clip == candidate.Clip)
            {
                _sources[0].loop = loop;
                _sourceGains[0] = volume;
                _sources[0].volume = volume * _masterVolume;
            }
        }
        GUILayout.Label("Decision: " + _draft.Decision);
        GUILayout.BeginHorizontal();
        foreach (SoundReviewDecision decision in Enum.GetValues(typeof(SoundReviewDecision)))
        {
            GUI.enabled = available || decision != SoundReviewDecision.Keep;
            if (GUILayout.Button(decision.ToString()))
            {
                Stop();
                _draft.Decision = decision;
                Changed();
                SaveDraft();
            }
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Label("Notes: unwanted sounds, loop clicks, character, or replacement request");
        string notes = GUILayout.TextArea(_draft.Notes ?? "", 4000, GUILayout.MinHeight(100));
        if (notes != _draft.Notes) { _draft.Notes = notes; Changed(); }
    }
}
