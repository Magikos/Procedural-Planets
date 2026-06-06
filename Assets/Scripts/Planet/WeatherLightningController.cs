using UnityEngine;

/// <summary>
/// Drives short lightning pulses in active storm cells. Rendering stays in the
/// cloud and precipitation shaders through global lightning parameters.
/// </summary>
[CommandPrefix("lightning")]
public class WeatherLightningController : MonoBehaviour
{
    [Header("Timing")]
    public bool EnableLightning = true;
    [Range(0.5f, 30f)] public float MinDelay = 4f;
    [Range(0.5f, 45f)] public float MaxDelay = 12f;
    [Range(0.05f, 2f)] public float FlashDuration = 0.55f;
    [Range(0f, 1f)] public float SecondaryPulseChance = 0.65f;
    [Range(0.03f, 0.5f)] public float SecondaryPulseDelay = 0.16f;

    [Header("Storm Selection")]
    [Range(0f, 1f)] public float MinimumPrecipitation = 0.35f;
    [Range(0f, 1f)] public float MinimumStormIntensity = 0.6f;
    [Range(1f, 45f)] public float FlashAngularRadius = 6f;
    [Range(0f, 25f)] public float StrikeScatterAngle = 7f;
    [Range(1, 4)] public int PathCellCount = 3;
    [Range(0f, 20f)] public float PathStepAngle = 5f;
    [Range(0.02f, 0.35f)] public float PathCellDelay = 0.075f;

    [Header("Lighting")]
    [Range(0f, 8f)] public float CloudFlashIntensity = 2.8f;
    [Range(0f, 2f)] public float RainFlashIntensity = 0.55f;
    public Color LightningColor = new Color(0.72f, 0.84f, 1f, 1f);

    IWeatherProvider _weather;
    Vector3 _planetCenter;
    bool _hasPlanet;
    Vector3 _strikeDirection = Vector3.up;
    readonly Vector3[] _pathDirections = { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
    readonly float[] _pathWeights = { 1f, 0.75f, 0.55f, 0.38f };
    float _strikeStartTime = -100f;
    float _strikeDuration = 0.5f;
    float _strikePower;
    float _secondaryPower;
    float _nextStrikeTime;

    static readonly int _weatherLightningParamsId = Shader.PropertyToID("_WeatherLightningParams");
    static readonly int _weatherLightningColorId = Shader.PropertyToID("_WeatherLightningColor");
    static readonly int[] _weatherLightningCellIds =
    {
        Shader.PropertyToID("_WeatherLightningCell0"),
        Shader.PropertyToID("_WeatherLightningCell1"),
        Shader.PropertyToID("_WeatherLightningCell2"),
        Shader.PropertyToID("_WeatherLightningCell3")
    };

    void OnEnable()
    {
        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
        ScheduleNextStrike(1f, 3f);
        ClearLightning();
    }

    void OnDisable()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
        ClearLightning();
    }

    void Update()
    {
        if (!EnableLightning)
        {
            ClearLightning();
            return;
        }

        ResolveWeatherProvider();

        if (Time.time >= _nextStrikeTime)
            TryStartStrike();

        UploadLightning();
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetCenter = evt.PlanetCenter;
        _hasPlanet = true;
    }

    void ResolveWeatherProvider()
    {
        if (_weather != null)
            return;

        ServiceLocator.TryGet(out _weather);
    }

    void TryStartStrike()
    {
        if (!_hasPlanet || _weather == null ||
            !_weather.TryFindStrongestPrecipitation(out Vector3 stormPosition, out WeatherSample sample) ||
            sample.Precipitation < MinimumPrecipitation ||
            sample.StormIntensity < MinimumStormIntensity)
        {
            ScheduleNextStrike(1f, 2.5f);
            return;
        }

        Vector3 stormDirection = stormPosition - _planetCenter;
        if (stormDirection.sqrMagnitude <= 0.0001f)
        {
            ScheduleNextStrike(1f, 2.5f);
            return;
        }

        _strikeDirection = ScatterDirection(stormDirection.normalized);
        BuildLightningPath(_strikeDirection);
        _strikeStartTime = Time.time;
        _strikeDuration = Mathf.Max(FlashDuration, 0.05f);
        _strikePower = CloudFlashIntensity * Mathf.Lerp(0.75f, 1.25f, Random.value);
        _secondaryPower = Random.value <= SecondaryPulseChance
            ? _strikePower * Mathf.Lerp(0.25f, 0.55f, Random.value)
            : 0f;
        float strikeRadius = (stormPosition - _planetCenter).magnitude;
        Vector3 strikePosition = _planetCenter + _strikeDirection * strikeRadius;
        EventBus<WeatherLightningEvent>.Raise(new WeatherLightningEvent(
            strikePosition,
            _strikeDirection,
            _strikePower,
            sample.StormIntensity,
            sample.Precipitation,
            _strikeDuration,
            isGroundStrike: false));
        ScheduleNextStrike(_strikeDuration + MinDelay, _strikeDuration + Mathf.Max(MaxDelay, MinDelay));
    }

    Vector3 ScatterDirection(Vector3 direction)
    {
        float scatterRadians = StrikeScatterAngle * Mathf.Deg2Rad * Mathf.Sqrt(Random.value);
        if (scatterRadians <= 0.0001f)
            return direction;

        Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.92f
            ? Vector3.forward
            : Vector3.up;
        Vector3 tangent = Vector3.Cross(reference, direction).normalized;
        Vector3 bitangent = Vector3.Cross(direction, tangent).normalized;
        float angle = Random.value * Mathf.PI * 2f;
        Vector3 offset = tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
        return (direction * Mathf.Cos(scatterRadians) + offset * Mathf.Sin(scatterRadians)).normalized;
    }

    void BuildLightningPath(Vector3 origin)
    {
        _pathDirections[0] = origin;

        Vector3 reference = Mathf.Abs(Vector3.Dot(origin, Vector3.up)) > 0.92f
            ? Vector3.forward
            : Vector3.up;
        Vector3 tangent = Vector3.Cross(reference, origin).normalized;
        Vector3 bitangent = Vector3.Cross(origin, tangent).normalized;
        float pathAngle = Random.value * Mathf.PI * 2f;
        Vector3 travel = tangent * Mathf.Cos(pathAngle) + bitangent * Mathf.Sin(pathAngle);

        int count = Mathf.Clamp(PathCellCount, 1, 4);
        for (int i = 1; i < 4; i++)
        {
            if (i >= count)
            {
                _pathDirections[i] = Vector3.zero;
                continue;
            }

            float stepRadians = PathStepAngle * Mathf.Deg2Rad * i * Mathf.Lerp(0.75f, 1.25f, Random.value);
            Vector3 bend = Vector3.Cross(origin, travel).normalized;
            Vector3 bentTravel = (travel * Mathf.Cos(stepRadians * 0.3f) + bend * Mathf.Sin(stepRadians * 0.3f)).normalized;
            _pathDirections[i] = (origin * Mathf.Cos(stepRadians) + bentTravel * Mathf.Sin(stepRadians)).normalized;
        }
    }

    float CalculateCellIntensity(int cellIndex)
    {
        float elapsed = Time.time - _strikeStartTime;
        if (elapsed < 0f || elapsed > _strikeDuration)
            return 0f;

        float cellElapsed = elapsed - PathCellDelay * cellIndex;
        if (cellElapsed < 0f)
            return 0f;

        float firstPulse = Mathf.Exp(-cellElapsed * 22f);
        float secondaryElapsed = elapsed - SecondaryPulseDelay;
        float secondPulse = secondaryElapsed >= 0f
            ? Mathf.Exp(-(secondaryElapsed + PathCellDelay * cellIndex) * 26f)
            : 0f;
        float afterglow = Mathf.Clamp01(1f - elapsed / _strikeDuration);
        afterglow *= afterglow * 0.05f;

        return (_strikePower * firstPulse + _secondaryPower * secondPulse + _strikePower * afterglow)
            * _pathWeights[cellIndex];
    }

    void ScheduleNextStrike(float minDelay, float maxDelay)
    {
        _nextStrikeTime = Time.time + Random.Range(Mathf.Max(0.05f, minDelay), Mathf.Max(minDelay, maxDelay));
    }

    void UploadLightning()
    {
        float radius = Mathf.Max(FlashAngularRadius, 0.01f) * Mathf.Deg2Rad;
        float innerDot = Mathf.Cos(radius);
        float outerDot = Mathf.Cos(radius * 2.4f);
        Shader.SetGlobalVector(_weatherLightningParamsId, new Vector4(
            innerDot,
            outerDot,
            MinimumStormIntensity,
            0f));

        for (int i = 0; i < _weatherLightningCellIds.Length; i++)
        {
            Vector3 direction = _pathDirections[i];
            float intensity = direction.sqrMagnitude > 0.0001f ? Mathf.Max(0f, CalculateCellIntensity(i)) : 0f;
            Shader.SetGlobalVector(_weatherLightningCellIds[i], new Vector4(
                direction.x,
                direction.y,
                direction.z,
                intensity));
        }

        Color lightningColor = LightningColor;
        lightningColor.a = Mathf.Max(RainFlashIntensity, 0f);
        Shader.SetGlobalColor(_weatherLightningColorId, lightningColor);
    }

    void ClearLightning()
    {
        for (int i = 0; i < _weatherLightningCellIds.Length; i++)
            Shader.SetGlobalVector(_weatherLightningCellIds[i], Vector4.zero);

        float radius = Mathf.Max(FlashAngularRadius, 0.01f) * Mathf.Deg2Rad;
        Shader.SetGlobalVector(_weatherLightningParamsId, new Vector4(
            Mathf.Cos(radius),
            Mathf.Cos(radius * 2.4f),
            MinimumStormIntensity,
            0f));
        Color lightningColor = LightningColor;
        lightningColor.a = Mathf.Max(RainFlashIntensity, 0f);
        Shader.SetGlobalColor(_weatherLightningColorId, lightningColor);
    }

    // --- Console commands -------------------------------------------------

    [ConsoleCommand("enable", "Get or enable/disable lightning generation.", MonoTargetType.Single)]
    string EnableCmd(bool? on = null)
    {
        if (on == null) return $"lightning enabled: {EnableLightning}";
        EnableLightning = on.Value;
        return $"lightning enabled: {EnableLightning}";
    }

    [ConsoleCommand("delay", "Get or set min/max delay between strikes (seconds). Pass both values to set.", MonoTargetType.Single)]
    string DelayCmd(float? min = null, float? max = null)
    {
        if (min == null && max == null)
            return $"lightning delay: {MinDelay:F1}-{MaxDelay:F1}s";
        if (min == null || max == null)
            return "lightning.delay needs BOTH min and max values to set";
        MinDelay = Mathf.Max(0.5f, min.Value);
        MaxDelay = Mathf.Max(MinDelay, max.Value);
        return $"lightning delay: {MinDelay:F1}-{MaxDelay:F1}s";
    }

    [ConsoleCommand("intensity", "Get or set cloud-flash and rain-flash intensities. Pass both values to set.", MonoTargetType.Single)]
    string IntensityCmd(float? cloud = null, float? rain = null)
    {
        if (cloud == null && rain == null)
            return $"lightning intensity: cloud={CloudFlashIntensity:F2}, rain={RainFlashIntensity:F2}";
        if (cloud == null || rain == null)
            return "lightning.intensity needs BOTH cloud and rain values to set";
        CloudFlashIntensity = Mathf.Clamp(cloud.Value, 0f, 8f);
        RainFlashIntensity = Mathf.Clamp(rain.Value, 0f, 2f);
        return $"lightning intensity: cloud={CloudFlashIntensity:F2}, rain={RainFlashIntensity:F2}";
    }
}
