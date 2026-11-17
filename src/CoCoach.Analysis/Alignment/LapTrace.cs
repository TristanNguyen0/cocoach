namespace CoCoach.Analysis.Alignment;

/// <summary>Identifies one lap: which loaded session it came from and iRacing's lap number in it.</summary>
/// <param name="Session">Zero-based position of the session in the analysed set.</param>
/// <param name="SessionLabel">The session's label (its file name).</param>
/// <param name="Lap">iRacing's lap number.</param>
public sealed record LapId(int Session, string SessionLabel, int Lap)
{
    /// <summary>"L6", or "S2 L6" when several sessions are analysed together.</summary>
    public string Display(bool multipleSessions) =>
        multipleSessions ? $"S{Session + 1} L{Lap}" : $"L{Lap}";

    public override string ToString() => Display(multipleSessions: true);
}

/// <summary>
/// One complete lap resampled onto a fixed distance grid: sample <c>i</c> is at <c>i * Step</c> metres
/// from the start/finish line. Every lap of a track shares the same grid, so channels of two laps can be
/// compared index by index (distance alignment, not time alignment).
/// </summary>
/// <remarks>
/// Continuous channels are linearly interpolated between the two frames either side of a grid point;
/// discrete channels (surface, material, delta-OK flags) take the earlier frame's value.
/// A channel the source does not carry is <see langword="null"/>; a missing value inside a carried
/// channel is NaN.
/// </remarks>
public sealed class LapTrace
{
    public required LapId Id { get; init; }

    /// <summary>Lap time in seconds, from the interpolated start/finish crossings.</summary>
    public required double LapTime { get; init; }

    /// <summary>iRacing's own <c>LapLastLapTime</c> for this lap, when published.</summary>
    public double? ReportedLapTime { get; init; }

    /// <summary>
    /// iRacing's <c>LapBestLapTime</c> while this lap was driven: the lap its <c>LapDeltaToBestLap</c> compares against.
    /// </summary>
    public double? IracingBestLapTime { get; init; }

    /// <summary>Track length the grid spans, metres.</summary>
    public required double Length { get; init; }

    /// <summary>Grid spacing, metres.</summary>
    public required double Step { get; init; }

    /// <summary>Seconds since the start/finish crossing at each grid point; strictly increasing.</summary>
    public required double[] Time { get; init; }

    /// <summary>Speed, m/s.</summary>
    public required double[] Speed { get; init; }

    public double[]? Throttle { get; init; }
    public double[]? Brake { get; init; }

    /// <summary>Steering wheel angle, radians (positive = left).</summary>
    public double[]? Steering { get; init; }

    /// <summary>Yaw rate, rad/s (positive = left).</summary>
    public double[]? YawRate { get; init; }

    /// <summary>GPS latitude and longitude, degrees; NaN where the recording has no fix.</summary>
    public double[]? Lat { get; init; }

    public double[]? Lon { get; init; }
    public double[]? Alt { get; init; }

    /// <summary><c>irsdk_TrkLoc</c> per grid point (0 = off track, 3 = on track).</summary>
    public double[]? TrackSurface { get; init; }

    /// <summary><c>irsdk_TrkSurf</c> per grid point (11-14 = kerbs).</summary>
    public double[]? SurfaceMaterial { get; init; }

    /// <summary>iRacing's <c>LapDeltaToBestLap</c>, NaN where its <c>_OK</c> flag was false.</summary>
    public double[]? IracingDeltaToBest { get; init; }

    /// <summary>iRacing's <c>LapDeltaToOptimalLap</c>, NaN where its <c>_OK</c> flag was false.</summary>
    public double[]? IracingDeltaToOptimal { get; init; }

    /// <summary>The car was on pit road during the lap.</summary>
    public bool PitRoad { get; init; }

    /// <summary>The car went off track (<c>PlayerTrackSurface == 0</c>) during the lap.</summary>
    public bool OffTrack { get; init; }

    /// <summary>A lap that can serve as a reference: no pit road, no off-track excursion.</summary>
    public bool IsValid => !PitRoad && !OffTrack;

    /// <summary>For composite laps: the lap each segment was taken from. Empty for a recorded lap.</summary>
    public IReadOnlyList<LapId> ComposedFrom { get; init; } = [];

    public int Count => Time.Length;

    /// <summary>Distance of grid point <paramref name="index"/>, metres.</summary>
    public double DistanceAt(int index) => index * Step;

    /// <summary>Grid index at or before <paramref name="distance"/>, clamped to the grid.</summary>
    public int IndexAt(double distance) => Math.Clamp((int)Math.Floor(distance / Step + 1e-9), 0, Count - 1);

    /// <summary>
    /// Seconds since the line at <paramref name="distance"/> metres, interpolated between grid points;
    /// <see cref="Length"/> returns <see cref="LapTime"/>.
    /// </summary>
    public double ElapsedAt(double distance)
    {
        if (distance <= 0)
            return 0;
        if (distance >= Length)
            return LapTime;
        var i = IndexAt(distance);
        var d0 = DistanceAt(i);
        var (d1, t1) = i + 1 < Count ? (DistanceAt(i + 1), Time[i + 1]) : (Length, LapTime);
        return Time[i] + (t1 - Time[i]) * (distance - d0) / (d1 - d0);
    }

    /// <summary>Seconds spent between two distances (start inclusive, end exclusive).</summary>
    public double TimeBetween(double from, double to) => ElapsedAt(to) - ElapsedAt(from);
}
