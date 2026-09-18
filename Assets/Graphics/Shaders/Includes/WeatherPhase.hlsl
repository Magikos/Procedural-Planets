#ifndef WEATHER_PHASE_INCLUDED
#define WEATHER_PHASE_INCLUDED
float4 _WeatherParticlePhaseParams;
float WeatherSnowFraction(float temperatureCelsius)
{
    float blend = max(_WeatherParticlePhaseParams.y, 0.1);
    return 1.0 - smoothstep(_WeatherParticlePhaseParams.x - blend,
        _WeatherParticlePhaseParams.x + blend, temperatureCelsius);
}

#endif
