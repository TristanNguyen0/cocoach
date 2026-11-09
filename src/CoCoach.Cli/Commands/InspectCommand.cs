using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoCoach.Telemetry.Ibt;

namespace CoCoach.Cli.Commands;

/// <summary>
/// <c>cocoach inspect &lt;file.ibt&gt; [--json]</c>: header facts, the track and the player's car, and a
/// present/absent checklist of the variables later phases rely on. Other drivers are never printed.
/// </summary>
internal static class InspectCommand
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var json = args.Contains("--json");
        var files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (files.Length != 1)
        {
            error.WriteLine("usage: cocoach inspect <file.ibt> [--json]");
            return CliApp.Usage;
        }

        var report = IbtInspector.Inspect(files[0]);
        if (json)
            output.WriteLine(ToJson(report));
        else
            WriteText(report, output);
        return CliApp.Ok;
    }

    internal static string ToJson(IbtInspection report) => JsonSerializer.Serialize(new
    {
        file = report.File,
        fileBytes = report.FileBytes,
        header = new
        {
            version = report.Header.Version,
            tickRate = report.Header.TickRate,
            recordCount = report.Header.RecordCount,
            claimedRecordCount = report.Header.ClaimedRecordCount,
            durationSeconds = Math.Round(report.Header.Duration.TotalSeconds, 3),
            recordLength = report.Header.RecordLength,
            variableCount = report.TotalVariables,
            sessionStartUtc = SessionStart(report.Header)?.ToString("O", CultureInfo.InvariantCulture),
            sessionLapCount = report.Header.SessionLapCount,
        },
        session = report.Session,
        verdicts = new
        {
            gps = report.HasGps,
            kerbChannels = report.HasKerbChannels,
            subTickVariables = report.SubTickVariables.Count,
        },
        checklist = report.Checklist,
        subTickVariables = report.SubTickVariables,
    }, Json);

    static void WriteText(IbtInspection r, TextWriter o)
    {
        var inv = CultureInfo.InvariantCulture;
        var h = r.Header;
        o.WriteLine($"file:      {r.File}");
        o.WriteLine(string.Create(inv, $"size:      {r.FileBytes / (1024.0 * 1024.0):0.0} MiB, header v{h.Version}, {r.TotalVariables} variables, {h.RecordLength} bytes per record"));
        o.WriteLine(string.Create(inv, $"tick rate: {h.TickRate} Hz"));
        var truncated = h.ClaimedRecordCount > h.RecordCount ? $" (header claims {h.ClaimedRecordCount}; file is truncated)" : string.Empty;
        o.WriteLine(string.Create(inv, $"records:   {h.RecordCount}{truncated}"));
        o.WriteLine(string.Create(inv, $"duration:  {h.Duration:hh\\:mm\\:ss\\.fff} ({h.Duration.TotalSeconds:0.0} s)"));
        if (SessionStart(h) is { } start)
            o.WriteLine(string.Create(inv, $"recorded:  {start:yyyy-MM-dd HH:mm} UTC, {h.SessionLapCount} laps in header"));
        o.WriteLine(Formatting.TrackAndCar(r.Session));
        if (r.Session.EventType is not null || r.Session.SessionTypes.Count > 0)
            o.WriteLine($"session:   {r.Session.EventType ?? "?"}; {string.Join(", ", r.Session.SessionTypes)}");
        o.WriteLine();

        o.WriteLine("checklist:");
        WriteChecks(r.Checklist, o);
        o.WriteLine();

        o.WriteLine(r.SubTickVariables.Count == 0
            ? "sub-tick (_ST) variables: none in this file"
            : $"sub-tick (_ST) variables: {r.SubTickVariables.Count}");
        WriteChecks(r.SubTickVariables, o);
        o.WriteLine();

        o.WriteLine("verdicts:");
        o.WriteLine($"  GPS (Lat/Lon present and non-zero): {(r.HasGps ? "yes" : "NO")}");
        o.WriteLine($"  per-tyre kerb channels:             {(r.HasKerbChannels ? "yes" : "NO")}");
        o.WriteLine($"  sub-tick (_ST) variables:           {r.SubTickVariables.Count}");
    }

    static void WriteChecks(IReadOnlyList<VariableCheck> checks, TextWriter o)
    {
        foreach (var c in checks)
        {
            if (!c.Present)
            {
                o.WriteLine($"  [ ] {c.Name,-28} absent");
                continue;
            }

            var unit = string.IsNullOrEmpty(c.Unit) ? "-" : c.Unit;
            var count = c.Count is > 1 ? $"[{c.Count}]" : string.Empty;
            var range = c.Populated
                ? $"{Formatting.Number(c.Min)} .. {Formatting.Number(c.Max)}"
                : "always zero";
            o.WriteLine($"  [x] {c.Name,-28} {c.Type + count,-10} {unit,-14} {range}");
        }
    }

    static DateTime? SessionStart(IbtHeader h)
    {
        // a time_t after 2000 and before 2100 is a real date; anything else is a synthetic or broken header
        if (h.SessionStartDate is < 946_684_800 or > 4_102_444_800)
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(h.SessionStartDate).UtcDateTime;
    }
}
