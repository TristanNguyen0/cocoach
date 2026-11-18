using CoCoach.Analysis.Alignment;

namespace CoCoach.Analysis.Comparison;

/// <summary>How closely our delta trace matches iRacing's own delta channel over one lap.</summary>
/// <param name="Lap">The lap whose delta is compared.</param>
/// <param name="Reference">The lap we compared against: the one iRacing was using.</param>
/// <param name="Samples">Grid points where iRacing's channel was valid.</param>
/// <param name="MedianAbsError">Median |ours - iRacing's|, seconds.</param>
/// <param name="P95AbsError">95th percentile of |ours - iRacing's|, seconds.</param>
/// <param name="MaxAbsError">Largest |ours - iRacing's|, seconds.</param>
/// <param name="FinalDelta">Our delta at the finish line (lap time minus reference lap time), seconds.</param>
/// <param name="IracingFinalDelta">iRacing's delta at the last grid point before the line, seconds.</param>
/// <param name="IracingSelfConsistent">
/// iRacing's own delta at the line agrees (within <see cref="IracingDeltaCheck.SelfConsistencyTolerance"/>) with
/// its own lap times. When it does not, iRacing's channel is not tracking that reference and the errors say
/// nothing about our delta.
/// </param>
public sealed record DeltaCheck(
    LapId Lap,
    LapId Reference,
    int Samples,
    double MedianAbsError,
    double P95AbsError,
    double MaxAbsError,
    double FinalDelta,
    double? IracingFinalDelta,
    bool IracingSelfConsistent);

/// <summary>
/// Cross-checks our distance-aligned delta against iRacing's <c>LapDeltaToBestLap</c> (research report §4.3).
/// iRacing compares each lap with its session best so far, whose time it publishes as <c>LapBestLapTime</c>;
/// the reference for a lap is the earlier lap of the same session with that lap time.
/// </summary>
public static class IracingDeltaCheck
{
    /// <summary>iRacing's delta channel still shows the previous lap's value at the line; skip this far either side of it, metres.</summary>
    public const double EdgeSkip = 5;

    /// <summary>How far iRacing's delta at the line may be from its own lap-time difference, seconds.</summary>
    public const double SelfConsistencyTolerance = 0.01;

    /// <summary>How close a lap time must be to iRacing's best lap time to be that lap, seconds.</summary>
    public const double LapTimeMatch = 0.005;

    /// <summary>
    /// Checks every lap of one session that carries <c>LapDeltaToBestLap</c> against the earlier lap iRacing
    /// was using as its best. Laps whose reference is not in the recording (set before recording started) are skipped.
    /// </summary>
    /// <param name="sessionLaps">Complete laps of one session, in driving order.</param>
    public static IReadOnlyList<DeltaCheck> AgainstBest(IReadOnlyList<LapTrace> sessionLaps)
    {
        ArgumentNullException.ThrowIfNull(sessionLaps);
        var results = new List<DeltaCheck>();
        for (var k = 1; k < sessionLaps.Count; k++)
        {
            var lap = sessionLaps[k];
            if (lap.IracingDeltaToBest is null || lap.IracingBestLapTime is not { } best)
                continue;
            var reference = sessionLaps
                .Take(k)
                .Select(l => (Lap: l, Error: Math.Abs((l.ReportedLapTime ?? l.LapTime) - best)))
                .Where(c => c.Error <= LapTimeMatch)
                .OrderBy(c => c.Error)
                .Select(c => c.Lap)
                .FirstOrDefault();
            if (reference is not null && Compare(lap, reference) is { } check)
                results.Add(check);
        }
        return results;
    }

    /// <summary>Compares our delta of <paramref name="lap"/> against <paramref name="reference"/> with iRacing's channel.</summary>
    public static DeltaCheck? Compare(LapTrace lap, LapTrace reference)
    {
        ArgumentNullException.ThrowIfNull(lap);
        ArgumentNullException.ThrowIfNull(reference);
        if (lap.IracingDeltaToBest is not { } iracing)
            return null;

        var ours = DeltaTrace.Compute(lap, reference);
        var errors = new List<double>();
        for (var i = 0; i < ours.Length; i++)
        {
            var d = lap.DistanceAt(i);
            if (d < EdgeSkip || d > lap.Length - EdgeSkip || !double.IsFinite(iracing[i]))
                continue;
            errors.Add(Math.Abs(ours[i] - iracing[i]));
        }
        if (errors.Count == 0)
            return null;

        double? iracingFinal = null;
        for (var i = ours.Length - 1; i >= 0 && iracingFinal is null; i--)
        {
            if (lap.DistanceAt(i) <= lap.Length - 1 && double.IsFinite(iracing[i]))
                iracingFinal = iracing[i];
        }
        var expected = (lap.ReportedLapTime ?? lap.LapTime) - (lap.IracingBestLapTime ?? reference.ReportedLapTime ?? reference.LapTime);
        var consistent = iracingFinal is { } f && Math.Abs(f - expected) <= SelfConsistencyTolerance;

        return new DeltaCheck(
            lap.Id,
            reference.Id,
            errors.Count,
            Signal.Median(errors),
            Signal.Quantile(errors, 0.95),
            errors.Max(),
            lap.LapTime - reference.LapTime,
            iracingFinal,
            consistent);
    }

    /// <summary>
    /// iRacing's own optimal lap time, read back from <c>LapDeltaToOptimalLap</c> at the line of the latest lap that
    /// carries it (lap time minus the delta). iRacing builds its optimal lap from its own sectors and may include laps
    /// from before the recording, so this is a plausibility check for our composite, not an exact match.
    /// </summary>
    public static double? IracingOptimalLapTime(IReadOnlyList<LapTrace> sessionLaps)
    {
        ArgumentNullException.ThrowIfNull(sessionLaps);
        for (var k = sessionLaps.Count - 1; k >= 0; k--)
        {
            var lap = sessionLaps[k];
            if (!lap.IsValid || lap.IracingDeltaToOptimal is not { } delta)
                continue;
            for (var i = delta.Length - 1; i >= 0; i--)
            {
                if (lap.DistanceAt(i) > lap.Length - 1 || !double.IsFinite(delta[i]))
                    continue;
                var time = (lap.ReportedLapTime ?? lap.LapTime) - delta[i];
                return time > 0 ? time : null;
            }
        }
        return null;
    }
}
