using System;
using System.Collections.Generic;
using NUnit.Framework;
using Recon.Diagnostics.Core;

namespace Recon.Tests.Core
{
    public class PerfStatsTests
    {
        [Test]
        public void Summarize_KnownSamples()
        {
            var s = PerfStats.Summarize(new double[] { 30, 28, 31, 29, 60, 27, 30, 30, 29, 31 });
            Assert.That(s.Count, Is.EqualTo(10));
            Assert.That(s.Min, Is.EqualTo(27));
            Assert.That(s.Max, Is.EqualTo(60));
            Assert.That(s.Median, Is.EqualTo(30).Within(1e-9));
            Assert.That(s.Mean, Is.EqualTo(32.5).Within(1e-9));
            // sorted: 27 28 29 29 30 30 30 31 31 60 ; rank(5%) = 0.45 → 27 + 0.45*(28-27)
            Assert.That(s.P5, Is.EqualTo(27.45).Within(1e-9));
            // rank(95%) = 8.55 → 31 + 0.55*(60-31)
            Assert.That(s.P95, Is.EqualTo(46.95).Within(1e-9));
        }

        [Test]
        public void Percentile_SingleSample_IsThatSample()
        {
            Assert.That(PerfStats.Percentile(new[] { 42.0 }, 5), Is.EqualTo(42));
            Assert.That(PerfStats.Percentile(new[] { 42.0 }, 95), Is.EqualTo(42));
        }

        [Test]
        public void Percentile_Endpoints()
        {
            var sorted = new double[] { 1, 2, 3, 4 };
            Assert.That(PerfStats.Percentile(sorted, 0), Is.EqualTo(1));
            Assert.That(PerfStats.Percentile(sorted, 100), Is.EqualTo(4));
            Assert.That(PerfStats.Median(sorted), Is.EqualTo(2.5).Within(1e-9));
        }

        [Test]
        public void Empty_Throws()
        {
            Assert.Throws<ArgumentException>(() => PerfStats.Summarize(new List<double>()));
            Assert.Throws<ArgumentException>(() => PerfStats.Percentile(new double[0], 50));
        }

        [Test]
        public void BadPercentile_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PerfStats.Percentile(new[] { 1.0, 2.0 }, 101));
        }

        [Test]
        public void Summarize_DoesNotMutateInput()
        {
            var input = new List<double> { 3, 1, 2 };
            PerfStats.Summarize(input);
            Assert.That(input, Is.EqualTo(new List<double> { 3, 1, 2 }));
        }
    }
}
