using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProceduralPlanets.Tests
{
    public sealed class SurfaceWeatherTests
    {
        [Test]
        public void DesertDepletesCloudAndRainWithoutResettingWindHistory()
        {
            var compute = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Graphics/Shaders/WeatherEvolution.compute"));
            var textures = new RenderTexture[6];
            var climate = new Texture2DArray(8,8,6,TextureFormat.RGFloat,false);
            try
            {
                for (int i = 0; i < textures.Length; i++)
                {
                    textures[i] = new RenderTexture(8,8,0,RenderTextureFormat.ARGBFloat)
                    { dimension = TextureDimension.Tex2DArray, volumeDepth = 6, enableRandomWrite = true };
                    textures[i].Create();
                }
                for (int face = 0; face < 6; face++) climate.SetPixels(new Color[64],face);
                climate.Apply();
                int force = compute.FindKernel("CSForceWeather");
                int init = compute.FindKernel("CSInitFlow");
                int evolve = compute.FindKernel("CSEvolveWeather");
                compute.SetInt("_Resolution",8);
                compute.SetVector("_ForceWeatherValue",new Vector4(1,1,1,.5f));
                compute.SetVector("_ForceDynamicsValue",Vector4.one);
                compute.SetTexture(force,"_WeatherWrite",textures[0]);
                compute.SetTexture(force,"_DynamicsWrite",textures[2]);
                compute.Dispatch(force,1,1,6);
                compute.SetTexture(init,"_FlowWrite",textures[4]);
                compute.Dispatch(init,1,1,6);
                compute.SetTexture(evolve,"_ClimateRead",climate);
                compute.SetInt("_HasClimate",1);
                foreach (var field in typeof(CloudConstants).GetFields())
                    if (field.IsLiteral && field.FieldType == typeof(float))
                        compute.SetFloat("_"+field.Name,(float)field.GetRawConstantValue());
                compute.SetFloat("_StormThreshold",.7f);
                compute.SetFloat("_DeltaTime",1);
                compute.SetFloat("_StepAngle",0);
                compute.SetVector("_WindDirection",Vector3.right);
                void Step()
                {
                    compute.SetTexture(evolve,"_WeatherRead",textures[0]);
                    compute.SetTexture(evolve,"_WeatherWrite",textures[1]);
                    compute.SetTexture(evolve,"_DynamicsRead",textures[2]);
                    compute.SetTexture(evolve,"_DynamicsWrite",textures[3]);
                    compute.SetTexture(evolve,"_FlowRead",textures[4]);
                    compute.SetTexture(evolve,"_FlowWrite",textures[5]);
                    compute.Dispatch(evolve,1,1,6);
                    (textures[0],textures[1])=(textures[1],textures[0]);
                    (textures[2],textures[3])=(textures[3],textures[2]);
                    (textures[4],textures[5])=(textures[5],textures[4]);
                }
                Color Read(RenderTexture texture)
                {
                    var read = AsyncGPUReadback.Request(texture,0,TextureFormat.RGBAFloat);
                    read.WaitForCompletion(); Assert.IsFalse(read.hasError);
                    return read.GetData<Color>()[27];
                }
                Step();
                Assert.Greater(Read(textures[0]).r,.9f,"A front must not disappear instantly over a desert.");
                for (int i=0;i<180;i++) Step();
                Assert.Less(Read(textures[0]).r,.01f,"Dry air must exhaust cloud support.");
                Assert.Less(Read(textures[2]).b,.01f,"Rain must stop after humidity is exhausted.");
                compute.SetFloat("_StepAngle",.01f);
                Step();
                Color before = Read(textures[4]);
                compute.SetVector("_WindDirection",Vector3.forward);
                compute.SetFloat("_StepAngle",0);
                Step();
                Color after = Read(textures[4]);
                Assert.Less(Vector3.Distance(new Vector3(before.r,before.g,before.b),
                    new Vector3(after.r,after.g,after.b)),.001f,
                    "Changing wind must not rotate the accumulated cloud pattern instantly.");
            }
            finally
            {
                foreach (var texture in textures)
                    if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                Object.DestroyImmediate(climate); Object.DestroyImmediate(compute);
            }
        }

        [Test]
        public void RainWetsDryAirDriesAndColdSnowMeltsWhenWarm()
        {
            var compute = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Graphics/Shaders/WeatherEvolution.compute"));
            var weather = new Texture2DArray(2, 2, 6, TextureFormat.RGBAFloat, false);
            var climate = new Texture2DArray(2, 2, 6, TextureFormat.RGFloat, false);
            var surface = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBFloat)
            { dimension = TextureDimension.Tex2DArray, volumeDepth = 6, enableRandomWrite = true };
            surface.Create();
            try
            {
                int init = compute.FindKernel("CSInitSurface");
                int evolve = compute.FindKernel("CSSurfaceWeather");
                compute.SetInt("_Resolution", 2);
                compute.SetTexture(init, "_SurfaceWeather", surface);
                compute.Dispatch(init, 1, 1, 6);
                compute.SetTexture(evolve, "_SurfaceWeather", surface);
                compute.SetTexture(evolve, "_WeatherRead", weather);
                compute.SetTexture(evolve, "_DynamicsRead", weather);
                compute.SetTexture(evolve, "_ClimateRead", climate);
                compute.SetInt("_HasClimate", 1);
                compute.SetFloat("_DeltaTime", 1);
                compute.SetFloat("_SurfaceWindSpeed", 10);
                compute.SetVector("_WeatherParticlePhaseParams", new Vector4(0, 2, 0, 0));
                compute.SetVector("_SurfacePrecipitationParams", new Vector4(1, .5f, .2f, 0));

                void Run(float temperature, bool raining, int seconds)
                {
                    compute.SetVector("_ClimateTemperatureRangeCelsius", new Vector4(temperature,temperature,0,0));
                    for (int face = 0; face < 6; face++)
                    {
                        var rain = raining ? Color.white : Color.clear;
                        weather.SetPixels(new[] { rain, rain, rain, rain }, face);
                        climate.SetPixels(new[] { Color.clear, Color.clear, Color.clear, Color.clear }, face);
                    }
                    weather.Apply(); climate.Apply();
                    for (int i = 0; i < seconds; i++) compute.Dispatch(evolve, 1, 1, 6);
                }
                Color Read()
                {
                    var read = AsyncGPUReadback.Request(surface, 0, TextureFormat.RGBAFloat);
                    read.WaitForCompletion();
                    Assert.IsFalse(read.hasError);
                    return read.GetData<Color>()[0];
                }

                Run(25, true, 60);
                Assert.Greater(Read().r, .9f, "Warm rain must wet the surface.");
                Assert.Less(Read().g, .001f, "Warm rain must not accumulate snow.");
                Run(35, false, 400);
                Assert.Less(Read().r, .01f, "Dry warm air must remove wetness after rain ends.");
                Run(-10, true, 150);
                float snow = Read().g;
                Assert.Greater(snow, .5f, "Cold precipitation must accumulate snow.");
                Run(20, false, 200);
                Assert.Less(Read().g, .01f, "Warm air must melt the snow.");
            }
            finally
            {
                surface.Release();
                Object.DestroyImmediate(surface);
                Object.DestroyImmediate(weather);
                Object.DestroyImmediate(climate);
                Object.DestroyImmediate(compute);
            }
        }
    }
}
