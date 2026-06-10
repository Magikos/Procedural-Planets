Shader "Hidden/WeatherParticles"
{
HLSLINCLUDE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Includes/Math.hlsl"
#include "Includes/WeatherSampling.hlsl"
#include "Includes/ClimateSampling.hlsl"

TEXTURE2D(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);

int _WeatherParticlesEnabled;
int _WeatherParticleProof;
float3 _PrecipitationPlanetCenter;
float4 _PrecipitationRadii;
float4 _PrecipitationParams;
float4 _PrecipitationColor;
float4 _PrecipitationStormColor;
float4 _WeatherParticleCommon;
float4 _WeatherParticleCounts;
float4 _WeatherParticleDustParams;
float4 _WeatherParticleRainParams;
float4 _WeatherParticleRainExtended;
// Distant rain profile (3). Same fall column as profile 1 but at planet-scale
// radius so storms are visible from a distance. .x = radius (m), .y = streak
// length (m, much longer than close rain so streaks read at distance), .z =
// opacity multiplier, .w = visibility threshold.
float4 _WeatherParticleDistantRainParams;
// Distant rain extended params. .x = streak width (m, needs to be wide enough
// that streaks resolve to at least one pixel at far viewing distances — close
// rain at 0.06m is sub-pixel past ~500m and renders invisible).
float4 _WeatherParticleDistantRainExtended;
float4 _WeatherParticleSnowParams;
float4 _WeatherParticlePhaseParams;
float4 _WeatherParticleDustColor;
float4 _WeatherParticleSnowColor;
float3 _WindDirection;
float _WindSpeedMps;
float _WindStrength01;
float3 _SunParams;
float _NightAmbientIntensity;
float4 _WeatherLightningColor;

struct ParticleVaryings
{
    float4 positionCS : SV_POSITION;
    float4 screenPosition : TEXCOORD0;
    float viewDepth : TEXCOORD1;
    float edge : TEXCOORD2;
    float along : TEXCOORD3;
    float alpha : TEXCOORD4;
    float storm : TEXCOORD5;
    float lightning : TEXCOORD6;
    float3 color : TEXCOORD7;
};

float ParticleHash11(float value)
{
    return Hash12(float2(value * 1.371 + 17.17, value * 0.719 + 43.11));
}

float2 ParticleHash21(float value)
{
    return float2(
        ParticleHash11(value + 11.3),
        ParticleHash11(value + 79.1));
}

float SceneDepthLinear(float2 uv)
{
    float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
    return LinearEyeDepth(rawDepth, _ZBufferParams);
}

void RibbonCorner(uint corner, out float along, out float side)
{
    if (corner == 0u) { along = 0.0; side = -1.0; }
    else if (corner == 1u) { along = 0.0; side = 1.0; }
    else if (corner == 2u) { along = 1.0; side = -1.0; }
    else if (corner == 3u) { along = 1.0; side = -1.0; }
    else if (corner == 4u) { along = 0.0; side = 1.0; }
    else { along = 1.0; side = 1.0; }
}

float ProfileCount(int profile)
{
    if (profile == 0) return _WeatherParticleCounts.x;
    if (profile == 1) return _WeatherParticleCounts.y;
    if (profile == 2) return _WeatherParticleCounts.z;
    return _WeatherParticleCounts.w;
}

float ProfileProofVisibility(int profile)
{
    if (_WeatherParticleProof == 0)
        return -1.0;
    if (_WeatherParticleProof == 4)
        return 1.0;
    // Proof slots 1=Dust, 2=Rain, 3=Snow. Distant rain (profile 3) follows
    // close rain's proof slot — they share a "Rain" visual category.
    int proofProfile = profile == 3 ? 1 : profile;
    return _WeatherParticleProof == proofProfile + 1 ? 1.0 : 0.0;
}

float3 SafeParticleNormalize(float3 value, float3 fallback)
{
    float lengthSquared = dot(value, value);
    return lengthSquared > 0.000001 ? value * rsqrt(lengthSquared) : fallback;
}

ParticleVaryings WeatherParticleVertex(
    uint vertexID,
    uint instanceID,
    int profile)
{
    ParticleVaryings output = (ParticleVaryings)0;

    float3 cameraVector = _WorldSpaceCameraPos.xyz - _PrecipitationPlanetCenter;
    float cameraRadius = length(cameraVector);
    float3 cameraNormal = cameraRadius > 0.0001
        ? cameraVector / cameraRadius
        : float3(0.0, 1.0, 0.0);
    float seaRadius = max(_PrecipitationRadii.w, 1.0);
    float cameraAltitude = cameraRadius - seaRadius;
    float precipitationBottomRadius = _PrecipitationRadii.x;
    float precipitationTopRadius = max(
        _PrecipitationRadii.y,
        precipitationBottomRadius + 1.0);
    float precipitationLayerThickness =
        precipitationTopRadius - precipitationBottomRadius;

    // Profile-specific horizontal radius. Close rain (1) uses RainExtended (~600m).
    // Distant rain (3) uses DistantRainParams.x (~3000m+, planet-scale, so storms
    // are visible from far away). Dust (0) and snow (2) stay at the camera-local
    // radius from Common.
    float radius;
    if (profile == 1) radius = max(_WeatherParticleRainExtended.x, 1.0);
    else if (profile == 3) radius = max(_WeatherParticleDistantRainParams.x, 1.0);
    else radius = max(_WeatherParticleCommon.x, 1.0);
    float maxCameraAltitude = max(_WeatherParticleCommon.y, 1.0);
    float verticalRange = max(_WeatherParticleCommon.z, 1.0);
    float systemVisible = _WeatherParticlesEnabled != 0 &&
        cameraAltitude >= 0.0 &&
        cameraAltitude <= maxCameraAltitude
        ? 1.0
        : 0.0;

    float id = (float)instanceID;
    float count = max(ProfileCount(profile), 1.0);
    float gridWidth = ceil(sqrt(count));
    float cellX = fmod(id, gridWidth);
    float cellY = floor(id / gridWidth);

    float cameraLongitude = atan2(cameraNormal.z, cameraNormal.x);
    float cameraLatitude = asin(clamp(cameraNormal.y, -1.0, 1.0));
    float latitudeScale = max(cos(cameraLatitude), 0.25);
    float angularRadius = radius / max(cameraRadius, 1.0);
    float angularSpacing = max((angularRadius * 2.0) / gridWidth, 0.00001);
    float2 cameraTile = floor(
        float2(cameraLongitude * latitudeScale, cameraLatitude) / angularSpacing);
    float2 tile = cameraTile + float2(cellX, cellY) - gridWidth * 0.5;
    // WORLD-ANCHORED SEED: every per-particle property (jitter, vertical phase,
    // wind phase, visibility roll, random opacity) hashes off the world-space
    // tile coords + profile. If we hashed off instanceID instead, then as the
    // camera moves and the SAME world tile gets re-rendered by a DIFFERENT
    // instance, that tile would get a fresh random altitude/phase every camera
    // step — visually identical to the "particles regenerate as camera moves"
    // bug. With tile-based seeding the same world tile always shows the same
    // particle, so rain stays in world space and the camera moves through it.
    float tileSeed = tile.x * 173.0 + tile.y * 263.0 + profile * 719.0;
    float2 tileJitter = ParticleHash21(tileSeed);
    float2 sphericalCell = (tile + tileJitter) * angularSpacing;
    float longitude = sphericalCell.x / latitudeScale;
    float latitude = clamp(sphericalCell.y, -1.52, 1.52);
    float cosLatitude = cos(latitude);
    float3 normal = normalize(float3(
        cosLatitude * cos(longitude),
        sin(latitude),
        cosLatitude * sin(longitude)));

    float3 reference = abs(normal.y) > 0.92
        ? float3(1.0, 0.0, 0.0)
        : float3(0.0, 1.0, 0.0);
    float3 tangent = normalize(cross(reference, normal));
    float3 wind = SafeParticleNormalize(
        _WindDirection,
        float3(1.0, 0.0, 0.0));
    float3 windTangent = wind - normal * dot(wind, normal);
    windTangent = SafeParticleNormalize(windTangent, tangent);

    float4 weather = SampleWeather(normal);
    float4 dynamics = SampleDynamics(normal);
    float2 climate = SampleClimate01(normal);
    float temperatureCelsius = ClimateTemperatureCelsius(climate.x);
    float storm = saturate(weather.g);
    // Rain shows wherever there is actual local rain (dynamics.b). The previous
    // smoothstep(0.52, 0.88, weather.r) compounded on cloud cover, but typical world
    // cloud values cluster around 0.5 — barely crossing the threshold — so rain
    // visibility collapsed to near-zero even during heavy storms. Direct gating on
    // the rain signal matches the design intent: "rain drops during rain."
    float rainSignal = saturate(dynamics.b);
    float snowBlend = max(_WeatherParticlePhaseParams.y, 0.1);
    float snowPhase = 1.0 - smoothstep(
        _WeatherParticlePhaseParams.x - snowBlend,
        _WeatherParticlePhaseParams.x + snowBlend,
        temperatureCelsius);

    float proofVisibility = ProfileProofVisibility(profile);
    float visibility;
    float width;
    float streakLength;
    float fallSpeed;
    float turbulence;
    float3 color;
    float profileOpacity;

    if (profile == 0)
    {
        float dustDensity = _WeatherParticleDustParams.x +
            _WindStrength01 * 0.32 +
            storm * _WeatherParticleDustParams.y;
        dustDensity *= lerp(1.0, 0.12, rainSignal);
        dustDensity *= lerp(1.1, 0.62, climate.y);
        visibility = saturate(dustDensity);
        width = _WeatherParticleDustParams.w;
        float streakFactor = smoothstep(2.0, 15.0, _WindSpeedMps);
        streakLength = lerp(
            width * 1.15,
            _WeatherParticlePhaseParams.w,
            streakFactor);
        fallSpeed = 0.0;
        turbulence = _WeatherParticleCommon.w *
            lerp(0.35, 1.0, _WindStrength01) *
            (1.0 + storm * 1.2);
        color = _WeatherParticleDustColor.rgb;
        profileOpacity = _WeatherParticleDustParams.z;
    }
    else if (profile == 1)
    {
        visibility = smoothstep(
            _WeatherParticleRainParams.x,
            min(1.0, _WeatherParticleRainParams.x + 0.38),
            rainSignal) * (1.0 - snowPhase);
        width = _WeatherParticlePhaseParams.z;
        streakLength = _WeatherParticleRainParams.z;
        fallSpeed = _WeatherParticleRainParams.w;
        turbulence = 0.08 + storm * 0.12;
        color = lerp(
            _PrecipitationColor.rgb,
            _PrecipitationStormColor.rgb,
            storm);
        profileOpacity = _WeatherParticleRainParams.y;
    }
    else if (profile == 2)
    {
        visibility = smoothstep(
            _WeatherParticleSnowParams.x,
            min(1.0, _WeatherParticleSnowParams.x + 0.38),
            rainSignal) * snowPhase;
        width = _WeatherParticleSnowParams.z;
        streakLength = width * lerp(1.4, 2.8, _WindStrength01);
        fallSpeed = _WeatherParticleSnowParams.w;
        turbulence = 0.45 + _WindStrength01 * 0.65;
        color = _WeatherParticleSnowColor.rgb;
        profileOpacity = _WeatherParticleSnowParams.y;
    }
    else
    {
        // Distant rain (profile 3). Same fall column as close rain but with planet-
        // scale radius and much longer/wider streaks. Visibility uses its own
        // threshold so distant streaks can show wherever weather has *any* rain
        // signal, while close rain (profile 1) only shows in heavier rain. Snow
        // temperature gate still applies — distant rain becomes invisible in snow
        // conditions.
        visibility = smoothstep(
            _WeatherParticleDistantRainParams.w,
            min(1.0, _WeatherParticleDistantRainParams.w + 0.38),
            rainSignal) * (1.0 - snowPhase);
        width = _WeatherParticleDistantRainExtended.x;
        streakLength = _WeatherParticleDistantRainParams.y;
        fallSpeed = _WeatherParticleRainParams.w;
        turbulence = 0.04 + storm * 0.08;
        color = lerp(
            _PrecipitationColor.rgb,
            _PrecipitationStormColor.rgb,
            storm);
        profileOpacity = _WeatherParticleDistantRainParams.z;
    }

    if (proofVisibility >= 0.0)
        visibility = proofVisibility;
    // Probabilistic density gate: binary keep/discard. World-anchored — keyed
    // on tileSeed so the same world tile is always either visible-or-not under
    // identical weather, even after the camera-tile reassignment.
    visibility = ParticleHash11(tileSeed + 7.0) <= visibility ? 1.0 : 0.0;

    float travelRange = radius * 2.0;
    float travelPhase = ParticleHash11(tileSeed + 31.0);
    float travelDistance = frac(
        travelPhase + _GameTime * max(_WindSpeedMps, 0.0) /
        max(travelRange, 1.0)) * travelRange - radius;

    float verticalPhase = ParticleHash11(tileSeed + 53.0);
    float particleRadius;
    float precipitationLayerFade = 1.0;
    if (profile == 0)
    {
        float dustHeight = lerp(-verticalRange * 0.2, verticalRange * 0.6, verticalPhase);
        dustHeight += sin(
            _GameTime * (0.35 + _WindStrength01) +
            tileSeed * 0.00037) * turbulence;
        particleRadius = cameraRadius + dustHeight;
    }
    else if (profile == 1 || profile == 3)
    {
        // Naming convention to be aware of: _PrecipitationRadii.x is the BOTTOM of
        // the precipitation column (sea + ~25m, where drops "land"), and .y is the
        // TOP (cloud base, ~sea + 375m, where drops "form"). Drops spawn at top,
        // fall to bottom, recycle. Per-pixel depth test handles terrain occlusion
        // automatically — a drop falling over a mountain is depth-culled when it
        // enters terrain; a drop falling over water continues to sea level then
        // recycles. The recycle happens at the geometric column floor (sea level),
        // so visually drops appear to land on ground/water before being replaced.
        float rainTop = precipitationTopRadius;
        float rainBottom = max(seaRadius, 1.0);
        float rainThickness = max(rainTop - rainBottom, 1.0);
        float layerPosition01 = frac(
            verticalPhase -
            _GameTime * max(fallSpeed, 0.1) / rainThickness);
        particleRadius = lerp(rainBottom, rainTop, layerPosition01);
        // Narrow 2% fade bands at top (drops "form" out of cloud base) and bottom
        // (drops "land") hide the wrap discontinuity without visibly trimming the
        // column's usable height.
        precipitationLayerFade =
            smoothstep(0.0, 0.02, layerPosition01) *
            (1.0 - smoothstep(0.98, 1.0, layerPosition01));
    }
    else
    {
        // Snow stays within the cloud layer — slow, dispersed, tumbling.
        float layerPosition01 = frac(
            verticalPhase -
            _GameTime * max(fallSpeed, 0.1) / precipitationLayerThickness);
        particleRadius = lerp(
            precipitationBottomRadius,
            precipitationTopRadius,
            layerPosition01);
        precipitationLayerFade =
            smoothstep(0.0, 0.06, layerPosition01) *
            (1.0 - smoothstep(0.94, 1.0, layerPosition01));
    }

    float3 head = _PrecipitationPlanetCenter +
        normal * particleRadius +
        windTangent * travelDistance;
    if (profile == 2)
    {
        head += tangent * sin(_GameTime * 0.9 + tileSeed * 0.00071) * turbulence;
    }

    float3 velocity = windTangent * max(_WindSpeedMps, profile == 0 ? 0.2 : 0.0) -
        normal * fallSpeed;
    float3 motionDirection = SafeParticleNormalize(velocity, windTangent);
    float3 viewDirection = SafeParticleNormalize(
        _WorldSpaceCameraPos.xyz - head,
        -motionDirection);
    float3 sideDirection = SafeParticleNormalize(
        cross(viewDirection, motionDirection),
        tangent);

    uint segment = vertexID / 6u;
    uint corner = vertexID - segment * 6u;
    float cornerAlong;
    float side;
    RibbonCorner(corner, cornerAlong, side);
    float along = (segment + cornerAlong) / 3.0;
    float taper = sin(saturate(along) * 3.14159265);
    taper = profile == 2 ? lerp(0.72, 1.0, taper) : max(taper, 0.08);
    float curveScale = profile == 0 ? 0.18 : (profile == 2 ? 0.55 : 0.0);
    float curve = sin(
        along * 5.4 +
        tileSeed * 0.00029 +
        _GameTime * (0.8 + _WindStrength01 * 1.6)) *
        turbulence * width * taper * curveScale;
    float3 center = head - motionDirection * streakLength * (1.0 - along);
    float3 worldPosition = center +
        sideDirection * (side * width * taper + curve);

    float angularDistance = acos(clamp(dot(normal, cameraNormal), -1.0, 1.0));
    float edgeFade = saturate(1.0 - angularDistance * cameraRadius / radius);
    edgeFade = smoothstep(0.0, 0.16, edgeFade);
    float randomOpacity = lerp(
        0.46,
        1.0,
        ParticleHash11(tileSeed + 89.0));

    output.alpha = systemVisible * visibility * edgeFade * precipitationLayerFade *
        randomOpacity * profileOpacity;
    output.storm = storm;
    output.lightning = WeatherLightning(normal, storm);
    output.color = color;
    output.positionCS = TransformWorldToHClip(worldPosition);
    output.screenPosition = ComputeScreenPos(output.positionCS);
    output.viewDepth = -TransformWorldToView(worldPosition).z;
    output.edge = side;
    output.along = along;
    return output;
}

ParticleVaryings VertDust(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
{
    return WeatherParticleVertex(vertexID, instanceID, 0);
}

ParticleVaryings VertRain(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
{
    return WeatherParticleVertex(vertexID, instanceID, 1);
}

ParticleVaryings VertSnow(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
{
    return WeatherParticleVertex(vertexID, instanceID, 2);
}

ParticleVaryings VertDistantRain(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
{
    return WeatherParticleVertex(vertexID, instanceID, 3);
}

float4 FragParticle(ParticleVaryings input) : SV_Target
{
    if (input.alpha <= 0.0001)
        discard;

    float2 uv = input.screenPosition.xy / max(input.screenPosition.w, 0.0001);
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        discard;

    float sceneDepth = SceneDepthLinear(uv);
    if (input.viewDepth > sceneDepth + 0.15)
        discard;

    float softIntersection = saturate((sceneDepth - input.viewDepth) / 1.25);
    float edgeFade = 1.0 - smoothstep(0.48, 1.0, abs(input.edge));
    float endFade = smoothstep(0.0, 0.08, input.along) *
        (1.0 - smoothstep(0.92, 1.0, input.along));
    float alpha = input.alpha * edgeFade * endFade * softIntersection;
    if (alpha <= 0.0001)
        discard;

    float3 cameraNormal = normalize(
        _WorldSpaceCameraPos.xyz - _PrecipitationPlanetCenter);
    float localSun = saturate((dot(cameraNormal, _SunParams.xyz) + 0.1) * 3.0);
    float lightning = input.lightning * _WeatherLightningColor.a;
    float light = _NightAmbientIntensity * 0.65 + localSun * 0.72 + 0.18;
    float3 litColor = input.color * (light + lightning * 0.55) +
        _WeatherLightningColor.rgb * lightning * 0.18;
    return float4(litColor, saturate(alpha));
}

ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Dust"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertDust
            #pragma fragment FragParticle
            ENDHLSL
        }

        Pass
        {
            Name "Rain"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertRain
            #pragma fragment FragParticle
            ENDHLSL
        }

        Pass
        {
            Name "Snow"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertSnow
            #pragma fragment FragParticle
            ENDHLSL
        }

        Pass
        {
            Name "DistantRain"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertDistantRain
            #pragma fragment FragParticle
            ENDHLSL
        }
    }
}
