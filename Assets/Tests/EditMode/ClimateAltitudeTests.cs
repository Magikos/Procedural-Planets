using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ClimateAltitudeTests
    {
        [TestCase(0.4f, 0.05f, 0.275f)]
        [TestCase(0.9f, 0.05f, 0.775f)]
        [TestCase(0.4f, -0.05f, 0.4f)]
        public void AltitudeCoolsLandWithoutMakingEverySummitCold(
            float baseTemperature, float elevation, float expected)
        {
            var asset = ScriptableObject.CreateInstance<BiomeSettings>();
            try
            {
                asset.TemperatureLatitudeCurve = AnimationCurve.Linear(0f, baseTemperature, 1f, baseTemperature);
                asset.TemperatureNoiseStrength = 0f;
                var climate = new ClimateProvider(BiomeDto.From(asset));
                climate.Initialize(1691104419);
                Assert.That(climate.Evaluate(Vector3.up, elevation).Temperature01,
                    Is.EqualTo(expected).Within(1e-5f));
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
