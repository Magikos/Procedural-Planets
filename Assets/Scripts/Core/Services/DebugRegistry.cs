using System.Collections.Generic;
using System.Text;

public readonly struct DebugModuleId : System.IEquatable<DebugModuleId>
{
    public readonly string Value;

    public DebugModuleId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    public bool IsValid => !string.IsNullOrEmpty(Value);

    public bool Equals(DebugModuleId other)
    {
        return string.Equals(Value, other.Value, System.StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is DebugModuleId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return System.StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
    }

    public override string ToString()
    {
        return IsValid ? Value : "none";
    }

    public static bool operator ==(DebugModuleId left, DebugModuleId right) => left.Equals(right);
    public static bool operator !=(DebugModuleId left, DebugModuleId right) => !left.Equals(right);
}

public readonly struct DebugModeId : System.IEquatable<DebugModeId>
{
    public readonly DebugModuleId Module;
    public readonly int LocalId;

    public DebugModeId(DebugModuleId module, int localId)
    {
        Module = module;
        LocalId = localId;
    }

    public bool IsValid => Module.IsValid;

    public bool Equals(DebugModeId other)
    {
        return Module == other.Module && LocalId == other.LocalId;
    }

    public override bool Equals(object obj)
    {
        return obj is DebugModeId other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Module.GetHashCode() * 397) ^ LocalId;
        }
    }

    public override string ToString()
    {
        return IsValid ? $"{Module}.{LocalId:00}" : "none";
    }

    public static bool operator ==(DebugModeId left, DebugModeId right) => left.Equals(right);
    public static bool operator !=(DebugModeId left, DebugModeId right) => !left.Equals(right);
}

public readonly struct DebugCaptureSetId : System.IEquatable<DebugCaptureSetId>
{
    public readonly DebugModuleId Module;
    public readonly string Name;

    public DebugCaptureSetId(DebugModuleId module, string name)
    {
        Module = module;
        Name = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
    }

    public bool IsValid => Module.IsValid && !string.IsNullOrEmpty(Name);

    public bool Equals(DebugCaptureSetId other)
    {
        return Module == other.Module
            && string.Equals(Name, other.Name, System.StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is DebugCaptureSetId other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Module.GetHashCode() * 397)
                ^ System.StringComparer.Ordinal.GetHashCode(Name ?? string.Empty);
        }
    }

    public override string ToString()
    {
        return IsValid ? $"{Module}.{Name}" : "none";
    }

    public static bool operator ==(DebugCaptureSetId left, DebugCaptureSetId right) => left.Equals(right);
    public static bool operator !=(DebugCaptureSetId left, DebugCaptureSetId right) => !left.Equals(right);
}

public static class DebugCoreIds
{
    public static readonly DebugModuleId Module = new DebugModuleId("debug");
    public static readonly DebugCaptureSetId CurrentModeOnly = new DebugCaptureSetId(Module, "current-mode");
    public static readonly DebugCaptureSetId FullLoop = new DebugCaptureSetId(Module, "full-loop");
}

public enum DebugCaptureSetBehavior
{
    ModeList,
    CurrentModeOnly,
    FullLoop
}

public readonly struct DebugModeDefinition
{
    public readonly DebugModeId Id;
    public readonly string Name;
    public readonly string Category;

    public DebugModeDefinition(DebugModeId id, string name, string category)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? $"Mode{id.LocalId}" : name;
        Category = string.IsNullOrWhiteSpace(category) ? "General" : category;
    }
}

public readonly struct DebugCaptureSetDefinition
{
    public readonly DebugCaptureSetId Id;
    public readonly string Name;
    public readonly DebugCaptureSetBehavior Behavior;
    public readonly DebugModeId[] ModeIds;

    public DebugCaptureSetDefinition(DebugCaptureSetId id, string name, DebugCaptureSetBehavior behavior, DebugModeId[] modeIds)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? id.ToString() : name;
        Behavior = behavior;
        ModeIds = modeIds ?? System.Array.Empty<DebugModeId>();
    }
}

public readonly struct DebugRuntimeState
{
    public readonly DebugRegistry Registry;
    public readonly ICameraRigContext CameraContext;
    public readonly ICelestialTimeController CelestialController;
    public readonly IPrecipitationDebugControl PrecipitationController;
    public readonly IWeatherProvider WeatherProvider;
    public readonly DebugModeId CurrentModeId;
    public readonly string CurrentModeName;
    public readonly DebugCaptureSetId CaptureSetId;
    public readonly string CaptureSetName;
    public readonly bool ShowDetailedDebug;
    public readonly bool IncludeHeavyDiagnostics;

    public DebugRuntimeState(
        DebugRegistry registry,
        ICameraRigContext cameraContext,
        ICelestialTimeController celestialController,
        IPrecipitationDebugControl precipitationController,
        IWeatherProvider weatherProvider,
        DebugModeId currentModeId,
        string currentModeName,
        DebugCaptureSetId captureSetId,
        string captureSetName,
        bool showDetailedDebug,
        bool includeHeavyDiagnostics)
    {
        Registry = registry;
        CameraContext = cameraContext;
        CelestialController = celestialController;
        PrecipitationController = precipitationController;
        WeatherProvider = weatherProvider;
        CurrentModeId = currentModeId;
        CurrentModeName = currentModeName;
        CaptureSetId = captureSetId;
        CaptureSetName = string.IsNullOrWhiteSpace(captureSetName) ? captureSetId.ToString() : captureSetName;
        ShowDetailedDebug = showDetailedDebug;
        IncludeHeavyDiagnostics = includeHeavyDiagnostics;
    }
}

public readonly struct DebugCaptureContext
{
    public readonly DebugRuntimeState Runtime;
    public readonly DebugModeId ModeId;
    public readonly string ModeName;
    public readonly int SourceWidth;
    public readonly int SourceHeight;
    public readonly int SavedWidth;
    public readonly int SavedHeight;
    public readonly string ImagePath;

    public DebugCaptureContext(
        DebugRuntimeState runtime,
        DebugModeId modeId,
        string modeName,
        int sourceWidth,
        int sourceHeight,
        int savedWidth,
        int savedHeight,
        string imagePath)
    {
        Runtime = runtime;
        ModeId = modeId;
        ModeName = modeName;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        SavedWidth = savedWidth;
        SavedHeight = savedHeight;
        ImagePath = imagePath;
    }
}

public interface IDebugModule
{
    DebugModuleId Id { get; }
    void Register(DebugRegistry registry);
}

public interface IDebugModeApplier
{
    DebugModuleId ModuleId { get; }
    void ApplyDebugMode(DebugModeDefinition mode);
    void ClearDebugMode();
}

public interface IDebugCaptureMetadataProvider
{
    void AppendMetadata(DebugCaptureContext context, StringBuilder sb);
}

public interface IDebugOverlayContributor
{
    void DrawOverlay(DebugRuntimeState state);
}

public interface IDebugDiagnosticProvider
{
    string Id { get; }
    bool IsEnabled { get; set; }
    void Refresh(DebugRuntimeState state);
    void AppendCachedResult(DebugCaptureContext context, StringBuilder sb);
}

public sealed class DebugRegistry
{
    readonly Dictionary<DebugModeId, DebugModeDefinition> _modes = new();
    readonly List<DebugModeId> _modeOrder = new();
    readonly Dictionary<DebugCaptureSetId, DebugCaptureSetDefinition> _captureSets = new();
    readonly List<DebugCaptureSetId> _captureSetOrder = new();
    readonly List<IDebugModeApplier> _modeAppliers = new();
    readonly List<IDebugCaptureMetadataProvider> _metadataProviders = new();
    readonly List<IDebugOverlayContributor> _overlayContributors = new();
    readonly List<IDebugDiagnosticProvider> _diagnostics = new();
    DebugCaptureSetId _defaultCaptureSetId;

    public IReadOnlyList<IDebugCaptureMetadataProvider> MetadataProviders => _metadataProviders;
    public IReadOnlyList<IDebugOverlayContributor> OverlayContributors => _overlayContributors;
    public IReadOnlyList<IDebugDiagnosticProvider> Diagnostics => _diagnostics;
    public DebugModeId DefaultModeId => _modeOrder.Count > 0 ? _modeOrder[0] : default;

    public void RegisterModule(IDebugModule module)
    {
        if (module == null)
            throw new System.ArgumentNullException(nameof(module));
        if (!module.Id.IsValid)
            throw new System.ArgumentException("Debug module id must be valid.", nameof(module));

        module.Register(this);
    }

    public void RegisterCoreCaptureSets()
    {
        RegisterCaptureSet(DebugCoreIds.CurrentModeOnly, "Current Mode Only", DebugCaptureSetBehavior.CurrentModeOnly);
        RegisterCaptureSet(DebugCoreIds.FullLoop, "Full Loop", DebugCaptureSetBehavior.FullLoop);
    }

    public DebugModeId RegisterMode(DebugModuleId moduleId, int localId, string name, string category)
    {
        DebugModeId id = new DebugModeId(moduleId, localId);
        RegisterMode(id, name, category);
        return id;
    }

    public void RegisterMode(DebugModeId id, string name, string category)
    {
        if (!id.IsValid)
            throw new System.ArgumentException("Debug mode id must be valid.", nameof(id));

        if (!_modes.ContainsKey(id))
            _modeOrder.Add(id);

        _modes[id] = new DebugModeDefinition(id, name, category);
    }

    public void RegisterCaptureSet(DebugCaptureSetId id, string name, params DebugModeId[] modeIds)
    {
        RegisterCaptureSet(id, name, DebugCaptureSetBehavior.ModeList, false, modeIds);
    }

    public void RegisterDefaultCaptureSet(DebugCaptureSetId id, string name, params DebugModeId[] modeIds)
    {
        RegisterCaptureSet(id, name, DebugCaptureSetBehavior.ModeList, true, modeIds);
    }

    public void RegisterCaptureSet(DebugCaptureSetId id, string name, DebugCaptureSetBehavior behavior, params DebugModeId[] modeIds)
    {
        RegisterCaptureSet(id, name, behavior, false, modeIds);
    }

    public void RegisterCaptureSet(DebugCaptureSetId id, string name, DebugCaptureSetBehavior behavior, bool isDefault, params DebugModeId[] modeIds)
    {
        if (!id.IsValid)
            throw new System.ArgumentException("Debug capture set id must be valid.", nameof(id));
        if (behavior == DebugCaptureSetBehavior.ModeList && (modeIds == null || modeIds.Length == 0))
            throw new System.ArgumentException("Mode-list capture sets must include at least one mode.", nameof(modeIds));

        if (!_captureSets.ContainsKey(id))
            _captureSetOrder.Add(id);

        _captureSets[id] = new DebugCaptureSetDefinition(id, name, behavior, modeIds);

        if (isDefault || !_defaultCaptureSetId.IsValid)
            _defaultCaptureSetId = id;
    }

    public void RegisterModeApplier(IDebugModeApplier applier)
    {
        if (applier == null)
            return;
        if (!applier.ModuleId.IsValid)
            throw new System.ArgumentException("Debug mode applier module id must be valid.", nameof(applier));
        if (!_modeAppliers.Contains(applier))
            _modeAppliers.Add(applier);
    }

    public void RegisterMetadataProvider(IDebugCaptureMetadataProvider provider)
    {
        if (provider != null && !_metadataProviders.Contains(provider))
            _metadataProviders.Add(provider);
    }

    public void RegisterOverlayContributor(IDebugOverlayContributor contributor)
    {
        if (contributor != null && !_overlayContributors.Contains(contributor))
            _overlayContributors.Add(contributor);
    }

    public void RegisterDiagnostic(IDebugDiagnosticProvider diagnostic)
    {
        if (diagnostic != null && !_diagnostics.Contains(diagnostic))
            _diagnostics.Add(diagnostic);
    }

    public DebugModeDefinition GetMode(DebugModeId id)
    {
        if (_modes.TryGetValue(id, out DebugModeDefinition mode))
            return mode;

        if (_modeOrder.Count > 0 && _modes.TryGetValue(_modeOrder[0], out DebugModeDefinition defaultMode))
            return defaultMode;

        return new DebugModeDefinition(default, "Off", "General");
    }

    public string GetModeName(DebugModeId id) => GetMode(id).Name;

    public DebugModeId GetNextModeId(DebugModeId currentModeId)
    {
        if (_modeOrder.Count == 0)
            return default;

        int index = _modeOrder.IndexOf(currentModeId);
        if (index < 0)
            return _modeOrder[0];

        return _modeOrder[(index + 1) % _modeOrder.Count];
    }

    public DebugModeId[] GetAllModeIds()
    {
        return _modeOrder.ToArray();
    }

    public int ResolveCaptureSetIndex(int requestedIndex)
    {
        if (_captureSetOrder.Count == 0)
            return -1;

        if (requestedIndex >= 0 && requestedIndex < _captureSetOrder.Count)
            return requestedIndex;

        int defaultIndex = GetCaptureSetIndex(_defaultCaptureSetId);
        return defaultIndex >= 0 ? defaultIndex : 0;
    }

    public int GetNextCaptureSetIndex(int currentIndex)
    {
        if (_captureSetOrder.Count == 0)
            return -1;

        int index = ResolveCaptureSetIndex(currentIndex);
        return (index + 1) % _captureSetOrder.Count;
    }

    public int GetCaptureSetIndex(DebugCaptureSetId id)
    {
        return _captureSetOrder.IndexOf(id);
    }

    public DebugCaptureSetDefinition GetCaptureSet(int index)
    {
        int resolvedIndex = ResolveCaptureSetIndex(index);
        if (resolvedIndex < 0)
            return default;

        return _captureSets[_captureSetOrder[resolvedIndex]];
    }

    public DebugModeId[] GetCaptureModeIds(DebugCaptureSetDefinition captureSet, DebugModeId currentModeId)
    {
        switch (captureSet.Behavior)
        {
            case DebugCaptureSetBehavior.CurrentModeOnly:
                return new[] { currentModeId };
            case DebugCaptureSetBehavior.FullLoop:
                return GetAllModeIds();
            default:
                return (DebugModeId[])captureSet.ModeIds.Clone();
        }
    }

    public void ApplyMode(DebugModeId modeId)
    {
        DebugModeDefinition mode = GetMode(modeId);
        for (int i = 0; i < _modeAppliers.Count; i++)
        {
            if (_modeAppliers[i].ModuleId == mode.Id.Module)
                _modeAppliers[i].ApplyDebugMode(mode);
            else
                _modeAppliers[i].ClearDebugMode();
        }
    }

    public void ClearModes()
    {
        for (int i = 0; i < _modeAppliers.Count; i++)
            _modeAppliers[i].ClearDebugMode();
    }
}
