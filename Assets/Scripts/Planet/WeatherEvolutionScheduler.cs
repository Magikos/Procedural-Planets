using UnityEngine;

sealed class WeatherEvolutionScheduler
{
    float _accumulator;
    bool _missingComputeLogged;


    readonly ILogger _logger;

    public WeatherEvolutionScheduler(ILogger logger) { _logger = logger; }

    public void Reset()
    {
        _accumulator = 0f;
    }

    public void Tick(SphericalWeatherGrid grid, CloudDto settings, ComputeShader compute,
                     Vector3 windDirection, float windSpeed, float seaLevelRadius,
                     WeatherDiagnostics diagnostics)
    {
        if (grid == null || settings == null)
        {
            _accumulator = 0f;
            return;
        }

        if (compute == null)
        {
            if (!_missingComputeLogged)
            {
                _logger.Log(LogLevel.Warning, "Weather", "WeatherCompute is not assigned; dynamic weather evolution is disabled.");
                _missingComputeLogged = true;
            }
            _accumulator = 0f;
            return;
        }

        _accumulator += Time.deltaTime;
        float interval = Mathf.Max(settings.EvolutionInterval, 0.05f);
        if (_accumulator < interval)
            return;

        float stepAngle = StepAdvectionAngle(interval, windSpeed, seaLevelRadius);
        int maxSteps = 3;
        int steps = 0;
        while (_accumulator >= interval && steps < maxSteps)
        {
            if (grid.Advance(compute, settings, interval, windDirection, stepAngle))
            {
                diagnostics.RecordEvolutionDispatch(interval);
            }

            grid.AdvanceSurface(compute, interval, windSpeed);
            _accumulator -= interval;
            steps++;
        }


        float maxCarry = interval * maxSteps;
        if (_accumulator > maxCarry)
            _accumulator = maxCarry;
    }

    static float StepAdvectionAngle(float stepSeconds, float windSpeed, float seaLevelRadius)
    {
        float angularSpeed = windSpeed / Mathf.Max(seaLevelRadius, 1f);
        return angularSpeed * CloudConstants.FrontAdvectionSpeedMultiplier * stepSeconds;
    }
}
