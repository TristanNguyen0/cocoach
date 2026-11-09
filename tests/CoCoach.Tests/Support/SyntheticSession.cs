using CoCoach.Telemetry.Ibt;
using CoCoach.Telemetry.Ingest;

namespace CoCoach.Tests.Support;

/// <summary>A lap the generator knows the exact answer for.</summary>
internal sealed record ExpectedLap(int Lap, double? LapTime, float MinSpeed, float MaxSpeed, int Frames);

/// <summary>
/// Generates a deterministic 60 Hz session on a synthetic track: the car starts mid-lap (an out-lap),
/// drives whole laps with a varying speed profile and brake zones, and stops mid-lap. Speed is constant
/// within a tick, so the exact start/finish crossing time is known analytically.
/// </summary>
internal sealed class SyntheticSession
{
    public const double TrackLength = 1500;   // metres
    public const int TickRate = 60;
    public const double ReportDelaySeconds = 0.05;  // iRacing publishes LapLastLapTime a few ticks after the line

    public const string Yaml = """
        ---
        WeekendInfo:
         TrackName: synthetic_oval
         TrackID: 999
         TrackLength: 1.50 km
         TrackDisplayName: Synthetic Test Circuit
         TrackConfigName: Full
         TrackCity: Nowhere
         TrackCountry: Testland
         EventType: Test
         Official: 0

        SessionInfo:
         Sessions:
         - SessionNum: 0
           SessionType: Offline Testing

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
           CarScreenName: Test Roadster
           CarPath: testroadster
           CarID: 77
           CarClassShortName: TR
         - CarIdx: 2
           UserName: Rival: The "Other" Driver
           UserID: 654321
           CarScreenName: Rival Car
           CarPath: rivalcar
        ...
        """;

    public static readonly IbtVarSpec[] AllVars =
    [
        new(TelemetryVars.SessionTime, IbtVarType.Double, "s"),
        new(TelemetryVars.SessionTick, IbtVarType.Int),
        new(TelemetryVars.Lap, IbtVarType.Int),
        new(TelemetryVars.LapCompleted, IbtVarType.Int),
        new(TelemetryVars.LapDist, IbtVarType.Float, "m"),
        new(TelemetryVars.LapDistPct, IbtVarType.Float, "%"),
        new(TelemetryVars.LapLastLapTime, IbtVarType.Float, "s"),
        new(TelemetryVars.Speed, IbtVarType.Float, "m/s"),
        new(TelemetryVars.Throttle, IbtVarType.Float, "%"),
        new(TelemetryVars.Brake, IbtVarType.Float, "%"),
        new(TelemetryVars.SteeringWheelAngle, IbtVarType.Float, "rad"),
        new(TelemetryVars.Gear, IbtVarType.Int),
        new(TelemetryVars.IsOnTrack, IbtVarType.Bool),
        new(TelemetryVars.OnPitRoad, IbtVarType.Bool),
        new(TelemetryVars.PlayerTrackSurface, IbtVarType.Int, "irsdk_TrkLoc"),
        new("Lat", IbtVarType.Double, "deg"),
        new("Lon", IbtVarType.Double, "deg"),
        new("YawRate_ST", IbtVarType.Float, "rad/s", Count: 6),
    ];

    readonly List<Dictionary<string, double>> _rows = [];

    public SyntheticSession(int laps = 3, double startPct = 0.6, double endPct = 0.4, double startTime = 100.0)
    {
        var dt = 1.0 / TickRate;
        var distance = startPct * TrackLength;     // total distance from the first line crossing of lap 1
        var end = (laps + endPct + 1) * TrackLength;
        var time = startTime;
        var tick = (int)(startTime * TickRate);
        float lastLapTime = -1;
        double? lineTime = null;
        double? pendingReport = null;
        double reportAt = double.MaxValue;

        var lapFrames = new Dictionary<int, List<float>>();
        var crossings = new Dictionary<int, double>();

        while (distance < end)
        {
            var lapIndex = (int)Math.Floor(distance / TrackLength);
            var lap = lapIndex + 1;
            var pct = distance / TrackLength - lapIndex;
            var speed = SpeedAt(pct);
            var brake = BrakeAt(pct);

            if (pendingReport is { } report && time >= reportAt)
            {
                lastLapTime = (float)report;
                pendingReport = null;
            }

            _rows.Add(new Dictionary<string, double>
            {
                [TelemetryVars.SessionTime] = time,
                [TelemetryVars.SessionTick] = tick,
                [TelemetryVars.Lap] = lap,
                [TelemetryVars.LapCompleted] = lap - 1,
                [TelemetryVars.LapDist] = pct * TrackLength,
                [TelemetryVars.LapDistPct] = pct,
                [TelemetryVars.LapLastLapTime] = lastLapTime,
                [TelemetryVars.Speed] = speed,
                [TelemetryVars.Throttle] = brake > 0 ? 0 : 1,
                [TelemetryVars.Brake] = brake,
                [TelemetryVars.SteeringWheelAngle] = 0.3 * Math.Sin(2 * Math.PI * pct * 4),
                [TelemetryVars.Gear] = speed > 45 ? 5 : 4,
                [TelemetryVars.IsOnTrack] = 1,
                [TelemetryVars.OnPitRoad] = 0,
                [TelemetryVars.PlayerTrackSurface] = 3,
                ["Lat"] = 50.0 + pct * 0.01,
                ["Lon"] = 5.0 + pct * 0.01,
                ["YawRate_ST"] = 0.1,
            });

            if (!lapFrames.TryGetValue(lap, out var speeds))
                lapFrames[lap] = speeds = [];
            speeds.Add((float)speed);

            // advance one tick at constant speed; find the exact line crossing if there is one
            var next = distance + speed * dt;
            var nextLine = (lapIndex + 1) * TrackLength;
            if (next >= nextLine)
            {
                var crossing = time + (nextLine - distance) / speed;
                crossings[lap + 1] = crossing;
                if (lineTime is { } previousLine)
                {
                    pendingReport = crossing - previousLine;
                    reportAt = crossing + ReportDelaySeconds;
                }
                lineTime = crossing;
            }

            distance = next;
            time += dt;
            tick++;
        }

        var expected = new List<ExpectedLap>();
        foreach (var (lap, speeds) in lapFrames.OrderBy(kv => kv.Key))
        {
            double? lapTime = crossings.TryGetValue(lap, out var start) && crossings.TryGetValue(lap + 1, out var finish)
                ? finish - start
                : null;
            expected.Add(new ExpectedLap(lap, lapTime, speeds.Min(), speeds.Max(), speeds.Count));
        }
        ExpectedLaps = expected;
    }

    public IReadOnlyList<ExpectedLap> ExpectedLaps { get; }

    public int FrameCount => _rows.Count;

    public IReadOnlyList<Dictionary<string, double>> Rows => _rows;

    /// <summary>Speed in m/s: fast straights, slow corners.</summary>
    public static double SpeedAt(double pct) => 40 + 15 * Math.Cos(2 * Math.PI * pct * 3);

    /// <summary>Two brake zones per lap.</summary>
    public static double BrakeAt(double pct) => pct is (> 0.25 and < 0.30) or (> 0.70 and < 0.74) ? 0.8 : 0;

    /// <summary>Frames as the pipeline would see them, without going through a file.</summary>
    public IEnumerable<TelemetryFrame> Frames(IReadOnlyCollection<string>? omit = null) =>
        _rows.Select((row, i) => TelemetryFrameReader.Read(
            name => omit?.Contains(name) == true ? null : Lookup(row, name),
            i,
            0));

    static object? Lookup(Dictionary<string, double> row, string name)
    {
        if (!row.TryGetValue(name, out var v))
            return null;
        return AllVars.First(x => x.Name == name).Type switch
        {
            IbtVarType.Double => v,
            IbtVarType.Float => (float)v,
            IbtVarType.Int or IbtVarType.BitField => (int)v,
            IbtVarType.Bool => v != 0,
            _ => (byte)v,
        };
    }

    /// <summary>Writes the session as an .ibt file, optionally leaving some variables out.</summary>
    public string WriteIbt(string path, IReadOnlyCollection<string>? omit = null, string? yaml = null)
    {
        var vars = AllVars.Where(v => omit?.Contains(v.Name) != true).ToArray();
        var rows = _rows.Select(r => vars.Select(v => r[v.Name]).ToArray()).ToArray();
        IbtWriter.Write(path, vars, yaml ?? Yaml, rows, TickRate, lapCount: ExpectedLaps.Count);
        return path;
    }
}
