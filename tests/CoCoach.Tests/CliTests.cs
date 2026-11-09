using System.Text.Json;
using System.Text.RegularExpressions;
using CoCoach.Cli;
using CoCoach.Tests.Support;

namespace CoCoach.Tests;

public sealed class CliTests : IDisposable
{
    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await CliApp.RunAsync(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    static readonly string[] OtherDrivers = ["Test Driver", "Rival", "Pace Car", "123456", "654321", "Safety Car"];

    [Fact]
    public async Task Inspect_prints_header_track_car_and_checklist()
    {
        var path = new SyntheticSession(laps: 1).WriteIbt(_dir.File("s.ibt"));

        var (code, output, _) = await Run("inspect", path);

        Assert.Equal(0, code);
        Assert.Contains("tick rate: 60 Hz", output, StringComparison.Ordinal);
        Assert.Contains("Synthetic Test Circuit (Full)", output, StringComparison.Ordinal);
        Assert.Contains("Test Roadster", output, StringComparison.Ordinal);
        Assert.Matches(@"\[x\] Speed\s+float\s+m/s", output);
        Assert.Matches(@"\[ \] PlayerTrackSurfaceMaterial\s+absent", output);
        Assert.Contains("YawRate_ST", output, StringComparison.Ordinal);
        foreach (var name in OtherDrivers)
            Assert.DoesNotContain(name, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspect_json_is_machine_readable_and_private()
    {
        var path = new SyntheticSession(laps: 1).WriteIbt(_dir.File("s.ibt"));

        var (code, output, _) = await Run("inspect", path, "--json");

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        Assert.Equal(60, root.GetProperty("header").GetProperty("tickRate").GetInt32());
        Assert.Equal("Test Roadster", root.GetProperty("session").GetProperty("playerCarScreenName").GetString());
        var checklist = root.GetProperty("checklist").EnumerateArray().ToArray();
        Assert.Equal(24, checklist.Length);
        Assert.True(root.GetProperty("verdicts").GetProperty("gps").GetBoolean());
        foreach (var name in OtherDrivers)
            Assert.DoesNotContain(name, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_prints_a_row_per_lap()
    {
        var session = new SyntheticSession(laps: 3);
        var path = session.WriteIbt(_dir.File("s.ibt"));

        var (code, output, _) = await Run("replay", path);

        Assert.Equal(0, code);
        Assert.Contains($"laps: {session.ExpectedLaps.Count}, complete: 3", output, StringComparison.Ordinal);
        var lap2 = session.ExpectedLaps.Single(l => l.Lap == 2).LapTime!.Value;
        Assert.Matches($@"\s2\s+{Regex.Escape(Cli.Commands.Formatting.LapTime(lap2))}\s", output);
        Assert.Contains("partial", output, StringComparison.Ordinal);
        foreach (var name in OtherDrivers)
            Assert.DoesNotContain(name, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bad_input_fails_cleanly()
    {
        Assert.Equal(2, (await Run()).Code);
        Assert.Equal(2, (await Run("nope")).Code);
        Assert.Equal(2, (await Run("inspect")).Code);
        var missing = await Run("inspect", _dir.File("missing.ibt"));
        Assert.Equal(1, missing.Code);
        Assert.Contains("error:", missing.Err, StringComparison.Ordinal);
        Assert.Equal(2, (await Run("latency", "--threshold", "2")).Code);
    }

    [Fact]
    public async Task Live_commands_explain_they_need_windows()
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("Only meaningful off Windows.");

        var live = await Run("live");
        Assert.Equal(1, live.Code);
        Assert.Contains("only on Windows", live.Err, StringComparison.Ordinal);
        Assert.Equal(1, (await Run("latency")).Code);
    }
}
