using System.Globalization;
using CoCoach.Telemetry.Laps;
using CoCoach.Telemetry.Session;

namespace CoCoach.Cli.Commands;

internal static class Formatting
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string LapTime(double? seconds)
    {
        if (seconds is not { } s || !double.IsFinite(s) || s <= 0)
            return "-";
        var minutes = (int)(s / 60);
        var rest = s - minutes * 60;
        return minutes > 0
            ? string.Create(Inv, $"{minutes}:{rest:00.000}")
            : string.Create(Inv, $"{rest:0.000}");
    }

    public static string Kmh(float? metresPerSecond) =>
        metresPerSecond is { } v ? (v * 3.6).ToString("0.0", Inv) : "-";

    public static string Number(double? value) =>
        value is { } v ? v.ToString("G6", Inv) : "-";

    public static string TrackAndCar(SessionFacts? facts)
    {
        if (facts is null)
            return "track and car: (no session info)";
        var length = facts.TrackLength is null ? string.Empty : $", {facts.TrackLength}";
        var car = (facts.PlayerCarScreenName, facts.PlayerCarPath) switch
        {
            ({ } name, { } path) => $"{name} [{path}]",
            ({ } name, null) => name,
            (null, { } path) => path,
            _ => "unknown",
        };
        return $"track: {facts.TrackLabel}{length}\ncar:   {car}";
    }

    public const string LapHeader = "  lap   lap time   iRacing    min km/h  max km/h  note";

    public static string LapRow(LapSummary lap)
    {
        var note = !lap.IsComplete ? "partial" : lap.PitRoad ? "pit" : string.Empty;
        return string.Create(Inv,
            $"{lap.Lap,5}  {LapTime(lap.LapTime),9}  {LapTime(lap.ReportedLapTime),9}  {Kmh(lap.MinSpeed),8}  {Kmh(lap.MaxSpeed),8}  {note}");
    }
}
