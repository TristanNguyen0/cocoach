namespace CoCoach.Telemetry.Ingest;

/// <summary>
/// iRacing telemetry variable names used by the ingest layer.
/// Variables are always looked up by name, because the set differs per car.
/// </summary>
public static class TelemetryVars
{
    public const string SessionTime = nameof(SessionTime);
    public const string SessionTick = nameof(SessionTick);
    public const string Lap = nameof(Lap);
    public const string LapCompleted = nameof(LapCompleted);
    public const string LapDist = nameof(LapDist);
    public const string LapDistPct = nameof(LapDistPct);
    public const string LapCurrentLapTime = nameof(LapCurrentLapTime);
    public const string LapLastLapTime = nameof(LapLastLapTime);
    public const string Speed = nameof(Speed);
    public const string Throttle = nameof(Throttle);
    public const string Brake = nameof(Brake);
    public const string SteeringWheelAngle = nameof(SteeringWheelAngle);
    public const string Gear = nameof(Gear);
    public const string IsOnTrack = nameof(IsOnTrack);
    public const string OnPitRoad = nameof(OnPitRoad);
    public const string PlayerTrackSurface = nameof(PlayerTrackSurface);
    public const string PlayerTrackSurfaceMaterial = nameof(PlayerTrackSurfaceMaterial);

    /// <summary>Every variable a <see cref="TelemetryFrame"/> carries.</summary>
    public static IReadOnlyList<string> FrameVariables { get; } =
    [
        SessionTime, SessionTick, Lap, LapCompleted, LapDist, LapDistPct, LapCurrentLapTime,
        LapLastLapTime, Speed, Throttle, Brake, SteeringWheelAngle, Gear, IsOnTrack, OnPitRoad,
        PlayerTrackSurface, PlayerTrackSurfaceMaterial,
    ];
}
