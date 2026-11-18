using CoCoach.Analysis.Alignment;
using CoCoach.Analysis.Track;

namespace CoCoach.Analysis.Corners;

/// <summary>
/// What one lap did in one corner (research report §4.2 key points). Positions are <c>LapDist</c> metres,
/// speeds m/s, times seconds. A key point the lap did not have (no braking, never lifted) is null.
/// </summary>
public sealed record CornerKeyPoints
{
    public required Corner Corner { get; init; }
    public required LapId Lap { get; init; }

    /// <summary>First sample above 5 % brake of the application used for this corner.</summary>
    public double? BrakePoint { get; init; }

    /// <summary>Highest brake pedal, 0 to 1.</summary>
    public double? PeakBrake { get; init; }

    public double? PeakBrakeAt { get; init; }

    /// <summary>Where the brake drops back below 5 %.</summary>
    public double? BrakeRelease { get; init; }

    /// <summary>
    /// Average release rate from the end of the peak (last sample at 90 % of peak) to the release, pedal fraction per second.
    /// Low values are a long, gradual trail-brake release.
    /// </summary>
    public double? ReleaseRate { get; init; }

    /// <summary>How far past the corner's geometric turn-in the brake is held, metres (negative = released before).</summary>
    public double? TrailBrakeDistance { get; init; }

    /// <summary>Where |steering| first rises past 30 % of its peak in this corner.</summary>
    public double? TurnInPoint { get; init; }

    /// <summary>Where speed is lowest in the corner (the apex as a driver feels it).</summary>
    public required double ApexPoint { get; init; }

    /// <summary>
    /// The minimum speed sits at an end of the window searched for it (taken flat out, or braking for the next corner),
    /// so <see cref="ApexPoint"/> marks the window edge rather than an apex the driver chose.
    /// </summary>
    public bool ApexAtWindowEdge { get; init; }

    public required double MinSpeed { get; init; }

    /// <summary>Speed at the corner's geometric turn-in.</summary>
    public required double EntrySpeed { get; init; }

    /// <summary>Speed at the end of the corner window.</summary>
    public required double ExitSpeed { get; init; }

    /// <summary>Throttle above 10 %, sustained, after the lift and brake release.</summary>
    public double? ThrottlePickup { get; init; }

    /// <summary>Back to full throttle after the apex.</summary>
    public double? FullThrottle { get; init; }

    /// <summary>Time from window start to turn-in, turn-in to exit, and exit to window end.</summary>
    public required double EntryTime { get; init; }

    public required double MidTime { get; init; }
    public required double ExitTime { get; init; }

    public double Time => EntryTime + MidTime + ExitTime;

    /// <summary>Metres driven on kerbs (Rumble surface) in the window; null when the recording has no surface material.</summary>
    public double? KerbMetres { get; init; }

    /// <summary>The car went off track in the window.</summary>
    public bool OffTrack { get; init; }

    /// <summary>Lateral position relative to the centreline at the corner's turn-in, apex and exit, metres (positive = left). GPS maps only.</summary>
    public double? LineAtTurnIn { get; init; }

    public double? LineAtApex { get; init; }
    public double? LineAtExit { get; init; }
}

/// <summary>Finds <see cref="CornerKeyPoints"/> for every corner of a lap.</summary>
public static class CornerKeyPointFinder
{
    public static IReadOnlyList<CornerKeyPoints> Find(LapTrace lap, IReadOnlyList<Corner> corners, TrackModel? model = null, CornerDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(lap);
        ArgumentNullException.ThrowIfNull(corners);
        options ??= new CornerDetectionOptions();
        var offsets = model?.LateralOffsets(lap);
        var result = new List<CornerKeyPoints>(corners.Count);
        for (var c = 0; c < corners.Count; c++)
        {
            var prev = c > 0 ? corners[c - 1] : null;
            var next = c + 1 < corners.Count ? corners[c + 1] : null;
            result.Add(FindOne(lap, corners[c], prev, next, offsets, options));
        }
        return result;
    }

    static CornerKeyPoints FindOne(LapTrace lap, Corner corner, Corner? prev, Corner? next, double[]? offsets, CornerDetectionOptions options)
    {
        var step = lap.Step;
        int M(double metres) => Math.Max(1, (int)Math.Round(metres / step));
        int I(double distance) => lap.IndexAt(distance);
        double D(int index) => lap.DistanceAt(index);

        var start = I(corner.Start);
        var turnIn = I(corner.TurnIn);
        var apexGeo = I(corner.Apex);
        var exit = I(corner.Exit);
        var end = I(corner.End);
        var earliest = Math.Max(prev is null ? 0 : I(prev.Apex), turnIn - M(options.BrakeLookback));
        var searchFrom = Math.Max(prev is null ? 0 : I(prev.Exit), earliest);
        var searchTo = next is null ? lap.Count - 1 : I(next.TurnIn);

        // braking
        double? brakePoint = null, peak = null, peakAt = null, release = null, releaseRate = null, trail = null;
        if (Inputs.BrakeApplication(lap, searchFrom, apexGeo, M(Inputs.BrakeMergeGap), earliest) is { } application && lap.Brake is { } brake)
        {
            brakePoint = D(application.Onset);
            var releaseIndex = application.Release;
            release = D(releaseIndex);
            var peakIndex = application.Onset;
            for (var i = application.Onset; i < releaseIndex; i++)
            {
                if (brake[i] > brake[peakIndex])
                    peakIndex = i;
            }
            peak = brake[peakIndex];
            peakAt = D(peakIndex);
            var holdEnd = peakIndex;
            for (var i = peakIndex; i < releaseIndex; i++)
            {
                if (brake[i] >= 0.9 * peak)
                    holdEnd = i;
            }
            var dt = lap.Time[releaseIndex] - lap.Time[holdEnd];
            if (dt > 0)
                releaseRate = (brake[holdEnd] - brake[releaseIndex]) / dt;
            trail = release - corner.TurnIn;
        }

        // turn-in: steering past 30 % of its peak in the direction of the corner
        double? turnInPoint = null;
        var steer = lap.Steering ?? TrackModelBuilder.YawCurvature(lap);
        if (steer is not null)
        {
            double peakSteer = 0;
            for (var i = start; i <= exit; i++)
            {
                if (corner.Sign * steer[i] > peakSteer)
                    peakSteer = corner.Sign * steer[i];
            }
            if (peakSteer > 0)
            {
                var from = Math.Max(searchFrom, start - M(50));
                for (var i = from; i <= apexGeo; i++)
                {
                    if (corner.Sign * steer[i] >= 0.3 * peakSteer)
                    {
                        turnInPoint = D(i);
                        break;
                    }
                }
            }
        }

        // apex: lowest speed around the geometric corner
        var apexFrom = Math.Max(start, turnIn - M(30));
        var apexTo = Math.Min(end, exit + M(30));
        var apex = apexFrom;
        for (var i = apexFrom; i <= apexTo; i++)
        {
            if (lap.Speed[i] < lap.Speed[apex])
                apex = i;
        }

        // throttle: a pickup only exists after a lift, and after the last touch of the brakes before the apex
        double? pickup = null, full = null;
        if (lap.Throttle is { } throttle && Inputs.FirstBelow(throttle, searchFrom, apex, Inputs.ThrottlePickup) is { } lift)
        {
            var lastBrake = Inputs.BrakeApplication(lap, searchFrom, apexGeo, M(Inputs.BrakeMergeGap), earliest, minPeakShare: 0);
            var from = Math.Max(lift, lastBrake?.Release ?? -1);
            if (Inputs.FirstSustained(throttle, from, searchTo, Inputs.ThrottlePickup, M(10)) is { } p)
            {
                pickup = D(p);
                if (Inputs.FirstSustained(throttle, Math.Max(p, apex), searchTo, Inputs.FullThrottle, M(5)) is { } f)
                    full = D(f);
            }
        }
        else if (lap.Throttle is { } t && Inputs.FirstBelow(t, searchFrom, apex, Inputs.FullThrottle) is not null &&
                 Inputs.FirstSustained(t, apex, searchTo, Inputs.FullThrottle, M(5)) is { } f)
        {
            // a partial lift: no pickup, but a return to full throttle
            full = D(f);
        }

        double? kerb = null;
        if (lap.SurfaceMaterial is { } material && material.Any(double.IsFinite))
        {
            var count = 0;
            for (var i = start; i <= end; i++)
            {
                if (SurfaceMaterials.IsKerb(material[i]))
                    count++;
            }
            kerb = count * step;
        }

        var offTrack = false;
        for (var i = start; i <= end && !offTrack; i++)
            offTrack = TrackModelBuilder.IsOffTrack(lap, i);

        double? Line(int i) => offsets is not null && i < offsets.Length && double.IsFinite(offsets[i]) ? offsets[i] : null;

        return new CornerKeyPoints
        {
            Corner = corner,
            Lap = lap.Id,
            BrakePoint = brakePoint,
            PeakBrake = peak,
            PeakBrakeAt = peakAt,
            BrakeRelease = release,
            ReleaseRate = releaseRate,
            TrailBrakeDistance = trail,
            TurnInPoint = turnInPoint,
            ApexPoint = D(apex),
            ApexAtWindowEdge = apex == apexFrom || apex == apexTo,
            MinSpeed = lap.Speed[apex],
            EntrySpeed = lap.Speed[turnIn],
            ExitSpeed = lap.Speed[end],
            ThrottlePickup = pickup,
            FullThrottle = full,
            EntryTime = lap.TimeBetween(corner.Start, corner.TurnIn),
            MidTime = lap.TimeBetween(corner.TurnIn, corner.Exit),
            ExitTime = lap.TimeBetween(corner.Exit, corner.End),
            KerbMetres = kerb,
            OffTrack = offTrack,
            LineAtTurnIn = Line(turnIn),
            LineAtApex = Line(apexGeo),
            LineAtExit = Line(exit),
        };
    }
}
