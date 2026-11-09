namespace CoCoach.Telemetry.Session;

/// <summary>
/// The privacy-safe subset of session info: the track and the player's own car.
/// It deliberately holds no driver names, custids, or anything about other cars,
/// so it is safe to print, store, or hand to an LLM.
/// </summary>
public sealed record SessionFacts
{
    public string? TrackDisplayName { get; init; }
    public string? TrackConfigName { get; init; }
    public string? TrackName { get; init; }
    public int? TrackId { get; init; }

    /// <summary>As iRacing writes it, for example "5.79 km".</summary>
    public string? TrackLength { get; init; }

    public string? TrackCity { get; init; }
    public string? TrackCountry { get; init; }

    /// <summary>"Race", "Test", "Time Trial", ... from WeekendInfo.EventType.</summary>
    public string? EventType { get; init; }

    /// <summary>True for an official (ranked) session.</summary>
    public bool? Official { get; init; }

    /// <summary>The session types in this event, in order (for example Practice, Qualify, Race).</summary>
    public IReadOnlyList<string> SessionTypes { get; init; } = [];

    public int? PlayerCarIdx { get; init; }
    public string? PlayerCarScreenName { get; init; }
    public string? PlayerCarPath { get; init; }
    public int? PlayerCarId { get; init; }
    public string? PlayerCarClassShortName { get; init; }

    /// <summary>Short display string for the track, including its configuration when there is one.</summary>
    public string TrackLabel =>
        string.IsNullOrWhiteSpace(TrackConfigName)
            ? TrackDisplayName ?? TrackName ?? "unknown track"
            : $"{TrackDisplayName ?? TrackName ?? "unknown track"} ({TrackConfigName})";
}
