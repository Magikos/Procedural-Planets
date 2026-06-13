using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// Weather observability owned by WeatherManager: the F9 JSON dump, the on-screen overlay, and the
// readback-driven aggregate stats. Reads simulation state live through the owner; the owner notifies
// it of evolution dispatches and feeds it query-cache face data. Holds no authoring config of its
// own — the serialized toggles live on the WeatherManager MonoBehaviour.
sealed class WeatherDiagnostics
{
    readonly WeatherManager _owner;

    int _evolutionDispatchCount;
    float _lastEvolutionDelta;
    float _lastEvolutionTime;
    bool _pending;
    bool _error;
    float _nextReadbackTime;
    int _nextFace;
    int _lastFace = -1;
    float _averageCondensation;
    float _averageStorm;
    float _averagePrecipitation;
    float _averageRainRate;
    float _averageMoistureSource;
    float _averageCondensationChange;
    float _maxCondensationChange;
    float _cloudyFraction;
    float _stormFraction;
    float _rainingFraction;
    float _condensingFraction;
    float _dryingFraction;
    int _samples;
    float _nextAggregateStatsTime;

    ILogger Logger => LoggerProvider.Get();

    public WeatherDiagnostics(WeatherManager owner)
    {
        _owner = owner;
    }

    public void RecordEvolutionDispatch(float interval)
    {
        _evolutionDispatchCount++;
        _lastEvolutionDelta = interval;
        _lastEvolutionTime = Time.time;
    }

    public void Tick()
    {
        UpdateReadback();
        UpdateAggregate();
    }

    void UpdateReadback()
    {
        if (_owner.EnableWeatherQueryCache)
            return;

        var grid = _owner.Grid;
        if (!_owner.ShowWeatherDiagnostics || grid == null || grid.Texture == null)
            return;

        if (_pending || Time.unscaledTime < _nextReadbackTime)
            return;

        int face = _nextFace;
        _nextFace = (_nextFace + 1) % 6;
        _lastFace = face;
        _pending = true;
        _error = false;
        _nextReadbackTime = Time.unscaledTime + Mathf.Max(_owner.WeatherDiagnosticsInterval, 0.25f);
        AsyncGPUReadback.Request(grid.Texture, 0,
            0, _owner.WeatherResolution,
            0, _owner.WeatherResolution,
            face, 1,
            TextureFormat.RGBAFloat,
            OnReadback);
    }

    void UpdateAggregate()
    {
        var grid = _owner.Grid;
        if (!_owner.ShowWeatherDiagnostics || grid == null || Time.unscaledTime < _nextAggregateStatsTime)
            return;

        var precipitationController = _owner.PrecipitationDebugControl;
        float rainThreshold = precipitationController != null
            ? precipitationController.StormThreshold
            : _owner.PrecipitationStormThreshold;
        var stats = grid.CalculateStats(_owner.CloudyThreshold, _owner.PrecipitationStormThreshold, rainThreshold);
        float invCount = stats.CellCount > 0 ? 1f / stats.CellCount : 0f;

        _averageCondensation = stats.AverageCondensation;
        _averageStorm = stats.AverageStorm;
        _averageMoistureSource = stats.AverageMoistureSource;
        _averageRainRate = stats.AverageRainRate;
        _cloudyFraction = stats.CloudyCellCount * invCount;
        _stormFraction = stats.StormCellCount * invCount;
        _rainingFraction = stats.RainingCellCount * invCount;
        _samples = stats.CellCount;
        _nextAggregateStatsTime = Time.unscaledTime + Mathf.Max(_owner.WeatherDiagnosticsInterval, 0.5f);
    }

    public void OnQueryCacheFaceData(int face, NativeArray<Color> data)
    {
        _lastFace = face;
        UpdateStats(data);
    }

    void OnReadback(AsyncGPUReadbackRequest request)
    {
        _pending = false;

        if (request.hasError)
        {
            _error = true;
            return;
        }

        UpdateStats(request.GetData<Color>());
    }

    void UpdateStats(NativeArray<Color> data)
    {
        int count = data.Length;
        if (count <= 0)
            return;

        double condensationSum = 0;
        double stormSum = 0;
        double precipitationSum = 0;
        double sourceSum = 0;
        double changeSum = 0;
        float maxChange = 0f;
        int condensing = 0;
        int drying = 0;

        for (int i = 0; i < count; i++)
        {
            Color pixel = data[i];
            float change = (pixel.a - 0.5f) / SphericalWeatherGrid.DeltaVisualizationScale;

            condensationSum += pixel.r;
            stormSum += pixel.g;
            precipitationSum += _owner.CalculatePrecipitation(pixel.g);
            sourceSum += pixel.b;
            changeSum += change;
            maxChange = Mathf.Max(maxChange, Mathf.Abs(change));

            if (change > 0.0001f)
                condensing++;
            else if (change < -0.0001f)
                drying++;
        }

        float invCount = 1f / count;
        _averageCondensation = (float)condensationSum * invCount;
        _averageStorm = (float)stormSum * invCount;
        _averagePrecipitation = (float)precipitationSum * invCount;
        _averageMoistureSource = (float)sourceSum * invCount;
        _averageCondensationChange = (float)changeSum * invCount;
        _maxCondensationChange = maxChange;
        _condensingFraction = condensing * invCount;
        _dryingFraction = drying * invCount;
        _samples = count;
    }

    public void Reset()
    {
        _evolutionDispatchCount = 0;
        _lastEvolutionDelta = 0f;
        _lastEvolutionTime = 0f;
        _pending = false;
        _error = false;
        _nextReadbackTime = 0f;
        _averageCondensation = 0f;
        _averageStorm = 0f;
        _averagePrecipitation = 0f;
        _averageRainRate = 0f;
        _averageMoistureSource = 0f;
        _averageCondensationChange = 0f;
        _maxCondensationChange = 0f;
        _cloudyFraction = 0f;
        _stormFraction = 0f;
        _rainingFraction = 0f;
        _condensingFraction = 0f;
        _dryingFraction = 0f;
        _samples = 0;
        _nextAggregateStatsTime = 0f;
        _nextFace = 0;
        _lastFace = -1;
    }

    public void OnDiagnosticsRequested()
    {
        if (!_owner.EnableWeatherDiagnosticHotkey)
            return;

        Dump("F9");
    }

    public string Dump(string reason)
    {
        var grid = _owner.Grid;
        if (grid == null)
        {
            const string noGrid = "[WeatherDiagnostics] No weather grid generated.";
            Logger.Log(LogLevel.Info, "Weather", noGrid);
            return noGrid;
        }

        var precipitationController = _owner.PrecipitationDebugControl;
        float rainThreshold = precipitationController != null
            ? precipitationController.StormThreshold
            : _owner.PrecipitationStormThreshold;
        var stats = grid.CalculateStats(_owner.CloudyThreshold, _owner.PrecipitationStormThreshold, rainThreshold);
        _owner.TryFindStrongestPrecipitation(out Vector3 strongestPosition, out WeatherSample strongestSample);

        bool precipitationRenderEnabled = precipitationController != null
            && precipitationController.PrecipitationRenderingEnabled
            && precipitationController.IsRenderingEnabled;
        string report = BuildJson(reason, stats, strongestPosition, strongestSample, precipitationController);
        string path = string.Empty;
        if (_owner.WriteWeatherDiagnosticsFile)
        {
            string fileName = $"weather-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            path = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllText(path, report);
        }

        float invCellCount = stats.CellCount > 0 ? 1f / stats.CellCount : 0f;
        int queryCacheFaces = _owner.QueryCacheFaceCount;
        string summary = $"[WeatherDiagnostics] cells={stats.CellCount}, " +
            $"avgCloud={stats.AverageCondensation:F3}, cloudy={stats.CloudyCellCount * invCellCount:P1}, " +
            $"avgPotential={stats.AverageMoistureSource:F3}, maxCloud={stats.MaxCondensation:F3}, " +
            $"avgStorm={stats.AverageStorm:F3}, storm={stats.StormCellCount * invCellCount:P1}, maxStorm={stats.MaxStorm:F3}, " +
            $"avgRain={stats.AverageRainRate:F3}, raining={stats.RainingCellCount * invCellCount:P1}, maxRain={stats.MaxRainRate:F3}, " +
            $"rainCandidates={stats.RainCandidateCellCount}, rainRender={(precipitationRenderEnabled ? "on" : "off")}, " +
            $"queryCacheFaces={queryCacheFaces}/6";
        if (queryCacheFaces < 6)
            summary += ", cache=warming";
        if (!string.IsNullOrEmpty(path))
            summary += $", file={path}";

        Logger.Log(LogLevel.Info, "Weather", summary);
        return report;
    }

    public void DrawOverlay()
    {
        if (!_owner.ShowWeatherDiagnostics)
            return;

        GUILayout.BeginArea(new Rect(10, 225, 430, 265), GUI.skin.box);
        GUILayout.Label("Weather Diagnostics");

        if (_owner.Grid == null)
        {
            GUILayout.Label("Grid: not generated");
            GUILayout.EndArea();
            return;
        }

        string readbackState = _error ? "readback error" :
            _pending ? "readback pending" : $"{_samples} cells";
        string lastUpdateAge = _evolutionDispatchCount > 0
            ? $"{Mathf.Max(0f, Time.time - _lastEvolutionTime):F2}s"
            : "none";

        GUILayout.Label($"Grid: {_owner.WeatherResolution} x {_owner.WeatherResolution} x 6 ({readbackState})");
        GUILayout.Label($"Query cache: {_owner.QueryCacheFaceCount}/6 faces, last face {(_owner.QueryCacheLastFace >= 0 ? _owner.QueryCacheLastFace.ToString() : "none")}");
        if (_owner.QueryCacheError)
            GUILayout.Label("Query cache readback error");
        GUILayout.Label($"Diagnostics face: {(_lastFace >= 0 ? _lastFace.ToString() : "none")}");
        GUILayout.Label($"Evolution: dispatches {_evolutionDispatchCount}, last dt {_lastEvolutionDelta:F2}s");
        GUILayout.Label($"Last update age: {lastUpdateAge}");
        GUILayout.Label($"Condensation avg: {_averageCondensation:F3}, storm avg: {_averageStorm:F3}");
        GUILayout.Label($"Cloudy/storm/raining: {_cloudyFraction:P1} / {_stormFraction:P1} / {_rainingFraction:P1}");
        GUILayout.Label($"Rain rate avg: {_averageRainRate:F3}");
        GUILayout.Label($"Moisture source avg: {_averageMoistureSource:F3}");
        GUILayout.Label($"Delta avg/max: {_averageCondensationChange:+0.0000;-0.0000;0.0000} / {_maxCondensationChange:F4}");
        GUILayout.Label($"Condensing: {_condensingFraction * 100f:F1}%, drying: {_dryingFraction * 100f:F1}%");
        GUILayout.Label("F9=Dump weather diagnostics");
        if (CloudDebugState.Mode == CloudDebugState.View.CondensationChange)
        {
            GUILayout.Label("Delta view: cyan condensing, red drying, dim below threshold");
            GUILayout.Label($"Threshold/saturation: {CloudDebugState.CondensationChangeThreshold:F4} / {CloudDebugState.CondensationChangeSaturation:F4}");
        }
        GUILayout.EndArea();
    }

    string BuildJson(
        string reason,
        WeatherGridStats stats,
        Vector3 strongestPosition,
        WeatherSample strongestSample,
        IPrecipitationDebugControl precipitationController)
    {
        var culture = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(2048);
        sb.AppendLine("{");
        AppendJsonString(sb, "reason", reason, 1, true);
        AppendJsonString(sb, "utc", DateTime.UtcNow.ToString("O", culture), 1, true);
        AppendJsonNumber(sb, "weatherResolution", _owner.WeatherResolution, 1, true);
        int queryCacheFaces = _owner.QueryCacheFaceCount;
        AppendJsonNumber(sb, "queryCacheFaces", queryCacheFaces, 1, true);
        AppendJsonBool(sb, "queryCacheComplete", queryCacheFaces >= 6, 1, true);
        AppendJsonNumber(sb, "evolutionDispatches", _evolutionDispatchCount, 1, true);
        AppendJsonNumber(sb, "lastEvolutionDelta", _lastEvolutionDelta, 1, true);
        AppendJsonBool(sb, "precipitationRenderEnabled",
            precipitationController != null && precipitationController.PrecipitationRenderingEnabled && precipitationController.IsRenderingEnabled,
            1, true);
        AppendJsonNumber(sb, "precipitationStormThreshold", precipitationController != null ? precipitationController.StormThreshold : _owner.PrecipitationStormThreshold, 1, true);

        Indent(sb, 1).AppendLine("\"gridStats\": {");
        float invCellCount = stats.CellCount > 0 ? 1f / stats.CellCount : 0f;
        AppendJsonNumber(sb, "cellCount", stats.CellCount, 2, true);
        AppendJsonNumber(sb, "cloudyCellCount", stats.CloudyCellCount, 2, true);
        AppendJsonNumber(sb, "stormCellCount", stats.StormCellCount, 2, true);
        AppendJsonNumber(sb, "rainCandidateCellCount", stats.RainCandidateCellCount, 2, true);
        AppendJsonNumber(sb, "rainingCellCount", stats.RainingCellCount, 2, true);
        AppendJsonNumber(sb, "cloudyFraction", stats.CloudyCellCount * invCellCount, 2, true);
        AppendJsonNumber(sb, "stormFraction", stats.StormCellCount * invCellCount, 2, true);
        AppendJsonNumber(sb, "rainCandidateFraction", stats.RainCandidateCellCount * invCellCount, 2, true);
        AppendJsonNumber(sb, "rainingFraction", stats.RainingCellCount * invCellCount, 2, true);
        AppendJsonNumber(sb, "averageCondensation", stats.AverageCondensation, 2, true);
        AppendJsonNumber(sb, "averageStorm", stats.AverageStorm, 2, true);
        AppendJsonNumber(sb, "averageMoistureSource", stats.AverageMoistureSource, 2, true);
        AppendJsonNumber(sb, "averageRainRate", stats.AverageRainRate, 2, true);
        AppendJsonNumber(sb, "maxCondensation", stats.MaxCondensation, 2, true);
        AppendJsonNumber(sb, "maxStorm", stats.MaxStorm, 2, true);
        AppendJsonNumber(sb, "maxMoistureSource", stats.MaxMoistureSource, 2, true);
        AppendJsonNumber(sb, "maxRainRate", stats.MaxRainRate, 2, false);
        Indent(sb, 1).AppendLine("},");

        Indent(sb, 1).AppendLine("\"strongestStorm\": {");
        AppendJsonVector(sb, "weatherDirection", stats.StrongestStormDirection, 2, true);
        AppendJsonVector(sb, "worldPosition", strongestPosition, 2, true);
        AppendJsonNumber(sb, "condensation", stats.StrongestStormCondensation, 2, true);
        AppendJsonNumber(sb, "storm", stats.StrongestStorm, 2, true);
        AppendJsonNumber(sb, "moistureSource", stats.StrongestStormMoistureSource, 2, true);
        AppendJsonNumber(sb, "samplePrecipitation", strongestSample.Precipitation, 2, false);
        Indent(sb, 1).AppendLine("}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static StringBuilder Indent(StringBuilder sb, int count)
    {
        for (int i = 0; i < count; i++)
            sb.Append("  ");
        return sb;
    }

    static void AppendJsonString(StringBuilder sb, string name, string value, int indent, bool comma)
    {
        Indent(sb, indent).Append('"').Append(name).Append("\": \"")
            .Append((value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\""))
            .Append('"').AppendLine(comma ? "," : string.Empty);
    }

    static void AppendJsonBool(StringBuilder sb, string name, bool value, int indent, bool comma)
    {
        Indent(sb, indent).Append('"').Append(name).Append("\": ")
            .Append(value ? "true" : "false").AppendLine(comma ? "," : string.Empty);
    }

    static void AppendJsonNumber(StringBuilder sb, string name, float value, int indent, bool comma)
    {
        Indent(sb, indent).Append('"').Append(name).Append("\": ")
            .Append(value.ToString("0.####", CultureInfo.InvariantCulture)).AppendLine(comma ? "," : string.Empty);
    }

    static void AppendJsonNumber(StringBuilder sb, string name, int value, int indent, bool comma)
    {
        Indent(sb, indent).Append('"').Append(name).Append("\": ")
            .Append(value.ToString(CultureInfo.InvariantCulture)).AppendLine(comma ? "," : string.Empty);
    }

    static void AppendJsonVector(StringBuilder sb, string name, Vector3 value, int indent, bool comma)
    {
        Indent(sb, indent).Append('"').Append(name).Append("\": { ")
            .Append("\"x\": ").Append(value.x.ToString("0.####", CultureInfo.InvariantCulture)).Append(", ")
            .Append("\"y\": ").Append(value.y.ToString("0.####", CultureInfo.InvariantCulture)).Append(", ")
            .Append("\"z\": ").Append(value.z.ToString("0.####", CultureInfo.InvariantCulture)).Append(" }")
            .AppendLine(comma ? "," : string.Empty);
    }
}
