using UnityEngine;

public class CelestialManager : MonoBehaviour
{
    [Header("References")]
    public Light SunLight;
    public Transform MoonTransform;
    public Transform PlanetCenter;

    [Header("Sun")]
    [Tooltip("Real seconds per full day/night cycle")]
    public float DayLengthSeconds = 120f;
    [Range(-45f, 45f), Tooltip("Axial tilt in degrees — affects how high the sun gets")]
    public float AxialTilt = 23.5f;

    [Header("Moon")]
    [Tooltip("How many days per full moon cycle")]
    public float MoonCycleDays = 8f;
    [Tooltip("Distance from planet center (set automatically from planet radius if 0)")]
    public float MoonOrbitRadius;
    [Range(0f, 15f), Tooltip("Moon orbital plane tilt relative to sun")]
    public float MoonInclination = 5f;

    [Header("State")]
    [Range(0f, 1f), Tooltip("Starting time of day: 0=midnight, 0.25=sunrise, 0.5=noon, 0.75=sunset")]
    public float StartTimeOfDay = 0.25f;

    float _timeOfDay;
    float _moonCycleProgress;
    float _planetRadius;
    bool _wasDay;

    // 8 moon phases, track which we're in to fire events on change
    int _lastMoonPhaseIndex = -1;

    public float TimeOfDay => _timeOfDay;
    public Vector3 SunDirection => SunLight != null ? -SunLight.transform.forward : Vector3.up;

    public bool IsDayAt(Vector3 worldPosition)
    {
        Vector3 center = PlanetCenter != null ? PlanetCenter.position : Vector3.zero;
        Vector3 surfaceNormal = (worldPosition - center).normalized;
        return Vector3.Dot(surfaceNormal, SunDirection) > 0f;
    }

    /// <summary>
    /// -1 = full moon, 0 = half, +1 = new moon.
    /// Derived from angle between sun and moon as seen from planet.
    /// </summary>
    public float MoonPhase { get; private set; }

    /// <summary>0-7 discrete phase index: 0=New, 1=WaxingCrescent, 2=FirstQuarter, 3=WaxingGibbous, 4=Full, 5=WaningGibbous, 6=LastQuarter, 7=WaningCrescent</summary>
    public int MoonPhaseIndex => Mathf.FloorToInt((_moonCycleProgress % 1f) * 8f) % 8;

    /// <summary>0 at new moon, 1 at full moon. Useful for magic intensity.</summary>
    public float MoonFullness => (1f - MoonPhase) * 0.5f;

    /// <summary>0-1 progress through the current season cycle</summary>
    public float SeasonProgress => 0f; // TODO: implement when seasons are needed

    void Start()
    {
        _timeOfDay = StartTimeOfDay;
        _wasDay = true;

        EventBus<PlanetGeneratedEvent>.Listen(OnPlanetGenerated);
        TryInitFromExistingPlanet();
    }

    void OnDestroy()
    {
        EventBus<PlanetGeneratedEvent>.Unlisten(OnPlanetGenerated);
    }

    void OnPlanetGenerated(PlanetGeneratedEvent evt)
    {
        _planetRadius = evt.PlanetRadius;
        if (PlanetCenter == null)
        {
            var planet = FindAnyObjectByType<Planet>();
            if (planet != null) PlanetCenter = planet.transform;
        }

        MoonOrbitRadius = _planetRadius * 3f;
    }

    void TryInitFromExistingPlanet()
    {
        if (PlanetCenter == null) return;
        var planet = PlanetCenter.GetComponent<Planet>();
        if (planet == null || planet.ShapeGenerator == null) return;

        float elevMax = planet.ShapeGenerator.ElevationMax;
        if (elevMax == float.MinValue) return;

        _planetRadius = planet._planetSettings.PlanetRadius * (1 + elevMax);
        MoonOrbitRadius = _planetRadius * 3f;
    }

    void Update()
    {
        if (DayLengthSeconds <= 0f) return;

        float dt = Time.deltaTime;
        UpdateSun(dt);
        UpdateMoon(dt);
        FireEvents();
    }

    void UpdateSun(float dt)
    {
        _timeOfDay = (_timeOfDay + dt / DayLengthSeconds) % 1f;

        float sunAngle = _timeOfDay * 360f;
        Vector3 center = PlanetCenter != null ? PlanetCenter.position : Vector3.zero;

        // Sun orbits in a tilted plane
        Quaternion tilt = Quaternion.Euler(AxialTilt, 0f, 0f);
        Vector3 sunDir = tilt * new Vector3(
            Mathf.Sin(sunAngle * Mathf.Deg2Rad),
            -Mathf.Cos(sunAngle * Mathf.Deg2Rad),
            0f
        );

        if (SunLight != null)
        {
            // Position far away so it acts as directional
            SunLight.transform.position = center - sunDir * (_planetRadius > 0 ? _planetRadius * 10f : 1000f);
            SunLight.transform.LookAt(center);
        }
    }

    void UpdateMoon(float dt)
    {
        if (MoonTransform == null) return;

        float moonDayLength = DayLengthSeconds * MoonCycleDays;
        if (moonDayLength <= 0f) return;

        _moonCycleProgress = (_moonCycleProgress + dt / moonDayLength) % 1f;

        float moonAngle = _moonCycleProgress * 360f;
        Vector3 center = PlanetCenter != null ? PlanetCenter.position : Vector3.zero;

        Quaternion inclination = Quaternion.Euler(MoonInclination, 0f, 0f);
        Vector3 moonDir = inclination * new Vector3(
            Mathf.Sin(moonAngle * Mathf.Deg2Rad),
            0f,
            Mathf.Cos(moonAngle * Mathf.Deg2Rad)
        );

        MoonTransform.position = center + moonDir * MoonOrbitRadius;
        MoonTransform.LookAt(center);

        // Phase = dot between sun direction and moon direction from planet
        Vector3 toMoon = (MoonTransform.position - center).normalized;
        MoonPhase = Vector3.Dot(SunDirection, toMoon);
    }

    void FireEvents()
    {
        Vector3 center = PlanetCenter != null ? PlanetCenter.position : Vector3.zero;
        bool isDay = IsDayAt(center + Vector3.forward * _planetRadius);
        if (isDay != _wasDay)
        {
            _wasDay = isDay;
            EventBus<DayNightChangedEvent>.Raise(new DayNightChangedEvent(_timeOfDay, isDay));
        }

        int phaseIdx = MoonPhaseIndex;
        if (phaseIdx != _lastMoonPhaseIndex)
        {
            _lastMoonPhaseIndex = phaseIdx;
            EventBus<MoonPhaseChangedEvent>.Raise(new MoonPhaseChangedEvent(MoonPhase));
        }
    }
}
