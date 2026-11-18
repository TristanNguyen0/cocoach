using CoCoach.Analysis.Alignment;
using CoCoach.Analysis.Corners;

namespace CoCoach.Analysis.Comparison;

/// <summary>
/// One lap's corner against the reference's (research report §4.3). Sign conventions:
/// time deltas are positive when the lap lost time; brake-point offset is positive when the lap braked
/// <em>earlier</em>; apex, throttle-pickup and full-throttle offsets are positive when the lap was
/// <em>later</em>; speed differences are lap minus reference; line offset is positive toward the inside.
/// </summary>
public sealed record CornerComparison
{
    public required Corner Corner { get; init; }
    public required CornerKeyPoints Lap { get; init; }
    public required CornerKeyPoints Reference { get; init; }

    /// <summary>Time lost (+) or gained (-) over the whole corner window, seconds.</summary>
    public double TimeDelta => EntryDelta + MidDelta + ExitDelta;

    public double EntryDelta => Lap.EntryTime - Reference.EntryTime;
    public double MidDelta => Lap.MidTime - Reference.MidTime;
    public double ExitDelta => Lap.ExitTime - Reference.ExitTime;

    /// <summary>Metres the lap braked before (+) or after (-) the reference.</summary>
    public double? BrakePointOffset => Diff(Reference.BrakePoint, Lap.BrakePoint);

    public double? PeakBrakeDifference => Diff(Lap.PeakBrake, Reference.PeakBrake);

    /// <summary>Lap's brake release rate minus the reference's, pedal fraction per second.</summary>
    public double? ReleaseRateDifference => Diff(Lap.ReleaseRate, Reference.ReleaseRate);

    public double EntrySpeedDifference => Lap.EntrySpeed - Reference.EntrySpeed;
    public double MinSpeedDifference => Lap.MinSpeed - Reference.MinSpeed;
    public double ExitSpeedDifference => Lap.ExitSpeed - Reference.ExitSpeed;

    /// <summary>Metres the lap's minimum-speed point is after (+) the reference's; null when either lies at the corner window edge.</summary>
    public double? ApexOffset => Lap.ApexAtWindowEdge || Reference.ApexAtWindowEdge ? null : Lap.ApexPoint - Reference.ApexPoint;

    /// <summary>Metres the lap picked up the throttle after (+) the reference.</summary>
    public double? ThrottlePickupOffset => Diff(Lap.ThrottlePickup, Reference.ThrottlePickup);

    public double? FullThrottleOffset => Diff(Lap.FullThrottle, Reference.FullThrottle);

    /// <summary>Metres the lap was further inside (+) the corner than the reference at the geometric apex. GPS maps only.</summary>
    public double? ApexLineOffset => Inside(Lap.LineAtApex, Reference.LineAtApex);

    public double? TurnInLineOffset => Inside(Lap.LineAtTurnIn, Reference.LineAtTurnIn);
    public double? ExitLineOffset => Inside(Lap.LineAtExit, Reference.LineAtExit);

    static double? Diff(double? a, double? b) => a is { } x && b is { } y ? x - y : null;

    double? Inside(double? lap, double? reference) => Diff(lap, reference) is { } d ? d * Corner.Sign : null;
}

/// <summary>Distance-aligned delta time between a lap and a reference on the shared grid.</summary>
public static class DeltaTrace
{
    /// <summary>
    /// Seconds the lap is behind (+) or ahead of (-) the reference at each grid point:
    /// elapsed time since the line on the lap minus the same on the reference, at the same distance.
    /// </summary>
    public static double[] Compute(LapTrace lap, LapTrace reference)
    {
        ArgumentNullException.ThrowIfNull(lap);
        ArgumentNullException.ThrowIfNull(reference);
        var n = Math.Min(lap.Count, reference.Count);
        var delta = new double[n];
        for (var i = 0; i < n; i++)
            delta[i] = lap.Time[i] - reference.Time[i];
        return delta;
    }

    /// <summary>Pairs each lap corner with the reference's.</summary>
    public static IReadOnlyList<CornerComparison> CompareCorners(IReadOnlyList<CornerKeyPoints> lap, IReadOnlyList<CornerKeyPoints> reference)
    {
        ArgumentNullException.ThrowIfNull(lap);
        ArgumentNullException.ThrowIfNull(reference);
        var byNumber = reference.ToDictionary(r => r.Corner.Number);
        return lap
            .Where(l => byNumber.ContainsKey(l.Corner.Number))
            .Select(l => new CornerComparison { Corner = l.Corner, Lap = l, Reference = byNumber[l.Corner.Number] })
            .ToArray();
    }
}
