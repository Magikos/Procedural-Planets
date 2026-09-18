#ifndef WATER_INTERACTIONS_INCLUDED
#define WATER_INTERACTIONS_INCLUDED
int _WaterRippleCount;
float4 _WaterRippleOrigins[24]; // xyz origin, w age in seconds
float4 _WaterRippleNormals[24]; // xyz up, w strength
float4 _WaterRippleParams[24];  // radius, lifetime, foam, propagation speed

void WaterInteractionEffects(float3 positionWS, out float3 slope, out float foam)
{
    slope = 0.0;
    foam = 0.0;
    [loop] for (int index = 0; index < min(_WaterRippleCount, 24); index++)
    {
        float4 origin = _WaterRippleOrigins[index];
        float4 normal = _WaterRippleNormals[index];
        float4 parameters = _WaterRippleParams[index];
        float3 offset = positionWS - origin.xyz;
        float front = parameters.x + origin.w * parameters.w;
        float width = 0.22 + origin.w * 0.16;
        float limit = front + width * 2.0;
        if (dot(offset, offset) > limit * limit || abs(dot(offset, normal.xyz)) > 1.5) continue;
        float3 tangent = offset - normal.xyz * dot(offset, normal.xyz);
        float radius = length(tangent);
        float phase = (radius - front) / width;
        float envelope = exp2(-phase * phase * 2.0) * pow(saturate(1.0 - origin.w / parameters.y), 2.0);
        slope += tangent / max(radius, 0.01) * (sin(phase * 3.14159) * envelope * normal.w * 0.45);
        foam = max(foam, envelope * parameters.z * normal.w);
    }
}
#endif
