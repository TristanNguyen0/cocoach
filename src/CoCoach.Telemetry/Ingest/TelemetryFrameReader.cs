namespace CoCoach.Telemetry.Ingest;

/// <summary>
/// Builds a <see cref="TelemetryFrame"/> from a by-name variable lookup.
/// A lookup returns the boxed scalar or array for a name, or <see langword="null"/> when the
/// source does not carry it. Values of an unexpected type are treated as missing.
/// </summary>
public static class TelemetryFrameReader
{
    public static TelemetryFrame Read(Func<string, object?> lookup, long index, long arrivalTimestamp)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        return new TelemetryFrame
        {
            Index = index,
            ArrivalTimestamp = arrivalTimestamp,
            SessionTime = AsDouble(lookup(TelemetryVars.SessionTime)),
            SessionTick = AsInt(lookup(TelemetryVars.SessionTick)),
            Lap = AsInt(lookup(TelemetryVars.Lap)),
            LapCompleted = AsInt(lookup(TelemetryVars.LapCompleted)),
            LapDist = AsFloat(lookup(TelemetryVars.LapDist)),
            LapDistPct = AsFloat(lookup(TelemetryVars.LapDistPct)),
            LapCurrentLapTime = AsFloat(lookup(TelemetryVars.LapCurrentLapTime)),
            LapLastLapTime = AsFloat(lookup(TelemetryVars.LapLastLapTime)),
            Speed = AsFloat(lookup(TelemetryVars.Speed)),
            Throttle = AsFloat(lookup(TelemetryVars.Throttle)),
            Brake = AsFloat(lookup(TelemetryVars.Brake)),
            SteeringWheelAngle = AsFloat(lookup(TelemetryVars.SteeringWheelAngle)),
            Gear = AsInt(lookup(TelemetryVars.Gear)),
            IsOnTrack = AsBool(lookup(TelemetryVars.IsOnTrack)),
            OnPitRoad = AsBool(lookup(TelemetryVars.OnPitRoad)),
            PlayerTrackSurface = AsInt(lookup(TelemetryVars.PlayerTrackSurface)),
            PlayerTrackSurfaceMaterial = AsInt(lookup(TelemetryVars.PlayerTrackSurfaceMaterial)),
        };
    }

    internal static double? AsDouble(object? value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        _ => null,
    };

    internal static float? AsFloat(object? value) => value switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        _ => null,
    };

    internal static int? AsInt(object? value) => value switch
    {
        int i => i,
        byte b => b,
        bool b => b ? 1 : 0,
        _ => null,
    };

    internal static bool? AsBool(object? value) => value switch
    {
        bool b => b,
        byte b => b != 0,
        int i => i != 0,
        _ => null,
    };
}
