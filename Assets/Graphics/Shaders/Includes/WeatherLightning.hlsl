#ifndef WEATHER_LIGHTNING_INCLUDED
#define WEATHER_LIGHTNING_INCLUDED

float4 _WeatherLightningParams;
float4 _WeatherLightningCell0;
float4 _WeatherLightningCell1;
float4 _WeatherLightningCell2;
float4 _WeatherLightningCell3;

float WeatherLightningCell(float4 cell, float3 normal, float storm)
{
    if (cell.w <= 0.0)
        return 0.0;

    float3 direction = dot(cell.xyz, cell.xyz) > 0.0001
        ? normalize(cell.xyz)
        : float3(0.0, 1.0, 0.0);
    float stormMask = smoothstep(_WeatherLightningParams.z, 1.0, storm);
    float locationMask = smoothstep(_WeatherLightningParams.y, _WeatherLightningParams.x, dot(normal, direction));
    return cell.w * stormMask * pow(saturate(locationMask), 1.35);
}

float WeatherLightning(float3 normal, float storm)
{
    float lightning = WeatherLightningCell(_WeatherLightningCell0, normal, storm);
    lightning = max(lightning, WeatherLightningCell(_WeatherLightningCell1, normal, storm));
    lightning = max(lightning, WeatherLightningCell(_WeatherLightningCell2, normal, storm));
    lightning = max(lightning, WeatherLightningCell(_WeatherLightningCell3, normal, storm));
    return lightning;
}

#endif
