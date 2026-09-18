"""Generate temporary GPU probes from current and pre-G01 shader source."""
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/Tests/GrassAuditProbe"


def source(path, baseline):
    if baseline:
        return subprocess.check_output(["git", "show", f"d1e0f624:{path}"], cwd=ROOT, text=True)
    return (ROOT / path).read_text(encoding="utf-8-sig")


def section(text, start, end):
    return text[text.index(start):text.index(end)]


def build(baseline):
    terrain = source("Assets/Graphics/Shaders/PlanetVertexColor.shader", baseline)
    common = source("Assets/Graphics/Shaders/Includes/GrassPlacementCommon.hlsl", baseline)
    placement = source("Assets/Graphics/Shaders/Includes/GrassPlacementParamBlend.hlsl", baseline)
    prefix = """#pragma kernel Probe
Texture2DArray<float4> _ClimateMap;
int _ClimateMapResolution;
Texture2D<float4> _BiomeIds;
Texture2D<float4> _BiomeWeights;
float4 _BiomeMap_TexelSize;
float2 _ProbeUv;
RWStructuredBuffer<float4> _Result;
#define LOAD_TEXTURE2D(tex,coord) tex.Load(int3(coord,0))
float2 BiomeMapUv(float2 uv) { return uv; }
"""
    declarations = section(common, "struct BiomeGrassParams", "struct GrassBladeInstance")
    declarations += "StructuredBuffer<BiomeGrassParams> _Params;\n"
    declarations += "#define GRASS_PLACEMENT_PARAM_COUNT 2\n#define GRASS_PLACEMENT_PARAM_BUFFER _Params\n"
    overlay = section(terrain, "            struct GrassOverlayParams", "            struct GrassOverlayEval")
    overlay += section(terrain, "            GrassOverlayParams EmptyGrassOverlayParams", "            GrassOverlayEval EmptyGrassOverlayEval")
    kernel = """
[numthreads(1,1,1)]
void Probe(uint3 id : SV_DispatchThreadID)
{
    float density;
    float2 f = saturate(_ProbeUv * 2.0 - 0.5);
    BiomeGrassParams p = BlendGrassParamCorners(
        _BiomeIds.Load(int3(0,0,0)), _BiomeWeights.Load(int3(0,0,0)),
        _BiomeIds.Load(int3(1,0,0)), _BiomeWeights.Load(int3(1,0,0)),
        _BiomeIds.Load(int3(0,1,0)), _BiomeWeights.Load(int3(0,1,0)),
        _BiomeIds.Load(int3(1,1,0)), _BiomeWeights.Load(int3(1,1,0)), f, density);
    GrassOverlayParams o = SampleGrassOverlayParams(_ProbeUv);
    _Result[0] = p.Shape;
    _Result[1] = p.Placement;
    _Result[2] = p.Tint;
    _Result[3] = p.TintDry;
    _Result[4] = p.TintLush;
    _Result[5] = float4(o.density,o.maxSlopeDeg,o.slopeFadeDeg,o.waterClearance);
    _Result[6] = float4(o.tint,1);
    _Result[7] = float4(
        1-smoothstep(p.Placement.x-p.Placement.y,p.Placement.x+p.Placement.y,30),
        1-smoothstep(o.maxSlopeDeg-o.slopeFadeDeg,o.maxSlopeDeg+o.slopeFadeDeg,30),0,0);
}
"""
    OUT.mkdir(parents=True, exist_ok=True)
    name = "Baseline" if baseline else "Current"
    (OUT / f"{name}.compute").write_text(prefix + declarations + placement + overlay + kernel, encoding="utf-8")


if __name__ == "__main__":
    build(False)
    build(True)
    print(f"Generated current and baseline GPU probes in {OUT}")
