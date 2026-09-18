using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace ProceduralPlanets.Tests
{
    public sealed class FrameTimingSectionTests
    {
        [TestCase(1500d)]
        [TestCase(60000d)]
        public void WholeFrameTimingRetainsSevereStalls(double milliseconds)
        {
            var sanitize = typeof(FrameTimingCounters).GetMethod("SanitizeWholeFrameMs",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(sanitize.Invoke(null, new object[] { milliseconds }), Is.EqualTo(milliseconds));
        }

        [Test]
        public void EverySectionRecordsReportsAndResetsWithoutOverlap()
        {
            FrameTimingCounters.Reset();
            try
            {
                var sections = (FrameTimingSection[])Enum.GetValues(typeof(FrameTimingSection));
                const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
                var ticks = (long[])typeof(FrameTimingCounters).GetField("_lastTicks", flags).GetValue(null);
                Assert.That(ticks.Length, Is.EqualTo(sections.Length));
                // Establish this frame before injecting completed-frame data.
                FrameTimingCounters.GetSectionMs(sections[0]);
                for (int i = 0; i < sections.Length; i++)
                {
                    Assert.That((int)sections[i], Is.EqualTo(i));
                    ticks[i] = Stopwatch.Frequency * (i + 1) / 1000;
                }
                typeof(FrameTimingCounters).GetMethod("RecordCompletedFrame", flags).Invoke(null, new object[] { 100d, 10d });
                var metadata = new StringBuilder();
                new FrameTimingModule().AppendMetadata(default, metadata);
                for (int i = 0; i < sections.Length; i++)
                {
                    var stats = FrameTimingCounters.GetSectionStats(sections[i]);
                    Assert.That(stats.SampleCount, Is.EqualTo(1));
                    Assert.That(stats.AverageMs, Is.EqualTo(i + 1).Within(.001));
                    StringAssert.Contains(sections[i] + " CPU:", metadata.ToString());
                }
                FrameTimingCounters.Reset();
                foreach (var section in sections)
                {
                    Assert.That(FrameTimingCounters.GetSectionMs(section), Is.Zero);
                    Assert.That(FrameTimingCounters.GetSectionStats(section).SampleCount, Is.Zero);
                }
            }
            finally { FrameTimingCounters.Reset(); }
        }
    }
}
