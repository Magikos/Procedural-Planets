#ifndef WEATHER_THRESHOLD_INCLUDED
#define WEATHER_THRESHOLD_INCLUDED

// Thresholds at one disable formation: normalized signals cannot exceed the threshold.
float WeatherThreshold(float low, float high, float value)
{
    if (high <= low) return value > low ? 1.0 : 0.0;
    float t = saturate((value - low) / (high - low));
    return t * t * (3.0 - 2.0 * t);
}

#endif
