using System.Globalization;
using CoCoach.Analysis.Recording;
using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Laps;

namespace CoCoach.Analysis.Alignment;

/// <summary>Resamples complete laps onto a fixed <c>LapDist</c> grid (1 m by default).</summary>
public static class LapTraceBuilder
{
    /// <summary>Default grid spacing, metres.</summary>
    public const double DefaultStep = 1.0;

    /// <summary>
    /// Track length in metres as iRacing measures <c>LapDist</c>: the median ratio of <c>LapDist</c> to
    /// <c>LapDistPct</c>, which is exact, falling back to WeekendInfo.TrackLength (rounded by iRacing).
    /// </summary>
    public static double? EstimateTrackLength(RecordedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var ratios = new List<double>();
        foreach (var f in session.Frames)
        {
            if (f.LapDist is { } dist && f.LapDistPct is { } pct && pct is > 0.2f and < 0.98f && dist > 0 && f.IsOnTrack != false)
                ratios.Add(dist / pct);
            if (ratios.Count >= 20_000)
                break;
        }
        if (ratios.Count >= 10)
            return Signal.Median(ratios);
        return ParseTrackLength(session.Facts?.TrackLength);
    }

    /// <summary>Parses iRacing's "4.28 km" (or "2.66 mi") into metres.</summary>
    public static double? ParseTrackLength(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
            return null;
        var unit = parts.Length > 1 ? parts[1].ToLowerInvariant() : "km";
        return unit switch
        {
            "km" => value * 1000,
            "mi" => value * 1609.344,
            "m" => value,
            _ => null,
        };
    }

    /// <summary>Every complete lap of <paramref name="session"/> as a trace on the shared grid.</summary>
    /// <param name="session">The session.</param>
    /// <param name="sessionIndex">Position of the session in the analysed set (goes into <see cref="LapId"/>).</param>
    /// <param name="trackLength">Track length, metres; use the same value for every session of a track.</param>
    /// <param name="step">Grid spacing, metres.</param>
    public static IReadOnlyList<LapTrace> Build(RecordedSession session, int sessionIndex, double trackLength, double step = DefaultStep)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(trackLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        var traces = new List<LapTrace>();
        foreach (var lap in session.Laps)
        {
            if (!lap.IsComplete || lap.StartTime is null || lap.LapTime is null || lap.FirstFrameIndex < 0)
                continue;
            var trace = BuildLap(session, sessionIndex, lap, trackLength, step);
            if (trace is not null)
                traces.Add(trace);
        }
        return traces;
    }

    static LapTrace? BuildLap(RecordedSession session, int sessionIndex, LapSummary lap, double length, double step)
    {
        // distance along the lap, unwrapped: the frame before the line is just below 0, the one after just above length
        var samples = new List<(double U, TelemetryFrame F)>();
        var offTrack = false;
        foreach (var f in session.FramesAround(lap))
        {
            if (f.SessionTime is null)
                continue;
            var wrap = f.Index < lap.FirstFrameIndex ? -1 : f.Index > lap.LastFrameIndex ? 1 : 0;
            double? u = f.LapDistPct is { } pct ? (Math.Max(0.0, pct) + wrap) * length
                : f.LapDist is { } dist ? Math.Max(0.0, dist) + wrap * length
                : null;
            if (u is null)
                continue;
            if (wrap == 0 && f.PlayerTrackSurface == 0)
                offTrack = true;
            // the car can stop or roll back after a spin; keep distance strictly increasing
            if (samples.Count > 0 && u.Value <= samples[^1].U + 1e-6)
                continue;
            samples.Add((u.Value, f));
        }

        if (samples.Count < 4 || samples[0].U > 0 || samples[^1].U < length)
            return null;

        var n = (int)Math.Floor(length / step + 1e-9) + 1;
        var time = new double[n];
        var startTime = Interpolate(samples, 0, f => f.SessionTime);
        var endTime = Interpolate(samples, length, f => f.SessionTime);
        var lapTime = endTime - startTime;
        if (!double.IsFinite(lapTime) || lapTime <= 0)
            return null;

        var grid = new double[n];
        for (var i = 0; i < n; i++)
            grid[i] = i * step;

        for (var i = 0; i < n; i++)
            time[i] = Interpolate(samples, grid[i], f => f.SessionTime) - startTime;

        var speed = Channel(samples, grid, f => f.Speed, discrete: false);
        if (speed is null)
            return null;

        return new LapTrace
        {
            Id = new LapId(sessionIndex, session.Label, lap.Lap),
            LapTime = lapTime,
            ReportedLapTime = lap.ReportedLapTime,
            IracingBestLapTime = IracingBest(samples, length),
            Length = length,
            Step = step,
            Time = time,
            Speed = speed,
            Throttle = Channel(samples, grid, f => f.Throttle, discrete: false),
            Brake = Channel(samples, grid, f => f.Brake, discrete: false),
            Steering = Channel(samples, grid, f => f.SteeringWheelAngle, discrete: false),
            YawRate = Channel(samples, grid, f => f.YawRate, discrete: false),
            Lat = Channel(samples, grid, f => f.Lat is null ? null : ValidGps(f) ? f.Lat : double.NaN, discrete: false),
            Lon = Channel(samples, grid, f => f.Lon is null ? null : ValidGps(f) ? f.Lon : double.NaN, discrete: false),
            Alt = Channel(samples, grid, f => f.Alt is null ? null : ValidGps(f) ? f.Alt : double.NaN, discrete: false),
            TrackSurface = Channel(samples, grid, f => f.PlayerTrackSurface, discrete: true),
            SurfaceMaterial = Channel(samples, grid, f => f.PlayerTrackSurfaceMaterial, discrete: true),
            IracingDeltaToBest = Channel(samples, grid, f => f.LapDeltaToBestLapOk == false ? double.NaN : f.LapDeltaToBestLap, discrete: false),
            IracingDeltaToOptimal = Channel(samples, grid, f => f.LapDeltaToOptimalLapOk == false ? double.NaN : f.LapDeltaToOptimalLap, discrete: false),
            PitRoad = lap.PitRoad,
            OffTrack = offTrack,
        };
    }

    // the best lap time iRacing shows mid-lap; it changes only at a line crossing
    static double? IracingBest(List<(double U, TelemetryFrame F)> samples, double length)
    {
        var mid = samples.FirstOrDefault(s => s.U >= length / 2).F;
        return mid?.LapBestLapTime is { } best && best > 0 ? best : null;
    }

    // GPS reads 0/0 when the sim has no world position for the car
    static bool ValidGps(TelemetryFrame f) =>
        f.Lat is { } lat && f.Lon is { } lon && (lat != 0 || lon != 0) && double.IsFinite(lat) && double.IsFinite(lon);

    static double Interpolate(List<(double U, TelemetryFrame F)> samples, double u, Func<TelemetryFrame, double?> value)
    {
        var k = Bracket(samples, u);
        var (u0, f0) = samples[k];
        var (u1, f1) = samples[k + 1];
        var v0 = value(f0) ?? double.NaN;
        var v1 = value(f1) ?? double.NaN;
        return v0 + (v1 - v0) * (u - u0) / (u1 - u0);
    }

    // index k with samples[k].U <= u <= samples[k + 1].U
    static int Bracket(List<(double U, TelemetryFrame F)> samples, double u)
    {
        int lo = 0, hi = samples.Count - 2;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (samples[mid].U <= u)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    static double[]? Channel(List<(double U, TelemetryFrame F)> samples, double[] grid, Func<TelemetryFrame, double?> value, bool discrete)
    {
        var any = false;
        foreach (var (_, f) in samples)
        {
            if (value(f) is not null)
            {
                any = true;
                break;
            }
        }
        if (!any)
            return null;

        var result = new double[grid.Length];
        var k = 0;
        for (var i = 0; i < grid.Length; i++)
        {
            var u = grid[i];
            while (k < samples.Count - 2 && samples[k + 1].U <= u)
                k++;
            var v0 = value(samples[k].F) ?? double.NaN;
            if (discrete)
            {
                result[i] = v0;
                continue;
            }
            var v1 = value(samples[k + 1].F) ?? double.NaN;
            var frac = (u - samples[k].U) / (samples[k + 1].U - samples[k].U);
            result[i] = v0 + (v1 - v0) * frac;
        }
        return result;
    }
}
