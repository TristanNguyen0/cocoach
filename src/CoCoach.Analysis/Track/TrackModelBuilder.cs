using CoCoach.Analysis.Alignment;

namespace CoCoach.Analysis.Track;

/// <summary>Builds a <see cref="TrackModel"/> from lap traces of one track layout.</summary>
public static class TrackModelBuilder
{
    /// <summary>Half a typical car's width, metres: the edge lies at least this far beyond the car's centre.</summary>
    public const double HalfCarWidth = 1.0;

    /// <summary>
    /// iRacing's <c>YawRate</c> sign relative to the ENU convention (positive = counter-clockwise = left turn).
    /// Checked against GPS heading on real recordings (see the real-recording tests).
    /// </summary>
    public const double YawRateSign = 1.0;

    /// <summary>Below this speed, yaw rate over speed is too noisy to be curvature.</summary>
    const double MinCurvatureSpeed = 8.0;

    /// <summary>
    /// Builds the model. <paramref name="cleanLaps"/> are averaged into the centreline;
    /// <paramref name="allLaps"/> (every complete lap, off-track ones included, pit-road ones ignored) feed the edge estimate.
    /// </summary>
    /// <exception cref="InvalidOperationException">No lap carries GPS or yaw rate, so no map can be built.</exception>
    public static TrackModel Build(TrackKey key, IReadOnlyList<LapTrace> cleanLaps, IReadOnlyList<LapTrace>? allLaps = null, int? expectedTurns = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(cleanLaps);
        if (cleanLaps.Count == 0)
            throw new InvalidOperationException("A track model needs at least one clean lap.");

        var gpsLaps = cleanLaps.Where(HasGps).ToArray();
        if (gpsLaps.Length > 0)
            return BuildFromGps(key, gpsLaps, allLaps ?? cleanLaps, expectedTurns);

        var yawLaps = cleanLaps.Where(l => l.YawRate is not null).ToArray();
        if (yawLaps.Length > 0)
            return BuildFromDeadReckoning(key, yawLaps, expectedTurns);

        throw new InvalidOperationException("The laps carry neither GPS (Lat/Lon) nor YawRate, so no track map can be built.");
    }

    /// <summary>A lap has usable GPS when at least 95 % of its grid points have a fix.</summary>
    public static bool HasGps(LapTrace lap)
    {
        ArgumentNullException.ThrowIfNull(lap);
        if (lap.Lat is null || lap.Lon is null)
            return false;
        var finite = 0;
        for (var i = 0; i < lap.Count; i++)
        {
            if (double.IsFinite(lap.Lat[i]) && double.IsFinite(lap.Lon[i]))
                finite++;
        }
        return finite >= 0.95 * lap.Count;
    }

    /// <summary>Per-lap curvature from yaw rate over speed, 1/m (positive = left); NaN at low speed.</summary>
    public static double[]? YawCurvature(LapTrace lap)
    {
        ArgumentNullException.ThrowIfNull(lap);
        if (lap.YawRate is null)
            return null;
        var k = new double[lap.Count];
        for (var i = 0; i < lap.Count; i++)
            k[i] = lap.Speed[i] > MinCurvatureSpeed ? YawRateSign * lap.YawRate[i] / lap.Speed[i] : double.NaN;
        return k;
    }

    static TrackModel BuildFromGps(TrackKey key, LapTrace[] laps, IReadOnlyList<LapTrace> allLaps, int? expectedTurns)
    {
        var first = laps[0];
        var n = first.Count;
        var start = Enumerable.Range(0, n).First(i => double.IsFinite(first.Lat![i]) && double.IsFinite(first.Lon![i]));
        var origin = new GeoOrigin(first.Lat![start], first.Lon![start], first.Alt is { } a && double.IsFinite(a[start]) ? a[start] : 0);

        var sumX = new double[n];
        var sumY = new double[n];
        var sumZ = new double[n];
        var count = new int[n];
        foreach (var lap in laps)
        {
            for (var i = 0; i < n && i < lap.Count; i++)
            {
                if (!double.IsFinite(lap.Lat![i]) || !double.IsFinite(lap.Lon![i]))
                    continue;
                var alt = lap.Alt is { } la && double.IsFinite(la[i]) ? la[i] : origin.Alt;
                var (e, nth, up) = Geodesy.ToEnu(origin, lap.Lat[i], lap.Lon[i], alt);
                sumX[i] += e;
                sumY[i] += nth;
                sumZ[i] += up;
                count[i]++;
            }
        }

        var x = new double[n];
        var y = new double[n];
        var z = new double[n];
        for (var i = 0; i < n; i++)
        {
            x[i] = count[i] > 0 ? sumX[i] / count[i] : double.NaN;
            y[i] = count[i] > 0 ? sumY[i] / count[i] : double.NaN;
            z[i] = count[i] > 0 ? sumZ[i] / count[i] : double.NaN;
        }
        FillGapsCircular(x);
        FillGapsCircular(y);
        FillGapsCircular(z);
        x = Signal.MovingAverage(x, 2, circular: true);
        y = Signal.MovingAverage(y, 2, circular: true);

        var step = first.Step;
        var heading = new double[n];
        var curvature = new double[n];
        for (var i = 0; i < n; i++)
        {
            var prev = Signal.Mod(i - 1, n);
            var next = Signal.Mod(i + 1, n);
            heading[i] = Math.Atan2(y[next] - y[prev], x[next] - x[prev]);
        }
        for (var i = 0; i < n; i++)
        {
            var prev = Signal.Mod(i - 1, n);
            var next = Signal.Mod(i + 1, n);
            curvature[i] = Signal.WrapAngle(heading[next] - heading[prev]) / (2 * step);
        }
        curvature = Signal.Smooth(curvature, 3, circular: true);

        var model = new TrackModel
        {
            Key = key,
            Length = first.Length,
            Step = step,
            Source = TrackMapSource.Gps,
            LapsUsed = laps.Length,
            Origin = origin,
            X = x,
            Y = y,
            Z = z,
            Heading = heading,
            Curvature = curvature,
            ExpectedTurns = expectedTurns,
        };

        var (left, right) = EstimateEdges(model, allLaps);
        return model with { LeftEdge = left, RightEdge = right };
    }

    // the edge lies beyond every on-track position of the car's centre (plus half a car) and inside every off-track one
    static (double[] Left, double[] Right) EstimateEdges(TrackModel model, IReadOnlyList<LapTrace> laps)
    {
        var n = model.Count;
        var onLeft = Filled(n, double.NegativeInfinity);
        var onRight = Filled(n, double.PositiveInfinity);
        var offLeft = Filled(n, double.PositiveInfinity);
        var offRight = Filled(n, double.NegativeInfinity);

        foreach (var lap in laps.Where(l => !l.PitRoad))
        {
            if (model.LateralOffsets(lap) is not { } offsets)
                continue;
            for (var i = 0; i < offsets.Length; i++)
            {
                var o = offsets[i];
                if (!double.IsFinite(o))
                    continue;
                if (IsOffTrack(lap, i))
                {
                    if (o > 0)
                        offLeft[i] = Math.Min(offLeft[i], o);
                    else
                        offRight[i] = Math.Max(offRight[i], o);
                }
                else
                {
                    onLeft[i] = Math.Max(onLeft[i], o);
                    onRight[i] = Math.Min(onRight[i], o);
                }
            }
        }

        var left = new double[n];
        var right = new double[n];
        for (var i = 0; i < n; i++)
        {
            left[i] = Edge(onLeft[i], offLeft[i], +1);
            right[i] = Edge(onRight[i], offRight[i], -1);
        }
        return (Signal.MovingAverage(left, 5, circular: true), Signal.MovingAverage(right, 5, circular: true));
    }

    // at least half a car beyond the car's centre, but never past a position where the car was off track
    static double Edge(double on, double off, int side)
    {
        if (!double.IsFinite(on))
            return double.NaN;
        return side > 0
            ? Math.Max(on, Math.Min(on + HalfCarWidth, off))
            : Math.Min(on, Math.Max(on - HalfCarWidth, off));
    }

    /// <summary>Off track by <c>PlayerTrackSurface</c>, or on grass, dirt, sand, gravel, grasscrete or astroturf.</summary>
    internal static bool IsOffTrack(LapTrace lap, int i) =>
        (lap.TrackSurface is { } surface && surface[i] == 0) ||
        (lap.SurfaceMaterial is { } material && material[i] >= SurfaceMaterials.FirstOffTrack);

    static TrackModel BuildFromDeadReckoning(TrackKey key, LapTrace[] laps, int? expectedTurns)
    {
        var first = laps[0];
        var n = first.Count;
        var step = first.Step;
        var length = first.Length;

        var sum = new double[n];
        var count = new int[n];
        foreach (var lap in laps)
        {
            var k = YawCurvature(lap)!;
            for (var i = 0; i < n && i < k.Length; i++)
            {
                if (double.IsFinite(k[i]))
                {
                    sum[i] += k[i];
                    count[i]++;
                }
            }
        }
        var curvature = new double[n];
        for (var i = 0; i < n; i++)
            curvature[i] = count[i] > 0 ? sum[i] / count[i] : double.NaN;
        FillGapsCircular(curvature);
        curvature = Signal.Smooth(curvature, 3, circular: true);

        // the last grid point is up to one step short of the line
        double Span(int i) => i < n - 1 ? step : length - (n - 1) * step;

        // heading closure: a lap turns through a whole number of revolutions
        double total = 0;
        for (var i = 0; i < n; i++)
            total += curvature[i] * Span(i);
        var target = 2 * Math.PI * Math.Round(total / (2 * Math.PI));
        var correction = (target - total) / length;
        for (var i = 0; i < n; i++)
            curvature[i] += correction;

        var heading = new double[n];
        var x = new double[n];
        var y = new double[n];
        double h = 0, px = 0, py = 0;
        for (var i = 0; i < n; i++)
        {
            heading[i] = h;
            x[i] = px;
            y[i] = py;
            var mid = h + curvature[i] * Span(i) / 2;
            px += Math.Cos(mid) * Span(i);
            py += Math.Sin(mid) * Span(i);
            h += curvature[i] * Span(i);
        }

        // position closure: spread the end-point miss linearly along the lap
        var closure = Math.Sqrt(px * px + py * py);
        for (var i = 0; i < n; i++)
        {
            var f = i * step / length;
            x[i] -= px * f;
            y[i] -= py * f;
        }

        return new TrackModel
        {
            Key = key,
            Length = length,
            Step = step,
            Source = TrackMapSource.DeadReckoning,
            LapsUsed = laps.Length,
            X = x,
            Y = y,
            Heading = heading.Select(Signal.WrapAngle).ToArray(),
            Curvature = curvature,
            ClosureError = closure,
            ExpectedTurns = expectedTurns,
        };
    }

    static double[] Filled(int n, double value)
    {
        var a = new double[n];
        Array.Fill(a, value);
        return a;
    }

    // linear interpolation across NaN runs, wrapping around the lap
    internal static void FillGapsCircular(double[] a)
    {
        var n = a.Length;
        var known = Enumerable.Range(0, n).Where(i => double.IsFinite(a[i])).ToArray();
        if (known.Length == 0)
        {
            Array.Fill(a, 0);
            return;
        }
        for (var k = 0; k < known.Length; k++)
        {
            var i0 = known[k];
            var i1 = known[(k + 1) % known.Length];
            var gap = Signal.Mod(i1 - i0, n);
            if (gap <= 1 && known.Length > 1)
                continue;
            if (known.Length == 1)
                gap = n;
            for (var j = 1; j < gap; j++)
            {
                var idx = (i0 + j) % n;
                a[idx] = a[i0] + (a[i1] - a[i0]) * j / gap;
            }
        }
    }
}

/// <summary><c>irsdk_TrkSurf</c> material ranges.</summary>
public static class SurfaceMaterials
{
    public const int FirstRumble = 11;
    public const int LastRumble = 14;

    /// <summary>Grass1 and every material after it (dirt, sand, gravel, grasscrete, astroturf) is off the racing surface.</summary>
    public const int FirstOffTrack = 15;

    public static bool IsKerb(double material) => material is >= FirstRumble and <= LastRumble;
}
