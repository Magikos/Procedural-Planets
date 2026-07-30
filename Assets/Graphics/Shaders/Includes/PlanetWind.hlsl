#ifndef PLANET_WIND_INCLUDED
#define PLANET_WIND_INCLUDED

// The wind "interface" for shaders: three globals published by whichever wind provider a scene has --
// WeatherManager on the planet (IWeatherProvider), AssetShowcaseController in the showcase, nobody
// elsewhere. With NO provider these stay 0, so foliage is perfectly still: there is no animation without
// a wind system, by design. Declared here ONCE and shared by every consumer (CloudShadows, foliage,
// grass) so there is one source of truth and no double-declaration across passes.
//
// This header declares ONLY the globals (no _Time, no functions) so it is safe to include from any
// shader regardless of what else it has pulled in. The sway function lives with each consumer, where the
// URP Core header (and thus _Time) is guaranteed to be present.
float3 _WindDirection;   // world-space direction the wind blows toward (xz plane)
float  _WindSpeedMps;     // wind speed, metres/second
float  _WindStrength01;   // normalised 0..1 gust strength

#endif
