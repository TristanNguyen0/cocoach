using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoCoach.Analysis;
using CoCoach.Analysis.Comparison;
using CoCoach.Analysis.Corners;
using CoCoach.Analysis.Recording;
using CoCoach.Analysis.References;
using CoCoach.Analysis.Track;

namespace CoCoach.Cli.Commands;

/// <summary>
/// <c>cocoach analyze &lt;file.ibt&gt;... [--reference best|optimal] [--json]</c>: the analysis engine on one or more
/// recordings of the same car and track. Prints the corners found, every lap's per-corner metrics against the
/// reference, and the three corners that cost the most time.
/// </summary>
internal static class AnalyzeCommand
{
    const string UsageText = "usage: cocoach analyze <file.ibt>... [--reference best|optimal] [--json]";

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var json = false;
        var reference = ReferenceKind.Best;
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json":
                    json = true;
                    break;
                case "--reference" when i + 1 < args.Length && args[i + 1] is "best" or "optimal":
                    reference = args[++i] == "optimal" ? ReferenceKind.Optimal : ReferenceKind.Best;
                    break;
                case var a when a.StartsWith("--", StringComparison.Ordinal):
                    await error.WriteLineAsync(UsageText).ConfigureAwait(false);
                    return CliApp.Usage;
                default:
                    files.Add(args[i]);
                    break;
            }
        }
        if (files.Count == 0)
        {
            await error.WriteLineAsync(UsageText).ConfigureAwait(false);
            return CliApp.Usage;
        }

        var sessions = new List<RecordedSession>();
        foreach (var file in files)
            sessions.Add(await SessionLoader.LoadIbtAsync(file, CliLogging.Sdk, cancellationToken).ConfigureAwait(false));

        AnalysisResult result;
        try
        {
            result = SessionAnalyzer.Analyze(sessions, new AnalysisOptions { Reference = reference });
        }
        catch (InvalidOperationException ex)
        {
            await error.WriteLineAsync($"error: {ex.Message}").ConfigureAwait(false);
            return CliApp.Failure;
        }

        await output.WriteAsync(json ? ToJson(result) + Environment.NewLine : ToText(result)).ConfigureAwait(false);
        return CliApp.Ok;
    }

    internal static string ToText(AnalysisResult r)
    {
        var sb = new StringBuilder();
        void Line(FormattableString text) => sb.AppendLine(text.ToString(Inv));
        var multi = r.MultipleSessions;

        sb.AppendLine(Formatting.TrackAndCar(r.Facts));
        Line($"sessions: {r.Sessions.Count}, complete laps: {r.Laps.Count}, valid: {r.Laps.Count(l => l.IsValid)}, clean (within 107 % of best): {r.CleanLaps.Count}");
        if (multi)
        {
            for (var i = 0; i < r.Sessions.Count; i++)
                Line($"  S{i + 1}: {r.Sessions[i]}");
        }

        var map = r.Track;
        var mapText = map.Source == TrackMapSource.Gps
            ? $"GPS centreline averaged from {map.LapsUsed} clean laps, edges estimated from {r.Laps.Count(l => !l.PitRoad)} laps off pit road"
            : $"no GPS: dead-reckoned from speed and yaw rate over {map.LapsUsed} laps, loop-closure error {map.ClosureError:0.0} m before correction";
        Line($"track map: {map.Length:0.0} m, {mapText}");

        var count = r.CornerCount;
        var expected = count.Expected is { } e
            ? $"iRacing lists {e} turns: {(count.Plausible ? "plausible" : "check the detection")}"
            : "iRacing turn count not in the recording";
        Line($"corners: {count.Found} found ({expected})");
        sb.AppendLine();
        if (map.Corners.Count == 0)
        {
            sb.AppendLine("  no corners detected, so there are no per-corner metrics (large-radius oval turns are not detected yet)");
        }
        else
        {
            sb.AppendLine("  corner  dir     start  turn-in    apex    exit     end   turn   min R");
            foreach (var c in map.Corners)
                Line($"  {c.Name,-6}  {(c.Direction == CornerDirection.Left ? "left" : "right"),-5}  {c.Start,6:0}  {c.TurnIn,7:0}  {c.Apex,6:0}  {c.Exit,6:0}  {c.End,6:0}  {c.HeadingChange,4:0}°  {c.MinRadius,4:0} m");
            sb.AppendLine("  (positions are metres from the start/finish line)");
        }
        sb.AppendLine();

        Line($"best lap:  {r.Best.Trace.Id.Display(multi)} {Formatting.LapTime(r.Best.LapTime)}");
        Line($"optimal:   {Formatting.LapTime(r.Optimal.LapTime)} ({Signed(r.Optimal.LapTime - r.Best.LapTime, "0.000")} s), best corner segments from {string.Join(", ", r.Optimal.Trace.ComposedFrom.Select(l => l.Display(multi)))}");
        var refName = r.Reference.Kind == ReferenceKind.Optimal ? "optimal composite" : $"best lap {r.Best.Trace.Id.Display(multi)}";
        var iracingOptimal = r.IracingOptimalLapTimes.Where(t => t is not null).Select(t => t!.Value).ToArray();
        if (iracingOptimal.Length > 0)
            Line($"           iRacing's own optimal lap (from LapDeltaToOptimalLap, its own sectors): {Formatting.LapTime(iracingOptimal.Min())}");
        Line($"reference: {refName}");
        WriteDeltaCheck(sb, r, multi);
        sb.AppendLine();

        Line($"per-corner metrics against the {refName}:");
        sb.AppendLine("  time deltas in s (+ = lost); brake m: + = braked earlier; apex and pickup m: + = later;");
        sb.AppendLine("  speeds in km/h (lap minus reference); line m: + = further inside at the apex (GPS maps only)");
        sb.AppendLine();
        sb.AppendLine("  lap     lap time     delta  corner   Δtime  Δentry    Δmid   Δexit  brake m  Δentry v  Δmin v  Δexit v  apex m  pickup m  kerb m  line m  note");
        foreach (var lap in r.LapAnalyses)
        {
            var head = string.Create(Inv, $"  {lap.Trace.Id.Display(multi),-6} {Formatting.LapTime(lap.Trace.LapTime),9}  {Signed(lap.LapTimeDelta(r.Reference), "0.000"),8}");
            var isReference = lap.Trace.Id == r.Reference.Trace.Id;
            if (lap.Corners.Count == 0)
                Line($"{head}  {(isReference ? "reference" : lap.Trace.OffTrack ? "off track" : string.Empty)}");
            foreach (var c in lap.Corners)
            {
                var note = isReference ? "reference" : c.Lap.OffTrack ? "off track" : string.Empty;
                Line($"{head}  {c.Corner.Name,-6} {Signed(c.TimeDelta, "0.00"),6}  {Signed(c.EntryDelta, "0.00"),6}  {Signed(c.MidDelta, "0.00"),6}  {Signed(c.ExitDelta, "0.00"),6}  {Opt(c.BrakePointOffset, "0"),7}  {Signed(c.EntrySpeedDifference * 3.6, "0.0"),8}  {Signed(c.MinSpeedDifference * 3.6, "0.0"),6}  {Signed(c.ExitSpeedDifference * 3.6, "0.0"),7}  {Signed(c.ApexOffset, "0"),6}  {Opt(c.ThrottlePickupOffset, "0"),8}  {Kerb(c.Lap.KerbMetres),6}  {Opt(c.ApexLineOffset, "0.0"),6}  {note}");
                head = new string(' ', 2 + 6 + 1 + 9 + 2 + 8);
            }
            if (!lap.Trace.IsValid)
                sb.AppendLine("         (lap not valid: off track or pit road; not used for references)");
        }
        sb.AppendLine();

        var top = r.CornerLosses.Where(l => l.TimeLost > 0).Take(3).ToArray();
        var lapsAveraged = r.CornerLosses.Count > 0 ? r.CornerLosses[0].Laps : 0;
        Line($"top {top.Length} time-loss corners (median over {lapsAveraged} valid laps, reference excluded):");
        if (top.Length == 0)
            sb.AppendLine(map.Corners.Count == 0 ? "  none: no corners detected" : "  none: no valid lap besides the reference");
        var rank = 1;
        foreach (var loss in top)
        {
            var parts = new[] { ("entry", loss.EntryLost), ("mid", loss.MidLost), ("exit", loss.ExitLost) };
            var worst = parts.MaxBy(p => p.Item2);
            var brake = loss.BrakePointOffset is { } b ? string.Create(Inv, $", brake point {Signed(b, "0")} m") : string.Empty;
            Line($"  {rank++}. {loss.Corner.Name}: {loss.TimeLost:0.000} s per lap (mean {loss.MeanTimeLost:0.000} s), most in the {worst.Item1} ({Signed(worst.Item2, "0.000")} s); min speed {Signed(loss.MinSpeedDifference * 3.6, "0.0")} km/h{brake}");
        }
        return sb.ToString();
    }

    static void WriteDeltaCheck(StringBuilder sb, AnalysisResult r, bool multi)
    {
        var checks = r.IracingDeltaChecks;
        if (checks.Count == 0)
        {
            sb.AppendLine("iRacing delta cross-check: not available (no LapDeltaToBestLap, or iRacing's best lap is not in the recording)");
            return;
        }
        var consistent = checks.Where(c => c.IracingSelfConsistent).ToArray();
        var skipped = checks.Count - consistent.Length;
        if (consistent.Length == 0)
        {
            sb.AppendLine(string.Create(Inv,
                $"iRacing delta cross-check: {checks.Count} laps, but on every one iRacing's own LapDeltaToBestLap disagrees with its lap times; not comparable"));
            return;
        }
        sb.AppendLine(string.Create(Inv,
            $"iRacing delta cross-check: {consistent.Length} laps against LapDeltaToBestLap, median |ours - iRacing| {Median(consistent.Select(c => c.MedianAbsError)):0.000} s, worst {consistent.Max(c => c.MaxAbsError):0.000} s ({string.Join(", ", consistent.Select(c => c.Lap.Display(multi)))})"));
        if (skipped > 0)
            sb.AppendLine(string.Create(Inv, $"  {skipped} laps skipped where iRacing's own delta disagrees with its lap times"));
    }

    static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    static string Signed(double value, string format) =>
        double.IsFinite(value) ? (value >= 0 ? "+" : "-") + Math.Abs(value).ToString(format, Inv) : "-";

    static string Opt(double? value, string format) => value is { } v ? Signed(v, format) : "-";

    static string Kerb(double? metres) => metres is { } m ? m.ToString("0", Inv) : "-";

    internal static string ToJson(AnalysisResult r)
    {
        var multi = r.MultipleSessions;
        string Id(Analysis.Alignment.LapId id) => id.Display(multi);
        static double R(double v, int digits = 3) => double.IsFinite(v) ? Math.Round(v, digits) : v;
        static double? RO(double? v, int digits = 3) => v is { } x ? R(x, digits) : null;

        object KeyPoints(CornerKeyPoints k) => new
        {
            brakePoint = RO(k.BrakePoint, 1),
            peakBrake = RO(k.PeakBrake),
            brakeRelease = RO(k.BrakeRelease, 1),
            releaseRate = RO(k.ReleaseRate),
            trailBrakeDistance = RO(k.TrailBrakeDistance, 1),
            turnInPoint = RO(k.TurnInPoint, 1),
            apexPoint = R(k.ApexPoint, 1),
            minSpeed = R(k.MinSpeed, 2),
            entrySpeed = R(k.EntrySpeed, 2),
            exitSpeed = R(k.ExitSpeed, 2),
            throttlePickup = RO(k.ThrottlePickup, 1),
            fullThrottle = RO(k.FullThrottle, 1),
            entryTime = R(k.EntryTime),
            midTime = R(k.MidTime),
            exitTime = R(k.ExitTime),
            kerbMetres = RO(k.KerbMetres, 0),
            offTrack = k.OffTrack,
            lineAtTurnIn = RO(k.LineAtTurnIn, 2),
            lineAtApex = RO(k.LineAtApex, 2),
            lineAtExit = RO(k.LineAtExit, 2),
        };

        var doc = new
        {
            units = "distances m from the start/finish line, speeds m/s, times s",
            conventions = "time deltas + = lost; brakePointOffset + = earlier; apex/throttle offsets + = later; speed differences lap minus reference; line offsets + = inside",
            session = r.Facts,
            sessions = r.Sessions,
            track = new
            {
                key = r.Key,
                length = R(r.Track.Length, 1),
                source = r.Track.Source,
                lapsUsed = r.Track.LapsUsed,
                closureError = RO(r.Track.ClosureError, 1),
                cornerCount = r.CornerCount,
                corners = r.Track.Corners,
            },
            best = new { lap = Id(r.Best.Trace.Id), lapTime = R(r.Best.LapTime) },
            optimal = new
            {
                lapTime = R(r.Optimal.LapTime),
                iracingOptimalLapTimes = r.IracingOptimalLapTimes.Select(t => RO(t)),
                segments = r.Optimal.Segments.Select(s => new { corner = s.Corner?.Name, from = s.From, to = s.To, lap = Id(s.Lap), time = R(s.Time) }),
            },
            reference = new
            {
                kind = r.Reference.Kind,
                lap = r.Reference.Kind == ReferenceKind.Best ? Id(r.Reference.Trace.Id) : null,
                lapTime = R(r.Reference.LapTime),
                corners = r.ReferenceCorners.Select(k => new { corner = k.Corner.Name, keyPoints = KeyPoints(k) }),
            },
            laps = r.LapAnalyses.Select(a => new
            {
                lap = Id(a.Trace.Id),
                session = a.Trace.Id.Session + 1,
                lapNumber = a.Trace.Id.Lap,
                lapTime = R(a.Trace.LapTime),
                iracingLapTime = RO(a.Trace.ReportedLapTime),
                valid = a.Trace.IsValid,
                offTrack = a.Trace.OffTrack,
                delta = R(a.LapTimeDelta(r.Reference)),
                corners = a.Corners.Select(c => new
                {
                    corner = c.Corner.Name,
                    timeDelta = R(c.TimeDelta),
                    entryDelta = R(c.EntryDelta),
                    midDelta = R(c.MidDelta),
                    exitDelta = R(c.ExitDelta),
                    brakePointOffset = RO(c.BrakePointOffset, 1),
                    peakBrakeDifference = RO(c.PeakBrakeDifference),
                    releaseRateDifference = RO(c.ReleaseRateDifference),
                    entrySpeedDifference = R(c.EntrySpeedDifference, 2),
                    minSpeedDifference = R(c.MinSpeedDifference, 2),
                    exitSpeedDifference = R(c.ExitSpeedDifference, 2),
                    apexOffset = R(c.ApexOffset, 1),
                    throttlePickupOffset = RO(c.ThrottlePickupOffset, 1),
                    fullThrottleOffset = RO(c.FullThrottleOffset, 1),
                    apexLineOffset = RO(c.ApexLineOffset, 2),
                    keyPoints = KeyPoints(c.Lap),
                }),
            }),
            topTimeLoss = r.CornerLosses.Where(l => l.TimeLost > 0).Take(3).Select(l => new
            {
                corner = l.Corner.Name,
                laps = l.Laps,
                timeLost = R(l.TimeLost),
                entryLost = R(l.EntryLost),
                midLost = R(l.MidLost),
                exitLost = R(l.ExitLost),
                minSpeedDifference = R(l.MinSpeedDifference, 2),
                brakePointOffset = RO(l.BrakePointOffset, 1),
                meanTimeLost = R(l.MeanTimeLost),
            }),
            iracingDeltaCheck = r.IracingDeltaChecks.Select(c => new
            {
                lap = Id(c.Lap),
                reference = Id(c.Reference),
                samples = c.Samples,
                medianAbsError = R(c.MedianAbsError, 4),
                p95AbsError = R(c.P95AbsError, 4),
                maxAbsError = R(c.MaxAbsError, 4),
                finalDelta = R(c.FinalDelta),
                iracingFinalDelta = RO(c.IracingFinalDelta),
                iracingSelfConsistent = c.IracingSelfConsistent,
            }),
        };
        return JsonSerializer.Serialize(doc, Json);
    }
}
