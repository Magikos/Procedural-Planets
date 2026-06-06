using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cycle-recall buffer for the console input line, persisted to <see cref="PlayerPrefs"/>.
///
/// Cursor semantics:
///   -1 = live input (not navigating history)
///    0 = newest entry
///    Count - 1 = oldest entry
/// </summary>
public sealed class ConsoleHistory
{
    const int Capacity = 100;
    const string PlayerPrefsKey = "ConsoleHistory";

    readonly List<string> _entries = new();
    int _cursor = -1;

    public int Count => _entries.Count;
    public int Cursor => _cursor;
    public bool IsNavigating => _cursor >= 0;

    public void Add(string line)
    {
        _cursor = -1;
        if (string.IsNullOrWhiteSpace(line)) return;

        // Dedupe: don't add if same as most recent.
        if (_entries.Count > 0 && _entries[_entries.Count - 1] == line)
            return;

        _entries.Add(line);
        while (_entries.Count > Capacity)
            _entries.RemoveAt(0);
    }

    /// <summary>Move toward older entries. Returns the entry to display, or null if none.</summary>
    public string Previous()
    {
        if (_entries.Count == 0) return null;
        int next = _cursor < 0 ? 0 : _cursor + 1;
        if (next >= _entries.Count) next = _entries.Count - 1;
        _cursor = next;
        return _entries[_entries.Count - 1 - _cursor];
    }

    /// <summary>
    /// Move toward newer entries / live input. Returns the entry to display, or
    /// empty string when leaving history back to live input, or null if not navigating.
    /// </summary>
    public string Next()
    {
        if (_cursor < 0) return null;
        _cursor--;
        if (_cursor < 0) return "";
        return _entries[_entries.Count - 1 - _cursor];
    }

    public void ResetCursor() => _cursor = -1;

    public void Save()
    {
        try
        {
            var data = new SerializableHistory { entries = _entries.ToArray() };
            PlayerPrefs.SetString(PlayerPrefsKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            LoggerProvider.Get().Log(LogLevel.Warning, "ConsoleHistory", $"Save failed: {ex.Message}");
        }
    }

    public void Load()
    {
        string json = PlayerPrefs.GetString(PlayerPrefsKey, "");
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            var data = JsonUtility.FromJson<SerializableHistory>(json);
            if (data?.entries == null) return;
            _entries.Clear();
            _entries.AddRange(data.entries);
            while (_entries.Count > Capacity)
                _entries.RemoveAt(0);
        }
        catch (Exception ex)
        {
            LoggerProvider.Get().Log(LogLevel.Warning, "ConsoleHistory", $"Load failed: {ex.Message}");
        }
    }

    [Serializable]
    class SerializableHistory
    {
        public string[] entries;
    }
}
