using CoCoach.Analysis.Alignment;

namespace CoCoach.Analysis.Corners;

/// <summary>Driver-input events on a lap trace, found by index on the distance grid.</summary>
internal static class Inputs
{
    /// <summary>Brake above this is "on the brakes" (research report §4.2: 5 %).</summary>
    public const double BrakeOn = 0.05;

    /// <summary>Throttle above this, sustained, is a throttle pickup (§4.2: 10 %).</summary>
    public const double ThrottlePickup = 0.10;

    /// <summary>
    /// A brake application interrupted for less than this many metres is still one application
    /// (a driver easing off and squeezing again while trail-braking), metres.
    /// </summary>
    public const double BrakeMergeGap = 25;

    /// <summary>Throttle at or above this is full throttle.</summary>
    public const double FullThrottle = 0.98;

    /// <summary>
    /// A brake application whose peak is below this fraction of the strongest one in the search window is a dab
    /// (a mid-corner correction), not the braking for the corner.
    /// </summary>
    public const double MinPeakShare = 0.25;

    /// <summary>
    /// The brake application that is under way at <paramref name="to"/>, or the last one that started before it,
    /// searching from <paramref name="from"/>. Runs separated by no more than <paramref name="mergeGap"/> samples
    /// count as one application, and applications peaking below <see cref="MinPeakShare"/> of the strongest one
    /// are skipped. An application already under way at <paramref name="from"/> is followed back to its true
    /// onset, but no further than <paramref name="earliest"/>.
    /// </summary>
    /// <returns>Onset and release indices (release is the first sample back below <see cref="BrakeOn"/>), or null.</returns>
    public static (int Onset, int Release)? BrakeApplication(LapTrace lap, int from, int to, int mergeGap, int earliest = 0)
    {
        if (lap.Brake is not { } brake)
            return null;
        from = Math.Max(0, from);
        to = Math.Min(lap.Count - 1, to);
        if (from > to)
            return null;

        var runs = new List<(int Onset, int LastOn, double Peak)>();
        for (var i = from; i <= to; i++)
        {
            if (!(brake[i] > BrakeOn))
                continue;
            if (runs.Count == 0 || i - runs[^1].LastOn > mergeGap)
                runs.Add((i, i, brake[i]));
            else
                runs[^1] = (runs[^1].Onset, i, Math.Max(runs[^1].Peak, brake[i]));
        }
        if (runs.Count == 0)
            return null;
        var strongest = runs.Max(r => r.Peak);
        var (onset, lastOn, _) = runs.FindLast(r => r.Peak >= MinPeakShare * strongest);

        // braking that began before the search window (in the previous corner of a chicane)
        if (onset == from)
        {
            var firstOn = from;
            for (var i = from - 1; i >= Math.Max(0, earliest); i--)
            {
                if (brake[i] > BrakeOn)
                    firstOn = i;
                else if (firstOn - i > mergeGap)
                    break;
            }
            onset = firstOn;
        }

        // follow the application forward to its release
        for (var i = lastOn + 1; i < lap.Count; i++)
        {
            if (brake[i] > BrakeOn)
            {
                lastOn = i;
                continue;
            }
            if (i - lastOn > mergeGap)
                break;
        }
        return (onset, Math.Min(lap.Count - 1, lastOn + 1));
    }

    /// <summary>First index in [from, to] where <paramref name="values"/> stays at or above <paramref name="level"/> for <paramref name="sustain"/> samples.</summary>
    public static int? FirstSustained(double[]? values, int from, int to, double level, int sustain)
    {
        if (values is null)
            return null;
        from = Math.Max(0, from);
        to = Math.Min(values.Length - 1, to);
        var run = 0;
        for (var i = from; i < values.Length; i++)
        {
            if (values[i] >= level)
            {
                run++;
                if (run >= sustain)
                    return i - run + 1 <= to ? i - run + 1 : null;
            }
            else
            {
                run = 0;
                if (i > to)
                    return null;
            }
        }
        return null;
    }

    /// <summary>First index in [from, to] where <paramref name="values"/> is below <paramref name="level"/>.</summary>
    public static int? FirstBelow(double[]? values, int from, int to, double level)
    {
        if (values is null)
            return null;
        for (var i = Math.Max(0, from); i <= Math.Min(values.Length - 1, to); i++)
        {
            if (values[i] < level)
                return i;
        }
        return null;
    }
}
