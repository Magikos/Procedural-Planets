using UnityEngine;

sealed class WeatherEvolutionScheduler
{
    float _accumulator;
    float _cloudWindAngle;
    bool _missingComputeLogged;

    static readonly int _cloudWindAngleId = Shader.PropertyToID(ShaderGlobalIds.CloudWindAngle);

    readonly ILogger _logger;

    public WeatherEvolutionScheduler(ILogger logger) { _logger = logger; }

    public void Reset()
    {
        _accumulator = 0f;
        _cloudWindAngle = 0f;
        Shader.SetGlobalFloat(_cloudWindAngleId, 0f);
    }

    public void Tick(SphericalWeatherGrid grid, CloudDto settings, ComputeShader compute,
                     Vector3 windDirection, float windSpeed, float seaLevelRadius,
                     WeatherDiagnostics diagnostics)
    {
        if (grid == null || !settings.EnableWeatherEvolution)
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
                _cloudWindAngle += stepAngle;
                diagnostics.RecordEvolutionDispatch(interval);
            }

            _accumulator -= interval;
            steps++;
        }

        Shader.SetGlobalFloat(_cloudWindAngleId, _cloudWindAngle);

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
