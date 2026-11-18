using CoCoach.Analysis.Track;
using CoCoach.Telemetry.Ibt;
using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Session;

namespace CoCoach.Tests.Support;

/// <summary>One arc of the synthetic circuit: a corner with a known position, radius and direction.</summary>
internal sealed record CircuitArc(int Number, double Start, double End, double Radius, int Sign, double Degrees)
{
    public double Mid => (Start + End) / 2;
}

/// <summary>How one generated lap is driven, corner by corner (1-based corner numbers).</summary>
internal sealed record LapStyle
{
    /// <summary>Corner speed as a fraction of the grip limit, per corner (default 1).</summary>
    public IReadOnlyDictionary<int, double> CornerSpeed { get; init; } = new Dictionary<int, double>();

    /// <summary>Braking deceleration, m/s², per corner (default <see cref="SyntheticCircuit.BrakeDecel"/>).</summary>
    public IReadOnlyDictionary<int, double> Decel { get; init; } = new Dictionary<int, double>();

    /// <summary>Lateral offset at the middle of a corner, metres (positive = left), faded in and out across the arc.</summary>
    public IReadOnlyDictionary<int, double> ApexOffset { get; init; } = new Dictionary<int, double>();

    /// <summary>Corners where the car runs over a kerb for 20 m around the middle of the arc.</summary>
    public IReadOnlySet<int> Kerbs { get; init; } = new HashSet<int>();

    /// <summary>Corner where the car leaves the track (<c>PlayerTrackSurface == 0</c>) for 15 m.</summary>
    public int? OffTrackAt { get; init; }

    public static LapStyle Clean { get; } = new();
}

/// <summary>Ground truth for one generated lap.</summary>
internal sealed class GeneratedLap
{
    public required LapStyle Style { get; init; }

    /// <summary>Profiles on the <see cref="SyntheticCircuit.Resolution"/> grid over one lap.</summary>
    public required double[] Speed { get; init; }

    public required double[] Time { get; init; }
    public required double[] Throttle { get; init; }
    public required double[] Brake { get; init; }

    public double LapTime => Time[^1];

    /// <summary>Distance where braking starts for each corner (null when the corner needs no braking).</summary>
    public required IReadOnlyDictionary<int, double?> BrakeStart { get; init; }

    /// <summary>Lowest speed in each corner's arc.</summary>
    public required IReadOnlyDictionary<int, double> MinSpeed { get; init; }

    public double TimeAt(double s) => SyntheticCircuit.Lerp(Time, s);

    public double SpeedAt(double s) => SyntheticCircuit.Lerp(Speed, s);
}

/// <summary>
/// A closed synthetic circuit of straights and arcs with eight known corners, two of them esses
/// (left-right pairs with no straight between), and a deterministic driver model on it: speed limited by
/// lateral grip in the arcs, constant acceleration and braking, step inputs. Every lap's exact distance-time
/// profile is known, so alignment, delta, segmentation and metric tests can check against the truth.
/// </summary>
internal sealed class SyntheticCircuit
{
    public const double Resolution = 0.1;  // metres
    public const double TopSpeed = 60;     // m/s
    public const double LateralGrip = 12;  // m/s²
    public const double Accel = 5;         // m/s²
    public const double BrakeDecel = 10;   // m/s²
    public const int TickRate = 60;
    public static readonly GeoOrigin Origin = new(47.2197, 14.7647, 680);

    readonly List<(double Length, double Curvature)> _segments = [];
    readonly double[] _x;
    readonly double[] _y;
    readonly double[] _h;
    readonly double[] _k;

    public SyntheticCircuit()
    {
        var arcs = new List<(int Segment, double Radius, double Degrees, int Sign)>();
        void Straight(double length) => _segments.Add((length, 0));
        void Arc(double radius, double degrees, int sign)
        {
            arcs.Add((_segments.Count, radius, degrees, sign));
            _segments.Add((radius * degrees * Math.PI / 180, sign / radius));
        }

        // heading east from the middle of the main straight; all corners but the ess right-handers turn left
        Straight(200);
        Arc(60, 40, +1);
        Arc(60, 40, -1);
        Straight(200);
        Arc(40, 90, +1);
        Straight(300);
        Arc(25, 90, +1);
        Straight(450);
        Arc(60, 40, +1);
        Arc(60, 40, -1);
        Straight(200);
        Arc(40, 90, +1);
        var southIndex = _segments.Count;
        Straight(0);
        Arc(60, 90, +1);
        Straight(0);

        // close the loop: the southbound straight cancels y, the final eastbound straight cancels x
        var (x0, y0, _) = Integrate(_segments);
        _segments[southIndex] = (y0, 0);
        var (x1, _, _) = Integrate(_segments);
        _segments[^1] = (-x1, 0);
        if (y0 <= 0 || x1 >= 0)
            throw new InvalidOperationException("synthetic circuit does not close");

        Length = _segments.Sum(s => s.Length);
        Arcs = arcs
            .Select((a, i) =>
            {
                var start = _segments.Take(a.Segment).Sum(s => s.Length);
                return new CircuitArc(i + 1, start, start + _segments[a.Segment].Length, a.Radius, a.Sign, a.Degrees);
            })
            .ToArray();

        var n = (int)Math.Round(Length / Resolution) + 1;
        _x = new double[n];
        _y = new double[n];
        _h = new double[n];
        _k = new double[n];
        double x = 0, y = 0, h = 0;
        var seg = 0;
        var segStart = 0.0;
        for (var i = 0; i < n; i++)
        {
            var s = i * Resolution;
            while (seg < _segments.Count - 1 && s >= segStart + _segments[seg].Length)
            {
                segStart += _segments[seg].Length;
                seg++;
            }
            _x[i] = x;
            _y[i] = y;
            _h[i] = h;
            _k[i] = _segments[seg].Curvature;
            var mid = h + _k[i] * Resolution / 2;
            x += Math.Cos(mid) * Resolution;
            y += Math.Sin(mid) * Resolution;
            h += _k[i] * Resolution;
        }
    }

    public double Length { get; }

    public IReadOnlyList<CircuitArc> Arcs { get; }

    static (double X, double Y, double H) Integrate(List<(double Length, double Curvature)> segments)
    {
        double x = 0, y = 0, h = 0;
        foreach (var (length, k) in segments)
        {
            if (k == 0)
            {
                x += Math.Cos(h) * length;
                y += Math.Sin(h) * length;
            }
            else
            {
                var r = 1 / k;
                var h1 = h + k * length;
                x += r * (Math.Sin(h1) - Math.Sin(h));
                y += r * (Math.Cos(h) - Math.Cos(h1));
                h = h1;
            }
        }
        return (x, y, h);
    }

    public static double Lerp(double[] a, double s)
    {
        var f = s / Resolution;
        var i = Math.Clamp((int)Math.Floor(f), 0, a.Length - 2);
        var t = f - i;
        return a[i] + (a[i + 1] - a[i]) * t;
    }

    int Index(double s) => Math.Clamp((int)Math.Round(s / Resolution), 0, _x.Length - 1);

    public double CurvatureAt(double s) => _k[Index(s)];

    public CircuitArc? ArcAt(double s) => Arcs.FirstOrDefault(a => s >= a.Start && s < a.End);

    /// <summary>Drives one lap in <paramref name="style"/> and returns its exact profile.</summary>
    public GeneratedLap Drive(LapStyle style)
    {
        var n = _x.Length;
        var limit = new double[n];
        var decel = new double[n];
        for (var i = 0; i < n; i++)
        {
            var s = i * Resolution;
            limit[i] = TopSpeed;
            decel[i] = BrakeDecel;
            if (ArcAt(s) is { } arc)
                limit[i] = Math.Min(TopSpeed, Math.Sqrt(LateralGrip * arc.Radius) * style.CornerSpeed.GetValueOrDefault(arc.Number, 1.0));
        }
        // the deceleration that applies on the approach to each corner
        foreach (var arc in Arcs)
        {
            var d = style.Decel.GetValueOrDefault(arc.Number, BrakeDecel);
            var from = arc.Number > 1 ? Index(Arcs[arc.Number - 2].End) : 0;
            for (var i = from; i <= Index(arc.Start); i++)
                decel[i] = d;
        }

        // a flying lap: it starts at the speed it finishes with
        var forward = new double[n];
        var speed = new double[n];
        var braking = new bool[n];
        var v0 = TopSpeed;
        for (var pass = 0; pass < 3; pass++)
        {
            forward[0] = v0;
            for (var i = 1; i < n; i++)
                forward[i] = Math.Min(limit[i], Math.Sqrt(forward[i - 1] * forward[i - 1] + 2 * Accel * Resolution));
            speed[n - 1] = forward[n - 1];
            for (var i = n - 2; i >= 0; i--)
            {
                var back = Math.Sqrt(speed[i + 1] * speed[i + 1] + 2 * decel[i] * Resolution);
                speed[i] = Math.Min(forward[i], back);
                braking[i] = back < forward[i] - 1e-9;
            }
            v0 = speed[n - 1];
        }

        var throttle = new double[n];
        var brake = new double[n];
        for (var i = 0; i < n; i++)
        {
            if (braking[i])
            {
                brake[i] = 0.8;
                continue;
            }
            var accelerating = i + 1 < n && speed[i + 1] > speed[i] + 1e-9;
            throttle[i] = accelerating || speed[i] >= TopSpeed - 1e-9 ? 1.0 : 0.3;
        }

        var time = new double[n];
        for (var i = 1; i < n; i++)
            time[i] = time[i - 1] + 2 * Resolution / (speed[i - 1] + speed[i]);

        var brakeStart = new Dictionary<int, double?>();
        var minSpeed = new Dictionary<int, double>();
        foreach (var arc in Arcs)
        {
            var from = arc.Number > 1 ? Index(Arcs[arc.Number - 2].End) : 0;
            double? start = null;
            for (var i = Index(arc.Start); i > from; i--)
            {
                if (brake[i] > 0 && brake[i - 1] == 0)
                {
                    start = i * Resolution;
                    break;
                }
            }
            brakeStart[arc.Number] = start;
            var min = double.MaxValue;
            for (var i = Index(arc.Start); i <= Index(arc.End); i++)
                min = Math.Min(min, speed[i]);
            minSpeed[arc.Number] = min;
        }

        return new GeneratedLap
        {
            Style = style,
            Speed = speed,
            Time = time,
            Throttle = throttle,
            Brake = brake,
            BrakeStart = brakeStart,
            MinSpeed = minSpeed,
        };
    }

    /// <summary>Lateral offset of the car from the centreline, metres (positive = left).</summary>
    public double OffsetAt(LapStyle style, double s)
    {
        if (ArcAt(s) is not { } arc || !style.ApexOffset.TryGetValue(arc.Number, out var amount))
            return 0;
        var phase = (s - arc.Start) / (arc.End - arc.Start);
        return amount * 0.5 * (1 - Math.Cos(2 * Math.PI * phase));
    }

    public SessionFacts Facts(int trackId = 999, string config = "Test") => new()
    {
        TrackId = trackId,
        TrackName = "synthetic_circuit",
        TrackDisplayName = "Synthetic Circuit",
        TrackConfigName = config,
        TrackLength = (Length / 1000).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " km",
        TrackNumTurns = Arcs.Count,
        PlayerCarScreenName = "Test Car",
        PlayerCarPath = "testcar",
    };

    /// <summary>
    /// A session: half an out-lap, the given laps, and half an in-lap, sampled at 60 Hz.
    /// iRacing's LapBestLapTime and LapDeltaToBestLap are synthesised from the exact profiles.
    /// </summary>
    public SyntheticRun Session(IReadOnlyList<LapStyle> styles, bool gps = true, double startTime = 50)
    {
        var laps = styles.Select(Drive).ToArray();
        var outLap = Drive(LapStyle.Clean);
        var all = new List<GeneratedLap> { outLap };
        all.AddRange(laps);
        all.Add(outLap);

        // cumulative time at the start of each lap of the run, from the out-lap's half-way point
        var lapStart = new double[all.Count + 1];
        lapStart[0] = -outLap.TimeAt(Length / 2);
        for (var k = 0; k < all.Count; k++)
            lapStart[k + 1] = lapStart[k] + all[k].LapTime;
        var endTime = lapStart[^2] + all[^1].TimeAt(Length / 2);

        var frames = new List<TelemetryFrame>();
        var dt = 1.0 / TickRate;
        var lapIndex = 0;
        for (var tick = 0; ; tick++)
        {
            var t = tick * dt;
            if (t > endTime)
                break;
            while (lapIndex + 1 < all.Count && t >= lapStart[lapIndex + 1])
                lapIndex++;
            var lap = all[lapIndex];
            var s = DistanceAt(lap, t - lapStart[lapIndex]);
            frames.Add(Frame(frames.Count, startTime + t, lapIndex, s, lap, laps, gps));
        }
        return new SyntheticRun(this, laps, frames);
    }

    // distance into the lap at elapsed time (binary search of the lap's time profile)
    double DistanceAt(GeneratedLap lap, double elapsed)
    {
        var time = lap.Time;
        if (elapsed <= 0)
            return 0;
        if (elapsed >= time[^1])
            return Length;
        int lo = 0, hi = time.Length - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (time[mid] <= elapsed)
                lo = mid;
            else
                hi = mid;
        }
        var f = (elapsed - time[lo]) / (time[hi] - time[lo]);
        return Math.Min(Length, (lo + f) * Resolution);
    }

    TelemetryFrame Frame(int index, double sessionTime, int lapIndex, double s, GeneratedLap lap, GeneratedLap[] driven, bool gps)
    {
        var i = Index(s);
        var speed = lap.SpeedAt(s);
        var style = lap.Style;
        var offset = OffsetAt(style, s);
        var h = _h[i];
        var east = _x[i] - offset * Math.Sin(h);
        var north = _y[i] + offset * Math.Cos(h);
        var (lat, lon, alt) = Geodesy.FromEnu(Origin, east, north, 0);

        var arc = ArcAt(s);
        var kerb = arc is not null && style.Kerbs.Contains(arc.Number) && Math.Abs(s - arc.Mid) <= 10;
        var offTrack = arc is not null && style.OffTrackAt == arc.Number && Math.Abs(s - arc.Mid) <= 7.5;

        // iRacing's delta: against the fastest earlier lap of the run (laps 1.. are the driven ones)
        var drivenIndex = lapIndex - 1;
        GeneratedLap? best = null;
        if (drivenIndex >= 1 && drivenIndex < driven.Length)
            best = driven.Take(drivenIndex).MinBy(l => l.LapTime);
        float? delta = best is null ? null : (float)(lap.TimeAt(s) - best.TimeAt(s));

        return new TelemetryFrame
        {
            Index = index,
            SessionTime = sessionTime,
            SessionTick = index,
            Lap = lapIndex + 1,
            LapCompleted = lapIndex,
            LapDist = (float)s,
            LapDistPct = (float)(s / Length),
            LapLastLapTime = drivenIndex >= 1 && drivenIndex <= driven.Length ? (float)driven[drivenIndex - 1].LapTime : -1,
            LapBestLapTime = best is null ? -1 : (float)best.LapTime,
            Speed = (float)speed,
            Throttle = (float)Lerp(lap.Throttle, s),
            Brake = (float)Lerp(lap.Brake, s),
            SteeringWheelAngle = (float)(_k[i] * 4.0),
            Gear = speed > 40 ? 5 : speed > 25 ? 4 : 3,
            IsOnTrack = true,
            OnPitRoad = false,
            PlayerTrackSurface = offTrack ? 0 : 3,
            PlayerTrackSurfaceMaterial = kerb ? SurfaceMaterials.FirstRumble : offTrack ? SurfaceMaterials.FirstOffTrack : 1,
            YawRate = (float)(speed * _k[i]),
            Lat = gps ? lat : null,
            Lon = gps ? lon : null,
            Alt = gps ? (float)alt : null,
            LapDeltaToBestLap = delta ?? 0,
            LapDeltaToBestLapOk = delta is not null,
        };
    }

    public static readonly IbtVarSpec[] IbtVars =
    [
        new(TelemetryVars.SessionTime, IbtVarType.Double, "s"),
        new(TelemetryVars.SessionTick, IbtVarType.Int),
        new(TelemetryVars.Lap, IbtVarType.Int),
        new(TelemetryVars.LapCompleted, IbtVarType.Int),
        new(TelemetryVars.LapDist, IbtVarType.Float, "m"),
        new(TelemetryVars.LapDistPct, IbtVarType.Float, "%"),
        new(TelemetryVars.LapLastLapTime, IbtVarType.Float, "s"),
        new(TelemetryVars.LapBestLapTime, IbtVarType.Float, "s"),
        new(TelemetryVars.Speed, IbtVarType.Float, "m/s"),
        new(TelemetryVars.Throttle, IbtVarType.Float, "%"),
        new(TelemetryVars.Brake, IbtVarType.Float, "%"),
        new(TelemetryVars.SteeringWheelAngle, IbtVarType.Float, "rad"),
        new(TelemetryVars.Gear, IbtVarType.Int),
        new(TelemetryVars.IsOnTrack, IbtVarType.Bool),
        new(TelemetryVars.OnPitRoad, IbtVarType.Bool),
        new(TelemetryVars.PlayerTrackSurface, IbtVarType.Int, "irsdk_TrkLoc"),
        new(TelemetryVars.PlayerTrackSurfaceMaterial, IbtVarType.Int, "irsdk_TrkSurf"),
        new(TelemetryVars.YawRate, IbtVarType.Float, "rad/s"),
        new(TelemetryVars.Lat, IbtVarType.Double, "deg"),
        new(TelemetryVars.Lon, IbtVarType.Double, "deg"),
        new(TelemetryVars.Alt, IbtVarType.Float, "m"),
        new(TelemetryVars.LapDeltaToBestLap, IbtVarType.Float, "s"),
        new(TelemetryVars.LapDeltaToBestLapOk, IbtVarType.Bool),
    ];

    /// <summary>Session-info YAML for the circuit, with other drivers listed to check they never leak.</summary>
    public string Yaml(int trackId = 999, string config = "Test") => $"""
        ---
        WeekendInfo:
         TrackName: synthetic_circuit
         TrackID: {trackId}
         TrackLength: {Facts().TrackLength}
         TrackDisplayName: Synthetic Circuit
         TrackConfigName: {config}
         TrackNumTurns: {Arcs.Count}
         EventType: Test
         Official: 0

        DriverInfo:
         DriverCarIdx: 1
         Drivers:
         - CarIdx: 0
           UserName: Pace Car
           UserID: -1
           CarScreenName: Safety Car
           CarPath: safety pcporsche911cup
         - CarIdx: 1
           UserName: Test Driver
           UserID: 123456
           CarScreenName: Test Car
           CarPath: testcar
           CarID: 77
         - CarIdx: 2
           UserName: Rival Driver
           UserID: 654321
           CarScreenName: Rival Car
           CarPath: rivalcar
        ...
        """;
}

/// <summary>A generated session: the circuit, the ground truth of each driven lap, and its frames.</summary>
internal sealed record SyntheticRun(SyntheticCircuit Circuit, GeneratedLap[] Laps, IReadOnlyList<TelemetryFrame> Frames)
{
    /// <summary>iRacing lap number of driven lap <paramref name="k"/> (0-based).</summary>
    public static int LapNumber(int k) => k + 2;

    /// <summary>This run's frames, then <paramref name="next"/>'s, as one recording whose lap numbers restart (a reset).</summary>
    public IReadOnlyList<TelemetryFrame> FollowedByReset(SyntheticRun next)
    {
        var offset = Frames.Count;
        return [.. Frames, .. next.Frames.Select(f => f with { Index = f.Index + offset, SessionTick = f.SessionTick + offset })];
    }

    public string WriteIbt(string path, int trackId = 999, string config = "Test")
    {
        var rows = Frames.Select(f => SyntheticCircuit.IbtVars.Select(v => Value(f, v.Name)).ToArray()).ToArray();
        IbtWriter.Write(path, SyntheticCircuit.IbtVars, Circuit.Yaml(trackId, config), rows, SyntheticCircuit.TickRate, lapCount: Laps.Length + 2);
        return path;
    }

    static double Value(TelemetryFrame f, string name) => name switch
    {
        TelemetryVars.SessionTime => f.SessionTime ?? 0,
        TelemetryVars.SessionTick => f.SessionTick ?? 0,
        TelemetryVars.Lap => f.Lap ?? 0,
        TelemetryVars.LapCompleted => f.LapCompleted ?? 0,
        TelemetryVars.LapDist => f.LapDist ?? 0,
        TelemetryVars.LapDistPct => f.LapDistPct ?? 0,
        TelemetryVars.LapLastLapTime => f.LapLastLapTime ?? -1,
        TelemetryVars.LapBestLapTime => f.LapBestLapTime ?? -1,
        TelemetryVars.Speed => f.Speed ?? 0,
        TelemetryVars.Throttle => f.Throttle ?? 0,
        TelemetryVars.Brake => f.Brake ?? 0,
        TelemetryVars.SteeringWheelAngle => f.SteeringWheelAngle ?? 0,
        TelemetryVars.Gear => f.Gear ?? 0,
        TelemetryVars.IsOnTrack => f.IsOnTrack == true ? 1 : 0,
        TelemetryVars.OnPitRoad => f.OnPitRoad == true ? 1 : 0,
        TelemetryVars.PlayerTrackSurface => f.PlayerTrackSurface ?? -1,
        TelemetryVars.PlayerTrackSurfaceMaterial => f.PlayerTrackSurfaceMaterial ?? 0,
        TelemetryVars.YawRate => f.YawRate ?? 0,
        TelemetryVars.Lat => f.Lat ?? 0,
        TelemetryVars.Lon => f.Lon ?? 0,
        TelemetryVars.Alt => f.Alt ?? 0,
        TelemetryVars.LapDeltaToBestLap => f.LapDeltaToBestLap ?? 0,
        TelemetryVars.LapDeltaToBestLapOk => f.LapDeltaToBestLapOk == true ? 1 : 0,
        _ => 0,
    };
}
