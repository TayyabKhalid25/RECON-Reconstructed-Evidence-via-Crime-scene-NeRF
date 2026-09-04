using System;
using System.Collections.Generic;

namespace Recon.Diagnostics.Core
{
    /// <summary>
    /// Summary statistics for a run of per-second samples. Pure C# so the numbers that go into
    /// docs/RESULTS.md are computed by tested code, not by reading a counter off a screen.
    /// </summary>
    public readonly struct PerfSummary
    {
        public PerfSummary(int count, double min, double p5, double median, double mean, double p95, double max)
        {
            Count = count; Min = min; P5 = p5; Median = median; Mean = mean; P95 = p95; Max = max;
        }

        public int Count { get; }
        public double Min { get; }
        public double P5 { get; }
        public double Median { get; }
        public double Mean { get; }
        public double P95 { get; }
        public double Max { get; }

        public override string ToString() =>
            $"n={Count} min={Min:F1} p5={P5:F1} median={Median:F1} mean={Mean:F1} p95={P95:F1} max={Max:F1}";
    }

    public static class PerfStats
    {
        /// <summary>
        /// Percentile with linear interpolation between order statistics (the "R-7" definition
        /// NumPy and Excel use), so a p5 over 60 samples is not just the 3rd or 4th value.
        /// </summary>
        public static double Percentile(IReadOnlyList<double> sortedAscending, double percentile)
        {
            if (sortedAscending == null || sortedAscending.Count == 0)
                throw new ArgumentException("Percentile of an empty sample is undefined.");
            if (percentile < 0 || percentile > 100)
                throw new ArgumentOutOfRangeException(nameof(percentile), "0..100");
            if (sortedAscending.Count == 1) return sortedAscending[0];

            double rank = percentile / 100.0 * (sortedAscending.Count - 1);
            int lo = (int)Math.Floor(rank);
            int hi = (int)Math.Ceiling(rank);
            if (lo == hi) return sortedAscending[lo];
            double frac = rank - lo;
            return sortedAscending[lo] + (sortedAscending[hi] - sortedAscending[lo]) * frac;
        }

        public static double Median(IReadOnlyList<double> sortedAscending) => Percentile(sortedAscending, 50);

        public static PerfSummary Summarize(IEnumerable<double> samples)
        {
            var sorted = new List<double>(samples ?? throw new ArgumentNullException(nameof(samples)));
            if (sorted.Count == 0) throw new ArgumentException("Cannot summarise zero samples.");
            sorted.Sort();
            double sum = 0;
            foreach (var s in sorted) sum += s;
            return new PerfSummary(
                sorted.Count,
                sorted[0],
                Percentile(sorted, 5),
                Percentile(sorted, 50),
                sum / sorted.Count,
                Percentile(sorted, 95),
                sorted[sorted.Count - 1]);
        }
    }
}
