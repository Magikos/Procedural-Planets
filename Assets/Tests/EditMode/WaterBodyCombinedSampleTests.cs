using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class WaterBodyCombinedSampleTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void CombinedSamplePreservesStateAndFallback(bool solved)
        {
            var map = new WaterBodyMap();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var mask = (byte[])typeof(WaterBodyMap).GetField("_mask", flags).GetValue(map);
            var levels = new float[mask.Length];
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = (byte)(i % 3);
                levels[i] = i % 4 == 0 ? float.NegativeInfinity : i * .001f;
            }
            // Obtain the sentinel from the implementation instead of assuming its representation.
            float noWater = (float)typeof(WaterBodyMap).GetField("NoWater", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
            for (int i = 0; i < levels.Length; i += 4) levels[i] = noWater;
            if (solved) typeof(WaterBodyMap).GetField("_level", flags).SetValue(map, levels);
            int resolution = WaterBodyMap.Resolution;
            for (int face = 0; face < 6; face++)
            for (int y = 0; y <= resolution; y++)
            for (int x = 0; x <= resolution; x++)
            {
                var direction = CoordinateConverter.CubeFaceToUnitSphere(face, new Vector2((float)x / resolution, (float)y / resolution));
                const float fallback = -.137f;
                map.SampleStateAndLevel(direction, fallback, out byte state, out float level);
                Assert.That(state, Is.EqualTo(map.Sample(direction)));
                Assert.That(level, Is.EqualTo(map.LevelAt(direction, fallback)));
            }
        }
    }
}
