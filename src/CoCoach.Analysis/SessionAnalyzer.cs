using CoCoach.Analysis.Alignment;
using CoCoach.Analysis.Comparison;
using CoCoach.Analysis.Corners;
using CoCoach.Analysis.Recording;
using CoCoach.Analysis.References;
using CoCoach.Analysis.Track;
using CoCoach.Telemetry.Session;

namespace CoCoach.Analysis;

/// <summary>Options for <see cref="SessionAnalyzer"/>.</summary>
public sealed record AnalysisOptions
{
    /// <summary>What every lap is compared against.</summary>
    public ReferenceKind Reference { get; init; } = ReferenceKind.Best;

    /// <summary>Grid spacing, metres.</summary>
    public double Step { get; init; } = LapTraceBuilder.DefaultStep;

    /// <summary>A valid lap within this factor of the best lap is clean enough to shape the track model and corners.</summary>
    public double CleanLapFactor { get; init; } = 1.07;

    public CornerDetectionOptions Corners { get; init; } = new();
}

/// <summary>One lap compared with the reference.</summary>
/// <param name="Trace">The lap.</param>
/// <param name="Delta">Seconds behind (+) the reference at each grid point.</param>
/// <param name="Corners">Per-corner comparison, in corner order.</param>
public sealed record LapAnalysis(LapTrace Trace, double[] Delta, IReadOnlyList<CornerComparison> Corners)
{
    public double LapTimeDelta(ReferenceLap reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return Trace.LapTime - reference.LapTime;
    }
}

/// <summary>Typical time lost in one corner over the analysed laps (medians, so one spin does not dominate).</summary>
/// <param name="Corner">The corner.</param>
/// <param name="Laps">Laps compared.</param>
/// <param name="TimeLost">Median per-lap time delta over the corner window, seconds.</param>
/// <param name="EntryLost">Median entry sub-segment delta, seconds.</param>
/// <param name="MidLost">Median mid sub-segment delta, seconds.</param>
/// <param name="ExitLost">Median exit sub-segment delta, seconds.</param>
/// <param name="MinSpeedDifference">Median minimum-speed difference, m/s.</param>
/// <param name="BrakePointOffset">Median brake-point offset, metres (+ = early), over laps that braked.</param>
/// <param name="MeanTimeLost">Mean per-lap time delta, seconds, for comparison with the median.</param>
public sealed record CornerLoss(
    Corner Corner,
    int Laps,
    double TimeLost,
    double EntryLost,
    double MidLost,
    double ExitLost,
    double MinSpeedDifference,
    double? BrakePointOffset,
    double MeanTimeLost);

/// <summary>Everything <see cref="SessionAnalyzer.Analyze"/> found.</summary>
public sealed record AnalysisResult
{
    public required TrackKey Key { get; init; }
    public required SessionFacts? Facts { get; init; }
    public required IReadOnlyList<string> Sessions { get; init; }
    public required TrackModel Track { get; init; }
    public required CornerCountCheck CornerCount { get; init; }

    /// <summary>Every complete lap, in session then driving order.</summary>
    public required IReadOnlyList<LapTrace> Laps { get; init; }

    /// <summary>Valid laps within <see cref="AnalysisOptions.CleanLapFactor"/> of the best: these shaped the track model and corners.</summary>
    public required IReadOnlyList<LapTrace> CleanLaps { get; init; }

    public required ReferenceLap Best { get; init; }
    public required ReferenceLap Optimal { get; init; }

    /// <summary>The reference every lap is compared against.</summary>
    public required ReferenceLap Reference { get; init; }

    /// <summary>Key points of the reference in every corner.</summary>
    public required IReadOnlyList<CornerKeyPoints> ReferenceCorners { get; init; }

    /// <summary>Every complete lap off pit road against the reference.</summary>
    public required IReadOnlyList<LapAnalysis> LapAnalyses { get; init; }

    /// <summary>Corners ordered by median time lost over the valid laps (reference excluded), worst first.</summary>
    public required IReadOnlyList<CornerLoss> CornerLosses { get; init; }

    /// <summary>Our delta against iRacing's <c>LapDeltaToBestLap</c>, for laps that carry it.</summary>
    public required IReadOnlyList<DeltaCheck> IracingDeltaChecks { get; init; }

    /// <summary>iRacing's own optimal lap time per session (from <c>LapDeltaToOptimalLap</c>), where the recording carries it.</summary>
    public required IReadOnlyList<double?> IracingOptimalLapTimes { get; init; }

    public bool MultipleSessions => Sessions.Count > 1;
}

/// <summary>
/// The analysis engine, end to end: align laps on a distance grid, build the track model, find corners,
/// build the best and optimal references, and compare every lap corner by corner. Deterministic and
/// local: the same recordings always give the same result.
/// </summary>
public static class SessionAnalyzer
{
    /// <exception cref="InvalidOperationException">
    /// The sessions are not all the same track layout and car, or they hold no valid complete lap.
    /// </exception>
    public static AnalysisResult Analyze(IReadOnlyList<RecordedSession> sessions, AnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (sessions.Count == 0)
            throw new InvalidOperationException("No sessions to analyse.");
        options ??= new AnalysisOptions();

        var facts = sessions[0].Facts;
        var key = TrackKey.From(facts);
        foreach (var s in sessions.Skip(1))
        {
            if (!TrackKey.From(s.Facts).SameLayout(key))
                throw new InvalidOperationException($"{s.Label} is a different track layout from {sessions[0].Label}; analyse one layout at a time.");
            if (!string.Equals(s.Facts?.PlayerCarPath, facts?.PlayerCarPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{s.Label} is a different car from {sessions[0].Label}; analyse one car at a time.");
        }

        var lengths = sessions.Select(LapTraceBuilder.EstimateTrackLength).Where(l => l is not null).Select(l => l!.Value).ToArray();
        if (lengths.Length == 0)
            throw new InvalidOperationException("The track length is unknown: the recordings have no LapDist/LapDistPct and no TrackLength.");
        var length = Signal.Median(lengths);

        var perSession = sessions.Select((s, i) => LapTraceBuilder.Build(s, i, length, options.Step)).ToArray();
        var laps = perSession.SelectMany(l => l).ToArray();
        var valid = laps.Where(l => l.IsValid).ToArray();
        if (valid.Length == 0)
            throw new InvalidOperationException("No valid complete lap (every lap was partial, in the pits, or off track).");

        var bestTime = valid.Min(l => l.LapTime);
        var clean = valid.Where(l => l.LapTime <= bestTime * options.CleanLapFactor).ToArray();

        var expectedTurns = sessions.Select(s => s.Facts?.TrackNumTurns).FirstOrDefault(t => t is not null);
        var model = TrackModelBuilder.Build(key, clean, laps, expectedTurns);
        var corners = CornerDetector.Detect(model, clean, options.Corners);
        model = model with { Corners = corners };

        var best = ReferenceBuilder.Best(valid)!;
        var optimal = ReferenceBuilder.Optimal(valid, corners)!;
        var reference = options.Reference == ReferenceKind.Optimal ? optimal : best;
        var referenceCorners = CornerKeyPointFinder.Find(reference.Trace, corners, model, options.Corners);

        var analyses = laps
            .Where(l => !l.PitRoad)
            .Select(l => new LapAnalysis(
                l,
                DeltaTrace.Compute(l, reference.Trace),
                DeltaTrace.CompareCorners(CornerKeyPointFinder.Find(l, corners, model, options.Corners), referenceCorners)))
            .ToArray();

        var losses = CornerLosses(analyses.Where(a => a.Trace.IsValid && a.Trace.Id != reference.Trace.Id).ToArray(), corners);
        var checks = perSession.SelectMany(s => IracingDeltaCheck.AgainstBest(s)).ToArray();

        return new AnalysisResult
        {
            Key = key,
            Facts = facts,
            Sessions = sessions.Select(s => s.Label).ToArray(),
            Track = model,
            CornerCount = CornerCountCheck.For(corners.Count, expectedTurns),
            Laps = laps,
            CleanLaps = clean,
            Best = best,
            Optimal = optimal,
            Reference = reference,
            ReferenceCorners = referenceCorners,
            LapAnalyses = analyses,
            CornerLosses = losses,
            IracingDeltaChecks = checks,
            IracingOptimalLapTimes = perSession.Select(IracingDeltaCheck.IracingOptimalLapTime).ToArray(),
        };
    }

    static CornerLoss[] CornerLosses(LapAnalysis[] laps, IReadOnlyList<Corner> corners)
    {
        if (laps.Length == 0)
            return [];
        return corners
            .Select(c =>
            {
                var rows = laps.Select(l => l.Corners.First(x => x.Corner.Number == c.Number)).ToArray();
                var brake = rows.Select(r => r.BrakePointOffset).Where(b => b is not null).Select(b => b!.Value).ToArray();
                return new CornerLoss(
                    c,
                    rows.Length,
                    Signal.Median(rows.Select(r => r.TimeDelta)),
                    Signal.Median(rows.Select(r => r.EntryDelta)),
                    Signal.Median(rows.Select(r => r.MidDelta)),
                    Signal.Median(rows.Select(r => r.ExitDelta)),
                    Signal.Median(rows.Select(r => r.MinSpeedDifference)),
                    brake.Length > 0 ? Signal.Median(brake) : null,
                    rows.Average(r => r.TimeDelta));
            })
            .OrderByDescending(l => l.TimeLost)
            .ToArray();
    }
}
