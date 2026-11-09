namespace CoCoach.Telemetry.Ingest;

/// <summary>
/// One telemetry row copied out of the live feed or an .ibt file.
/// Every channel is nullable: the variable set differs per car and per sim version,
/// so a channel the source does not carry is <see langword="null"/>, never an error.
/// </summary>
public sealed record TelemetryFrame
{
    /// <summary>Zero-based position of this frame in the source stream.</summary>
    public long Index { get; init; }

    /// <summary>
    /// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> taken when the row was copied out of the source.
    /// Used to measure arrival-to-action latency.
    /// </summary>
    public long ArrivalTimestamp { get; init; }

    /// <summary>Seconds since session start (sim clock).</summary>
    public double? SessionTime { get; init; }

    /// <summary>Sim tick counter.</summary>
    public int? SessionTick { get; init; }

    /// <summary>Lap number the player car is on. Increments when the car crosses the start/finish line.</summary>
    public int? Lap { get; init; }

    /// <summary>Laps completed by the player car.</summary>
    public int? LapCompleted { get; init; }

    /// <summary>Distance from start/finish along the lap, metres.</summary>
    public float? LapDist { get; init; }

    /// <summary>Fraction of the lap completed, 0 to 1.</summary>
    public float? LapDistPct { get; init; }

    /// <summary>iRacing's own estimate of the current lap time, seconds.</summary>
    public float? LapCurrentLapTime { get; init; }

    /// <summary>iRacing's last lap time, seconds. Non-positive when there is no valid last lap.</summary>
    public float? LapLastLapTime { get; init; }

    /// <summary>Ground speed, metres per second.</summary>
    public float? Speed { get; init; }

    /// <summary>Throttle pedal, 0 (off) to 1 (full).</summary>
    public float? Throttle { get; init; }

    /// <summary>Brake pedal, 0 (off) to 1 (full).</summary>
    public float? Brake { get; init; }

    /// <summary>Steering wheel angle, radians.</summary>
    public float? SteeringWheelAngle { get; init; }

    /// <summary>Gear: -1 reverse, 0 neutral, 1.. forward.</summary>
    public int? Gear { get; init; }

    /// <summary>True when the player is in the car with physics running on track.</summary>
    public bool? IsOnTrack { get; init; }

    /// <summary>True when the player car is between the pit-lane cones.</summary>
    public bool? OnPitRoad { get; init; }

    /// <summary><c>irsdk_TrkLoc</c>: -1 not in world, 0 off track, 1 in pit stall, 2 approaching pits, 3 on track.</summary>
    public int? PlayerTrackSurface { get; init; }

    /// <summary><c>irsdk_TrkSurf</c> material under the player car (Rumble1-4 are kerbs).</summary>
    public int? PlayerTrackSurfaceMaterial { get; init; }
}
