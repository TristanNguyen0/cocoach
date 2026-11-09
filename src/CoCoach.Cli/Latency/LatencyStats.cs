using System.Globalization;

namespace CoCoach.Cli.Latency;

/// <summary>
/// One click, with Stopwatch timestamps for each stage from telemetry arrival to the first click
/// sample reaching the audio device's play position.
/// </summary>
/// <param name="Arrival">The telemetry row was copied out of the SDK.</param>
/// <param name="Detected">The pipeline saw the trigger (for example Brake crossing 0.5) and asked for a click.</param>
/// <param name="Written">WASAPI pulled the buffer that starts with the click.</param>
/// <param name="Audible">The device clock's play position reached the click's first sample.</param>
internal readonly record struct LatencySample(long Arrival, long Detected, long Written, long Audible)
{
    public double PipelineMs => Ms(Arrival, Detected);
    public double BufferWaitMs => Ms(Detected, Written);
    public double DeviceQueueMs => Ms(Written, Audible);
    public double TotalMs => Ms(Arrival, Audible);

    static double Ms(long from, long to) => (to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
}

/// <summary>Summary statistics over a set of millisecond measurements.</summary>
internal sealed record LatencyStats(int Count, double Min, double Mean, double P50, double P95, double P99, double Max)
{
    public static LatencyStats? From(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Where(double.IsFinite).Order().ToArray();
        if (sorted.Length == 0)
            return null;

        return new LatencyStats(
            sorted.Length,
            sorted[0],
            sorted.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1]);
    }

    // linear interpolation between closest ranks
    internal static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1)
            return sorted[0];
        var rank = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"n={Count,3}  min {Min,6:0.0}  mean {Mean,6:0.0}  p50 {P50,6:0.0}  p95 {P95,6:0.0}  p99 {P99,6:0.0}  max {Max,6:0.0} ms");
}
