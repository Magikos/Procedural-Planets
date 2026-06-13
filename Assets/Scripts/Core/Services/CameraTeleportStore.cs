using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

// The camera-pose operations the teleport store delegates back to the camera controller: capturing
// the live pose into a location, and applying a stored location to the camera. Extends
// ICameraRigContext so the store can read TargetCenter/PlanetRadius for relative-pose math.
public interface ICameraTeleportTarget : ICameraRigContext
{
    CameraTeleportLocation CaptureLocation(string name);
    bool TryApply(CameraTeleportLocation location, out string error);
}

// Owns the camera teleport registry: saved locations, built-ins, name lookup, JSON/PlayerPrefs
// persistence, and the F10-capture pose import. Hosts the camera.* teleport console commands as a
// registry target. The live camera pose is read/written through ICameraTeleportTarget; this store
// never touches the transform directly.
[CommandPrefix("camera")]
public sealed class CameraTeleportStore : ICameraTeleportRegistry, IDisposable
{
    const string TeleportPlayerPrefsKey = "CameraTeleportLocations.v1";
    const string LastDebugCaptureName = "LastDebugCapture";
    const string LastDebugPrintAlias = "LastDebugPrint";
    const int MaxSavedTeleports = 64;
    const float BuiltInTeleportPlanetRadius = 5293.44f;

    static readonly BuiltInTeleport[] BuiltInTeleports =
    {
        new(
            "Grass Face Seam A",
            new Vector3(3412.10f, -3462.78f, -1654.32f),
            new Vector3(-0.1544f, -0.1294f, 0.9795f)),
        new(
            "Grass Face Seam B",
            new Vector3(3451.27f, -3433.97f, -1646.30f),
            new Vector3(-0.2632f, 0.0824f, 0.9612f)),
        new(
            "Terrain Texture Oblique",
            new Vector3(3565.51f, -3467.42f, -1998.33f),
            new Vector3(-0.3836f, 0.2236f, 0.8960f)),
    };

    readonly ICameraTeleportTarget _target;
    readonly List<CameraTeleportLocation> _savedTeleports = new();
    readonly List<string> _teleportNameScratch = new();
    CameraTeleportLocation _lastDebugCapture;

    public CameraTeleportStore(ICameraTeleportTarget target)
    {
        _target = target;
        LoadTeleports();
        ConsoleRegistry.RegisterInstance(this);
        ServiceLocator.Register<ICameraTeleportRegistry>(this);
    }

    public void Dispose()
    {
        ServiceLocator.Unregister<ICameraTeleportRegistry>(this);
        ConsoleRegistry.UnregisterInstance(typeof(CameraTeleportStore));
    }

    [ConsoleCommand("teleport", "Teleport to LastDebugCapture or a saved camera location.", MonoTargetType.Registry)]
    string TeleportCmd([CompletionSource(typeof(CameraTeleportNamesProvider))] string name)
    {
        if (!TryFindTeleport(name, out CameraTeleportLocation location))
            return $"unknown camera teleport: '{name}'";

        if (!_target.TryApply(location, out string error))
            return error;

        return $"camera teleported: {location.Name}";
    }

    [ConsoleCommand("save-teleport", "Save or overwrite the current camera position and rotation under a name.", MonoTargetType.Registry)]
    string SaveTeleportCmd(string name)
    {
        name = NormalizeTeleportName(name);
        if (string.IsNullOrEmpty(name))
            return "camera teleport name cannot be empty";
        if (IsReservedTeleportName(name))
            return $"'{name}' is reserved";

        CameraTeleportLocation location = _target.CaptureLocation(name);
        int existing = FindSavedTeleportIndex(name);
        bool overwroteExisting = existing >= 0 || TryFindBuiltInTeleport(name, out _);
        if (existing >= 0)
            _savedTeleports[existing] = location;
        else
        {
            if (_savedTeleports.Count >= MaxSavedTeleports)
                _savedTeleports.RemoveAt(0);
            _savedTeleports.Add(location);
        }

        SaveTeleports();
        return $"camera teleport {(overwroteExisting ? "updated" : "saved")}: {name}";
    }

    [ConsoleCommand("remove-teleport", "Remove a saved camera location.", MonoTargetType.Registry)]
    string RemoveTeleportCmd([CompletionSource(typeof(CameraTeleportNamesProvider))] string name)
    {
        name = NormalizeTeleportName(name);
        if (IsReservedTeleportName(name))
            return $"'{name}' is reserved and cannot be removed";

        int index = FindSavedTeleportIndex(name);
        if (index < 0)
        {
            if (TryFindBuiltInTeleport(name, out _))
                return $"'{name}' is built in; save the name to override it";
            return $"unknown camera teleport: '{name}'";
        }

        string removed = _savedTeleports[index].Name;
        _savedTeleports.RemoveAt(index);
        SaveTeleports();
        return $"camera teleport removed: {removed}";
    }

    [ConsoleCommand("teleports", "List reserved, saved, and built-in camera locations.", MonoTargetType.Registry)]
    string TeleportsCmd()
    {
        IReadOnlyList<string> names = GetTeleportNames();
        return names.Count == 0
            ? "camera teleports: none"
            : $"camera teleports ({names.Count}):\n- {string.Join("\n- ", names)}";
    }

    public IReadOnlyList<string> GetTeleportNames()
    {
        EnsureLastDebugCaptureImported();
        _teleportNameScratch.Clear();
        _teleportNameScratch.Add(LastDebugCaptureName);
        _teleportNameScratch.Add(LastDebugPrintAlias);
        for (int i = 0; i < _savedTeleports.Count; i++)
            _teleportNameScratch.Add(_savedTeleports[i].Name);
        for (int i = 0; i < BuiltInTeleports.Length; i++)
        {
            if (FindSavedTeleportIndex(BuiltInTeleports[i].Name) < 0)
                _teleportNameScratch.Add(BuiltInTeleports[i].Name);
        }
        return _teleportNameScratch;
    }

    public void RecordLastDebugCapture()
    {
        _lastDebugCapture = _target.CaptureLocation(LastDebugCaptureName);
        SaveTeleports();
    }

    bool TryFindTeleport(string name, out CameraTeleportLocation location)
    {
        location = null;
        name = NormalizeTeleportName(name);
        if (string.IsNullOrEmpty(name))
            return false;

        if (IsReservedTeleportName(name))
        {
            EnsureLastDebugCaptureImported();
            location = _lastDebugCapture;
            return location != null;
        }

        int index = FindSavedTeleportIndex(name);
        if (index >= 0)
        {
            location = _savedTeleports[index];
            return true;
        }

        return TryFindBuiltInTeleport(name, out location);
    }

    static bool TryFindBuiltInTeleport(string name, out CameraTeleportLocation location)
    {
        for (int i = 0; i < BuiltInTeleports.Length; i++)
        {
            BuiltInTeleport builtIn = BuiltInTeleports[i];
            if (!string.Equals(builtIn.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            Vector3 forward = builtIn.LocalForward.normalized;
            Vector3 up = Vector3.ProjectOnPlane(builtIn.LocalPosition.normalized, forward);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.right, forward);

            location = new CameraTeleportLocation
            {
                Name = builtIn.Name,
                Position = builtIn.LocalPosition,
                Rotation = Quaternion.LookRotation(forward, up.normalized),
                RelativeToTarget = true,
                SurfaceView = true,
                PlanetRadius = BuiltInTeleportPlanetRadius,
            };
            return true;
        }

        location = null;
        return false;
    }

    int FindSavedTeleportIndex(string name)
    {
        for (int i = 0; i < _savedTeleports.Count; i++)
        {
            if (string.Equals(_savedTeleports[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    static string NormalizeTeleportName(string name)
    {
        return string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
    }

    static bool IsReservedTeleportName(string name)
    {
        return string.Equals(name, LastDebugCaptureName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, LastDebugPrintAlias, StringComparison.OrdinalIgnoreCase);
    }

    void LoadTeleports()
    {
        string json = PlayerPrefs.GetString(TeleportPlayerPrefsKey, "");
        if (string.IsNullOrEmpty(json))
            return;

        try
        {
            CameraTeleportData data = JsonUtility.FromJson<CameraTeleportData>(json);
            _savedTeleports.Clear();
            if (data?.Locations != null)
            {
                for (int i = 0; i < data.Locations.Count && _savedTeleports.Count < MaxSavedTeleports; i++)
                {
                    CameraTeleportLocation location = data.Locations[i];
                    if (location != null && !string.IsNullOrWhiteSpace(location.Name)
                        && !IsReservedTeleportName(location.Name))
                    {
                        _savedTeleports.Add(location);
                    }
                }
            }
            _lastDebugCapture = data?.LastDebugCapture;
        }
        catch (Exception ex)
        {
            LoggerProvider.Log(LogLevel.Warning, "CameraTeleport", $"Load failed: {ex.Message}");
        }
    }

    void SaveTeleports()
    {
        try
        {
            var data = new CameraTeleportData
            {
                Locations = new List<CameraTeleportLocation>(_savedTeleports),
                LastDebugCapture = _lastDebugCapture,
            };
            PlayerPrefs.SetString(TeleportPlayerPrefsKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            LoggerProvider.Log(LogLevel.Warning, "CameraTeleport", $"Save failed: {ex.Message}");
        }
    }

    void EnsureLastDebugCaptureImported()
    {
        if (_lastDebugCapture != null)
            return;

        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return;
            string directory = Path.Combine(projectRoot, "local-only", "debug-screenshots");
            if (!Directory.Exists(directory))
                return;

            FileInfo newest = new DirectoryInfo(directory)
                .EnumerateFiles("F10-*.txt", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest == null)
                return;

            Vector3 position = default;
            Vector3 forward = default;
            bool surfaceView = false;
            bool hasPosition = false;
            bool hasForward = false;
            foreach (string line in File.ReadLines(newest.FullName))
            {
                if (line.StartsWith("Position:", StringComparison.Ordinal))
                    hasPosition = TryParseCaptureVector(line.Substring("Position:".Length), out position);
                else if (line.StartsWith("Forward:", StringComparison.Ordinal))
                    hasForward = TryParseCaptureVector(line.Substring("Forward:".Length), out forward);
                else if (line.StartsWith("Surface view:", StringComparison.Ordinal))
                    bool.TryParse(line.Substring("Surface view:".Length).Trim(), out surfaceView);
            }

            if (!hasPosition || !hasForward || forward.sqrMagnitude < 0.0001f)
                return;

            Transform targetCenter = _target.TargetCenter;
            Vector3 center = targetCenter != null ? targetCenter.position : Vector3.zero;
            Vector3 up = Vector3.ProjectOnPlane((position - center).normalized, forward.normalized);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.up, forward.normalized);
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.ProjectOnPlane(Vector3.right, forward.normalized);

            Quaternion worldRotation = Quaternion.LookRotation(forward.normalized, up.normalized);
            bool relative = targetCenter != null;
            _lastDebugCapture = new CameraTeleportLocation
            {
                Name = LastDebugCaptureName,
                Position = relative ? targetCenter.InverseTransformPoint(position) : position,
                Rotation = relative ? Quaternion.Inverse(targetCenter.rotation) * worldRotation : worldRotation,
                RelativeToTarget = relative,
                SurfaceView = surfaceView,
                PlanetRadius = _target.PlanetRadius,
            };
            SaveTeleports();
        }
        catch (Exception ex)
        {
            LoggerProvider.Log(LogLevel.Warning, "CameraTeleport", $"Could not import latest F10 pose: {ex.Message}");
        }
    }

    static bool TryParseCaptureVector(string text, out Vector3 value)
    {
        value = default;
        string[] parts = text.Split(',');
        if (parts.Length != 3)
            return false;

        NumberStyles style = NumberStyles.Float;
        CultureInfo culture = CultureInfo.InvariantCulture;
        if (!float.TryParse(parts[0].Trim(), style, culture, out float x)
            || !float.TryParse(parts[1].Trim(), style, culture, out float y)
            || !float.TryParse(parts[2].Trim(), style, culture, out float z))
        {
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }

    sealed class BuiltInTeleport
    {
        public readonly string Name;
        public readonly Vector3 LocalPosition;
        public readonly Vector3 LocalForward;

        public BuiltInTeleport(string name, Vector3 localPosition, Vector3 localForward)
        {
            Name = name;
            LocalPosition = localPosition;
            LocalForward = localForward;
        }
    }

    [Serializable]
    sealed class CameraTeleportData
    {
        public List<CameraTeleportLocation> Locations;
        public CameraTeleportLocation LastDebugCapture;
    }
}

[Serializable]
public sealed class CameraTeleportLocation
{
    public string Name;
    public Vector3 Position;
    public Quaternion Rotation;
    public bool RelativeToTarget;
    public bool SurfaceView;
    public float PlanetRadius;
}
