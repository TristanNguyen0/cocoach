using CoCoach.Analysis.Alignment;
using CoCoach.Analysis.Track;

namespace CoCoach.Analysis.Corners;

/// <summary>Tuning for <see cref="CornerDetector"/>. Distances in metres, curvature in 1/m.</summary>
public sealed record CornerDetectionOptions
{
    /// <summary>Half-width of the triangular smoothing applied to curvature.</summary>
    public double SmoothingHalfWindow { get; init; } = 8;

    /// <summary>A corner starts where |curvature| exceeds this (radius 250 m).</summary>
    public double EnterCurvature { get; init; } = 1.0 / 250;

    /// <summary>A corner extends while |curvature| stays above this with the same sign (radius 600 m).</summary>
    public double ExitCurvature { get; init; } = 1.0 / 600;

    /// <summary>Same-direction regions closer than this are one corner.</summary>
    public double MergeGap { get; init; } = 25;

    /// <summary>
    /// A same-direction region is split into two corners at a curvature valley below this fraction of both
    /// neighbouring peaks (a double-apex or compound corner), when each part still turns enough to be a corner.
    /// </summary>
    public double SplitValleyRatio { get; init; } = 0.4;

    /// <summary>Regions that turn through less than this, degrees, are not corners.</summary>
    public double MinHeadingChange { get; init; } = 15;

    /// <summary>How far before turn-in to look for the braking onset.</summary>
    public double BrakeLookback { get; init; } = 400;

    /// <summary>The analysis window starts this far before the typical brake point.</summary>
    public double WindowMargin { get; init; } = 10;

    /// <summary>Minimum length of the entry and exit sub-segments.</summary>
    public double MinApproach { get; init; } = 20;
}

/// <summary>Result of comparing the corner count with WeekendInfo.TrackNumTurns.</summary>
/// <param name="Found">Corners detected.</param>
/// <param name="Expected">iRacing's TrackNumTurns, when known.</param>
/// <param name="Plausible">True when there is no expectation, or the count is within max(2, 35 %) of it.</param>
public sealed record CornerCountCheck(int Found, int? Expected, bool Plausible)
{
    public static CornerCountCheck For(int found, int? expected)
    {
        if (expected is not { } e || e <= 0)
            return new CornerCountCheck(found, expected, true);
        var tolerance = Math.Max(2, 0.35 * e);
        return new CornerCountCheck(found, expected, Math.Abs(found - e) <= tolerance);
    }
}

/// <summary>
/// Deterministic corner segmentation (research report §4.2): curvature against <c>LapDist</c>, smoothed and
/// thresholded with hysteresis, merged across short gaps, split at sign changes (esses), then refined
/// with the laps' typical braking onset and full-throttle return.
/// </summary>
/// <remarks>Kerb channels (<c>Tire*_RumblePitch</c>) are not used: they are usually zero.</remarks>
public static class CornerDetector
{
    public static IReadOnlyList<Corner> Detect(TrackModel model, IReadOnlyList<LapTrace> cleanLaps, CornerDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(cleanLaps);
        options ??= new CornerDetectionOptions();
        var step = model.Step;
        var n = model.Count;
        int M(double metres) => Math.Max(1, (int)Math.Round(metres / step));

        var k = Signal.Smooth(model.Curvature, M(options.SmoothingHalfWindow), circular: true);
        var regions = Threshold(k, options.EnterCurvature, options.ExitCurvature);
        regions = Merge(regions, M(options.MergeGap));
        var minTurn = options.MinHeadingChange * Math.PI / 180;
        regions = regions.SelectMany(r => Split(k, r, step, options.SplitValleyRatio, minTurn)).ToList();

        var geometric = new List<(int A, int B, int Sign, int Apex, double Turn, double Peak)>();
        foreach (var (a, b, sign) in regions)
        {
            double turn = 0, peak = 0;
            var apex = a;
            for (var i = a; i <= b; i++)
            {
                turn += k[i] * step;
                if (sign * k[i] > peak)
                {
                    peak = sign * k[i];
                    apex = i;
                }
            }
            if (Math.Abs(turn) * 180 / Math.PI < options.MinHeadingChange)
                continue;
            geometric.Add((a, b, sign, apex, Math.Abs(turn) * 180 / Math.PI, peak));
        }

        // window: from the typical braking onset to the typical return to full throttle
        var starts = new double[geometric.Count];
        var ends = new double[geometric.Count];
        for (var c = 0; c < geometric.Count; c++)
        {
            var (a, b, _, apex, _, _) = geometric[c];
            var prevExit = c > 0 ? geometric[c - 1].B : 0;
            var nextTurnIn = c + 1 < geometric.Count ? geometric[c + 1].A : n - 1;

            // braking for a chicane's second half can start in the first half: look back to the previous apex
            var from = Math.Max(prevExit, a - M(options.BrakeLookback));
            var earliest = Math.Max(c > 0 ? geometric[c - 1].Apex : 0, a - M(options.BrakeLookback));
            var onsets = new List<double>();
            var fulls = new List<double>();
            foreach (var lap in cleanLaps)
            {
                if (Inputs.BrakeApplication(lap, from, apex, M(Inputs.BrakeMergeGap), earliest) is { } brake)
                    onsets.Add(brake.Onset);
                if (Inputs.FirstBelow(lap.Throttle, from, apex, Inputs.FullThrottle) is not null &&
                    Inputs.FirstSustained(lap.Throttle, apex, nextTurnIn, Inputs.FullThrottle, M(5)) is { } full)
                    fulls.Add(full);
            }

            // half the clean laps must brake for the corner to count as a braking corner
            var start = a - M(options.MinApproach);
            if (onsets.Count * 2 >= cleanLaps.Count && onsets.Count > 0)
                start = Math.Min(start, (int)Signal.Median(onsets) - M(options.WindowMargin));
            var end = b + M(options.MinApproach);
            if (fulls.Count * 2 >= cleanLaps.Count && fulls.Count > 0)
                end = Math.Max(end, (int)Signal.Median(fulls) + M(options.WindowMargin));

            starts[c] = Math.Max(prevExit, start);
            ends[c] = Math.Min(Math.Min(n - 1, nextTurnIn), end);
        }

        // neighbouring windows must not overlap: meet halfway, within the straight between them
        for (var c = 0; c + 1 < geometric.Count; c++)
        {
            if (ends[c] <= starts[c + 1])
                continue;
            var meet = Math.Round((ends[c] + starts[c + 1]) / 2);
            meet = Math.Clamp(meet, geometric[c].B, geometric[c + 1].A);
            ends[c] = meet;
            starts[c + 1] = meet;
        }

        var corners = new List<Corner>();
        for (var c = 0; c < geometric.Count; c++)
        {
            var (a, b, sign, apex, turn, peak) = geometric[c];
            // segments tile the lap, meeting halfway between neighbouring windows
            var segStart = c == 0 ? 0 : Math.Round((ends[c - 1] + starts[c]) / 2) * step;
            var segEnd = c + 1 == geometric.Count ? model.Length : Math.Round((ends[c] + starts[c + 1]) / 2) * step;
            corners.Add(new Corner
            {
                Number = c + 1,
                Name = $"T{c + 1}",
                Direction = sign > 0 ? CornerDirection.Left : CornerDirection.Right,
                Start = starts[c] * step,
                TurnIn = a * step,
                Apex = apex * step,
                Exit = b * step,
                End = ends[c] * step,
                SegmentStart = segStart,
                SegmentEnd = segEnd,
                HeadingChange = turn,
                MinRadius = peak > 0 ? 1 / peak : double.PositiveInfinity,
            });
        }
        return corners;
    }

    // regions above the enter threshold, grown while the same-signed curvature stays above the exit threshold
    static List<(int A, int B, int Sign)> Threshold(double[] k, double enter, double exit)
    {
        var n = k.Length;
        var result = new List<(int, int, int)>();
        var i = 0;
        while (i < n)
        {
            if (Math.Abs(k[i]) <= enter)
            {
                i++;
                continue;
            }
            var sign = Math.Sign(k[i]);
            var a = i;
            while (a > 0 && sign * k[a - 1] > exit && (result.Count == 0 || a - 1 > result[^1].Item2))
                a--;
            var b = i;
            while (b + 1 < n && sign * k[b + 1] > exit)
                b++;
            result.Add((a, b, sign));
            i = b + 1;
        }
        return result;
    }

    // split at the deepest curvature valley between two peaks, recursively, while both parts are corners
    static IEnumerable<(int A, int B, int Sign)> Split(double[] k, (int A, int B, int Sign) region, double step, double ratio, double minTurn)
    {
        var (a, b, sign) = region;
        var best = -1;
        var bestDepth = 0.0;
        for (var v = a + 1; v < b; v++)
        {
            var value = sign * k[v];
            if (value > sign * k[v - 1] || value > sign * k[v + 1])
                continue;
            double left = 0, right = 0;
            for (var i = a; i < v; i++)
                left = Math.Max(left, sign * k[i]);
            for (var i = v + 1; i <= b; i++)
                right = Math.Max(right, sign * k[i]);
            var peak = Math.Min(left, right);
            if (peak <= 0 || value >= ratio * peak)
                continue;
            if (Turn(k, a, v, step) < minTurn || Turn(k, v + 1, b, step) < minTurn)
                continue;
            var depth = 1 - value / peak;
            if (depth > bestDepth)
            {
                bestDepth = depth;
                best = v;
            }
        }

        if (best < 0)
        {
            yield return region;
            yield break;
        }
        foreach (var part in Split(k, (a, best, sign), step, ratio, minTurn))
            yield return part;
        foreach (var part in Split(k, (best + 1, b, sign), step, ratio, minTurn))
            yield return part;
    }

    static double Turn(double[] k, int a, int b, double step)
    {
        double turn = 0;
        for (var i = a; i <= b; i++)
            turn += k[i] * step;
        return Math.Abs(turn);
    }

    static List<(int A, int B, int Sign)> Merge(List<(int A, int B, int Sign)> regions, int gap)
    {
        var merged = new List<(int A, int B, int Sign)>();
        foreach (var r in regions)
        {
            if (merged.Count > 0 && merged[^1].Sign == r.Sign && r.A - merged[^1].B <= gap)
                merged[^1] = (merged[^1].A, r.B, r.Sign);
            else
                merged.Add(r);
        }
        return merged;
    }
}
