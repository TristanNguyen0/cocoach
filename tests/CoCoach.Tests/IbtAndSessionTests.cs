using CoCoach.Telemetry.Ibt;
using CoCoach.Telemetry.Session;
using CoCoach.Tests.Support;

namespace CoCoach.Tests;

public sealed class IbtFileTests : IDisposable
{
    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Reads_header_facts_and_variables()
    {
        var session = new SyntheticSession(laps: 2);
        var path = session.WriteIbt(_dir.File("s.ibt"));

        using var file = IbtFile.Open(path);

        Assert.Equal(60, file.Header.TickRate);
        Assert.Equal(2, file.Header.Version);
        Assert.Equal(session.FrameCount, file.Header.RecordCount);
        Assert.Equal(session.FrameCount / 60.0, file.Header.Duration.TotalSeconds, 0.001);
        Assert.Equal(SyntheticSession.AllVars.Length, file.Variables.Count);
        var st = file.Find("YawRate_ST")!;
        Assert.Equal(6, st.Count);
        Assert.Equal("rad/s", st.Unit);
        Assert.Contains("Synthetic Test Circuit", file.ReadSessionInfoYaml(), StringComparison.Ordinal);
    }

    [Fact]
    public void Truncated_file_reports_only_whole_records()
    {
        var session = new SyntheticSession(laps: 1);
        var path = session.WriteIbt(_dir.File("s.ibt"));
        int recordLength;
        using (var file = IbtFile.Open(path))
            recordLength = file.Header.RecordLength;

        using (var stream = new FileStream(path, FileMode.Open))
            stream.SetLength(stream.Length - recordLength * 10 - 3);

        using var truncated = IbtFile.Open(path);
        Assert.Equal(session.FrameCount, truncated.Header.ClaimedRecordCount);
        Assert.Equal(session.FrameCount - 11, truncated.Header.RecordCount);
    }

    [Fact]
    public void Rejects_files_that_are_not_ibt()
    {
        var path = _dir.File("junk.ibt");
        File.WriteAllBytes(path, new byte[64]);
        Assert.Throws<InvalidDataException>(() => IbtFile.Open(path));

        File.WriteAllBytes(path, new byte[4096]);
        Assert.Throws<InvalidDataException>(() => IbtFile.Open(path));
    }

    [Fact]
    public void Inspector_checks_every_listed_variable()
    {
        var path = new SyntheticSession(laps: 1).WriteIbt(_dir.File("s.ibt"));

        var report = IbtInspector.Inspect(path);

        Assert.Equal(IbtInspector.ChecklistVariables, report.Checklist.Select(c => c.Name));
        var speed = report.Checklist.Single(c => c.Name == "Speed");
        Assert.True(speed.Present);
        Assert.Equal("m/s", speed.Unit);
        Assert.Equal(25, speed.Min!.Value, 0.5);
        Assert.Equal(55, speed.Max!.Value, 0.5);
        Assert.False(report.Checklist.Single(c => c.Name == "TireLF_RumblePitch").Present);
        Assert.False(report.Checklist.Single(c => c.Name == "Alt").Present);
        Assert.True(report.HasGps);
        Assert.False(report.HasKerbChannels);
        var st = Assert.Single(report.SubTickVariables);
        Assert.Equal("YawRate_ST", st.Name);
        Assert.Equal(6 * report.Header.RecordCount, st.Samples);
    }

    [Fact]
    public void Gps_present_but_always_zero_is_not_gps()
    {
        var session = new SyntheticSession(laps: 1);
        foreach (var row in session.Rows)
        {
            row["Lat"] = 0;
            row["Lon"] = 0;
        }
        var report = IbtInspector.Inspect(session.WriteIbt(_dir.File("s.ibt")));

        var lat = report.Checklist.Single(c => c.Name == "Lat");
        Assert.True(lat.Present);
        Assert.False(lat.Populated);
        Assert.False(report.HasGps);
    }
}

public sealed class SessionInfoParserTests
{
    [Fact]
    public void Extracts_track_and_player_car_only()
    {
        var facts = SessionInfoParser.Parse(SyntheticSession.Yaml);

        Assert.Equal("Synthetic Test Circuit", facts.TrackDisplayName);
        Assert.Equal("Full", facts.TrackConfigName);
        Assert.Equal("1.50 km", facts.TrackLength);
        Assert.Equal(999, facts.TrackId);
        Assert.Equal("Test", facts.EventType);
        Assert.False(facts.Official);
        Assert.Equal(["Offline Testing"], facts.SessionTypes);
        Assert.Equal(1, facts.PlayerCarIdx);
        Assert.Equal("Test Roadster", facts.PlayerCarScreenName);
        Assert.Equal("testroadster", facts.PlayerCarPath);
        Assert.Equal(77, facts.PlayerCarId);
        Assert.Equal("TR", facts.PlayerCarClassShortName);
        Assert.Equal("Synthetic Test Circuit (Full)", facts.TrackLabel);
    }

    [Fact]
    public void Survives_unquoted_names_that_break_strict_yaml()
    {
        // "Rival: The "Other" Driver" is not valid as a plain YAML scalar; the parser must quote and retry
        Assert.Contains("UserName: Rival: The", SyntheticSession.Yaml, StringComparison.Ordinal);

        var facts = SessionInfoParser.Parse(SyntheticSession.Yaml);

        Assert.Equal("Test Roadster", facts.PlayerCarScreenName);
    }

    [Fact]
    public void Facts_never_carry_driver_names_or_ids()
    {
        var facts = SessionInfoParser.Parse(SyntheticSession.Yaml);
        var text = System.Text.Json.JsonSerializer.Serialize(facts);

        foreach (var secret in new[] { "Test Driver", "Rival", "Pace Car", "123456", "654321" })
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Garbage_gives_empty_facts()
    {
        var facts = SessionInfoParser.Parse(": : not yaml [");
        Assert.Null(facts.TrackDisplayName);
        Assert.Empty(facts.SessionTypes);
        Assert.Equal("unknown track", facts.TrackLabel);
    }

    [Fact]
    public void Quoting_leaves_already_quoted_values_alone()
    {
        const string yaml = "  UserName: 'Already Quoted'\n  TeamName: It's Team: One\n";
        var quoted = SessionInfoParser.QuoteFreeTextFields(yaml);
        Assert.Contains("UserName: 'Already Quoted'", quoted, StringComparison.Ordinal);
        Assert.Contains("TeamName: 'It''s Team: One'", quoted, StringComparison.Ordinal);
    }
}
