using CoCoach.Analysis.Alignment;
using CoCoach.Analysis.Corners;

namespace CoCoach.Analysis.References;

public enum ReferenceKind
{
    /// <summary>The driver's fastest valid lap.</summary>
    Best,

    /// <summary>The best time through every corner segment, stitched into one lap.</summary>
    Optimal,
}

/// <summary>Which lap one segment of a composite came from.</summary>
/// <param name="Corner">The corner the segment holds.</param>
/// <param name="From">Segment start, metres.</param>
/// <param name="To">Segment end, metres.</param>
/// <param name="Lap">The lap with the best time through the segment.</param>
/// <param name="Time">That lap's time through the segment, seconds.</param>
public sealed record SegmentChoice(Corner? Corner, double From, double To, LapId Lap, double Time);

/// <summary>A reference to compare laps against: a recorded lap or an optimal composite.</summary>
/// <param name="Kind">Best lap or optimal composite.</param>
/// <param name="Trace">The reference as a trace on the shared grid.</param>
/// <param name="Segments">For a composite, where each segment came from; for the best lap, one segment covering the lap.</param>
public sealed record ReferenceLap(ReferenceKind Kind, LapTrace Trace, IReadOnlyList<SegmentChoice> Segments)
{
    public double LapTime => Trace.LapTime;
}

/// <summary>Builds the best-lap and optimal-composite references from one or more sessions of the same car and track.</summary>
public static class ReferenceBuilder
{
    /// <summary>The fastest valid lap (no pit road, no off-track), or null when there is none.</summary>
    public static ReferenceLap? Best(IReadOnlyList<LapTrace> laps)
    {
        ArgumentNullException.ThrowIfNull(laps);
        var best = laps.Where(l => l.IsValid).MinBy(l => l.LapTime);
        if (best is null)
            return null;
        return new ReferenceLap(ReferenceKind.Best, best, [new SegmentChoice(null, 0, best.Length, best.Id, best.LapTime)]);
    }

    /// <summary>
    /// The optimal composite: for every corner segment, the valid lap that was fastest through it.
    /// Its lap time is the sum of the best segment times, so it is never slower than the best lap.
    /// </summary>
    public static ReferenceLap? Optimal(IReadOnlyList<LapTrace> laps, IReadOnlyList<Corner> corners)
    {
        ArgumentNullException.ThrowIfNull(laps);
        ArgumentNullException.ThrowIfNull(corners);
        var valid = laps.Where(l => l.IsValid).ToArray();
        if (valid.Length == 0)
            return null;
        if (corners.Count == 0)
            return Best(valid) is { } best ? best with { Kind = ReferenceKind.Optimal } : null;

        var first = valid[0];
        var step = first.Step;
        var n = first.Count;

        var bounds = new List<(Corner? Corner, double From, double To)>();
        foreach (var c in corners)
            bounds.Add((c, Snap(c.SegmentStart, step), c.Number == corners[^1].Number ? first.Length : Snap(c.SegmentEnd, step)));
        bounds[0] = (bounds[0].Corner, 0, bounds[0].To);

        var choices = new List<SegmentChoice>();
        foreach (var (corner, from, to) in bounds)
        {
            var bestLap = valid.MinBy(l => l.TimeBetween(from, to))!;
            choices.Add(new SegmentChoice(corner, from, to, bestLap.Id, bestLap.TimeBetween(from, to)));
        }

        var byId = valid.ToDictionary(l => l.Id);
        var time = new double[n];
        var channels = new Composite(n, valid);
        // segment offsets: the composite's elapsed time at each segment start
        var offsets = new double[choices.Count];
        for (var k = 1; k < choices.Count; k++)
            offsets[k] = offsets[k - 1] + choices[k - 1].Time;
        var segment = 0;
        for (var i = 0; i < n; i++)
        {
            var d = first.DistanceAt(i);
            while (segment + 1 < choices.Count && d >= choices[segment].To)
                segment++;
            var choice = choices[segment];
            var lap = byId[choice.Lap];
            time[i] = offsets[segment] + (lap.Time[i] - lap.ElapsedAt(choice.From));
            channels.Copy(lap, i);
        }
        var offset = offsets[^1] + choices[^1].Time;

        var trace = new LapTrace
        {
            Id = new LapId(-1, "optimal", 0),
            LapTime = offset,
            Length = first.Length,
            Step = step,
            Time = time,
            Speed = channels.Speed,
            Throttle = channels.Throttle,
            Brake = channels.Brake,
            Steering = channels.Steering,
            YawRate = channels.YawRate,
            LongAccel = channels.LongAccel,
            LatAccel = channels.LatAccel,
            Lat = channels.Lat,
            Lon = channels.Lon,
            Alt = channels.Alt,
            TrackSurface = channels.TrackSurface,
            SurfaceMaterial = channels.SurfaceMaterial,
            Gear = channels.Gear,
            ComposedFrom = choices.Select(c => c.Lap).Distinct().ToArray(),
        };
        return new ReferenceLap(ReferenceKind.Optimal, trace, choices);
    }

    static double Snap(double distance, double step) => Math.Round(distance / step) * step;

    // per-channel arrays of the composite; a channel exists when every source lap has it
    sealed class Composite(int n, IReadOnlyList<LapTrace> laps)
    {
        public double[] Speed { get; } = new double[n];
        public double[]? Throttle { get; } = Make(n, laps, l => l.Throttle);
        public double[]? Brake { get; } = Make(n, laps, l => l.Brake);
        public double[]? Steering { get; } = Make(n, laps, l => l.Steering);
        public double[]? YawRate { get; } = Make(n, laps, l => l.YawRate);
        public double[]? LongAccel { get; } = Make(n, laps, l => l.LongAccel);
        public double[]? LatAccel { get; } = Make(n, laps, l => l.LatAccel);
        public double[]? Lat { get; } = Make(n, laps, l => l.Lat);
        public double[]? Lon { get; } = Make(n, laps, l => l.Lon);
        public double[]? Alt { get; } = Make(n, laps, l => l.Alt);
        public double[]? TrackSurface { get; } = Make(n, laps, l => l.TrackSurface);
        public double[]? SurfaceMaterial { get; } = Make(n, laps, l => l.SurfaceMaterial);
        public double[]? Gear { get; } = Make(n, laps, l => l.Gear);

        static double[]? Make(int n, IReadOnlyList<LapTrace> laps, Func<LapTrace, double[]?> channel) =>
            laps.All(l => channel(l) is not null) ? new double[n] : null;

        public void Copy(LapTrace lap, int i)
        {
            Speed[i] = lap.Speed[i];
            Set(Throttle, lap.Throttle, i);
            Set(Brake, lap.Brake, i);
            Set(Steering, lap.Steering, i);
            Set(YawRate, lap.YawRate, i);
            Set(LongAccel, lap.LongAccel, i);
            Set(LatAccel, lap.LatAccel, i);
            Set(Lat, lap.Lat, i);
            Set(Lon, lap.Lon, i);
            Set(Alt, lap.Alt, i);
            Set(TrackSurface, lap.TrackSurface, i);
            Set(SurfaceMaterial, lap.SurfaceMaterial, i);
            Set(Gear, lap.Gear, i);
        }

        static void Set(double[]? target, double[]? source, int i)
        {
            if (target is not null && source is not null)
                target[i] = source[i];
        }
    }
}
