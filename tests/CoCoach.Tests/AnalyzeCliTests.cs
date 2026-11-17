using System.Text.Json;
using CoCoach.Analysis;
using CoCoach.Analysis.Corners;
using CoCoach.Analysis.Recording;
using CoCoach.Analysis.References;
using CoCoach.Cli;
using CoCoach.Cli.Commands;
using CoCoach.Tests.Support;

namespace CoCoach.Tests;

/// <summary><c>cocoach analyze</c> on generated .ibt files, through the real SDK replay path.</summary>
public sealed class AnalyzeCliTests : IDisposable
{
    static readonly SyntheticCircuit Circuit = new();
    static readonly string[] OtherDrivers = ["Test Driver", "Rival", "Pace Car", "123456", "654321", "Safety Car"];

    static readonly LapStyle[] Styles =
    [
        LapStyle.Clean,
        new() { CornerSpeed = new Dictionary<int, double> { [4] = 0.85 } },
        new() { Decel = new Dictionary<int, double> { [3] = 13 } },
    ];

    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await CliApp.RunAsync(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    string Ibt(string name, IReadOnlyList<LapStyle>? styles = null, int trackId = 999) =>
        Circuit.Session(styles ?? Styles).WriteIbt(_dir.File(name), trackId);

    [Fact]
    public async Task Analyze_prints_corners_per_corner_metrics_and_top_time_loss()
    {
        var (code, output, _) = await Run("analyze", Ibt("s.ibt"));

        Assert.Equal(0, code);
        Assert.Contains("Synthetic Circuit (Test)", output, StringComparison.Ordinal);
        Assert.Contains("GPS centreline", output, StringComparison.Ordinal);
        Assert.Contains("corners: 8 found (iRacing lists 8 turns: plausible)", output, StringComparison.Ordinal);
        Assert.Matches(@"T4\s+left\s+\d+", output);
        Assert.Contains("best lap:  L4", output, StringComparison.Ordinal);
        Assert.Contains("reference: best lap L4", output, StringComparison.Ordinal);
        Assert.Contains("iRacing delta cross-check: 2 laps", output, StringComparison.Ordinal);
        Assert.Contains("per-corner metrics against the best lap L4", output, StringComparison.Ordinal);
        Assert.Matches(@"L3\s+\d:\d\d\.\d{3}\s+\+\d\.\d{3}\s+T1", output);
        Assert.Contains("top 3 time-loss corners", output, StringComparison.Ordinal);
        Assert.Matches(@"1\. T[34]: ", output);
        foreach (var name in OtherDrivers)
            Assert.DoesNotContain(name, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_json_against_the_optimal_composite()
    {
        var (code, output, _) = await Run("analyze", Ibt("s.ibt"), "--reference", "optimal", "--json");

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        Assert.Equal("optimal", root.GetProperty("reference").GetProperty("kind").GetString());
        Assert.Equal(8, root.GetProperty("track").GetProperty("corners").GetArrayLength());
        Assert.Equal("gps", root.GetProperty("track").GetProperty("source").GetString());
        var optimal = root.GetProperty("optimal").GetProperty("lapTime").GetDouble();
        var best = root.GetProperty("best").GetProperty("lapTime").GetDouble();
        Assert.True(optimal <= best);
        var laps = root.GetProperty("laps").EnumerateArray().ToArray();
        Assert.Equal(3, laps.Length);
        Assert.All(laps, l => Assert.Equal(8, l.GetProperty("corners").GetArrayLength()));
        var slowC4 = laps[1].GetProperty("corners")[3];
        Assert.True(slowC4.GetProperty("minSpeedDifference").GetDouble() < -1);
        Assert.True(slowC4.GetProperty("timeDelta").GetDouble() > 0.1);
        Assert.True(root.GetProperty("topTimeLoss").GetArrayLength() >= 1);
        foreach (var name in OtherDrivers)
            Assert.DoesNotContain(name, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_combines_sessions_of_the_same_car_and_track()
    {
        var a = Ibt("a.ibt", [LapStyle.Clean, LapStyle.Clean]);
        var b = Ibt("b.ibt", [new() { Decel = new Dictionary<int, double> { [3] = 13 } }]);

        var (code, output, _) = await Run("analyze", a, b);

        Assert.Equal(0, code);
        Assert.Contains("sessions: 2, complete laps: 3", output, StringComparison.Ordinal);
        Assert.Contains("S2: b.ibt", output, StringComparison.Ordinal);
        Assert.Contains("best lap:  S2 L2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_refuses_different_tracks_and_bad_usage()
    {
        var (code, _, error) = await Run("analyze", Ibt("a.ibt"), Ibt("b.ibt", trackId: 1000));
        Assert.Equal(1, code);
        Assert.Contains("different track layout", error, StringComparison.Ordinal);

        Assert.Equal(2, (await Run("analyze")).Code);
        Assert.Equal(2, (await Run("analyze", "x.ibt", "--reference", "fastest")).Code);
        Assert.Equal(2, (await Run("analyze", "x.ibt", "--bogus")).Code);
    }

    [Fact]
    public async Task Analyze_text_lists_every_lap_and_says_so_when_no_corner_is_detected()
    {
        var session = await SessionLoader.LoadIbtAsync(Ibt("oval.ibt"), cancellationToken: TestContext.Current.CancellationToken);
        var options = new AnalysisOptions { Corners = new CornerDetectionOptions { EnterCurvature = 1 } };
        var result = SessionAnalyzer.Analyze([session], options);

        var output = AnalyzeCommand.ToText(result);

        Assert.Empty(result.Track.Corners);
        Assert.Contains("corners: 0 found", output, StringComparison.Ordinal);
        Assert.Contains("no corners detected, so there are no per-corner metrics (large-radius oval turns are not detected yet)", output, StringComparison.Ordinal);
        for (var k = 0; k < Styles.Length; k++)
            Assert.Matches($@"(?m)^  L{SyntheticRun.LapNumber(k)}\s+\d:\d\d\.\d{{3}}\s+[+-]\d\.\d{{3}}", output);
        Assert.Matches(@"(?m)^  L4\s+\d:\d\d\.\d{3}\s+\+0\.000\s+reference", output);
        Assert.Matches(@"(?m)^optimal:   \d:\d\d\.\d{3}, equals the best lap L4 \(no corner segments to combine\)\r?$", output);
        Assert.DoesNotContain("best corner segments from", output, StringComparison.Ordinal);
        Assert.Contains("top time-loss corners: none, no corners detected", output, StringComparison.Ordinal);
        Assert.DoesNotContain("median over", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_json_tells_apart_laps_whose_number_repeats_after_a_reset()
    {
        var before = Circuit.Session([LapStyle.Clean, Styles[1]]);
        var after = Circuit.Session([Styles[2], LapStyle.Clean], startTime: 900);
        var session = SessionLoader.FromFrames("reset.ibt", before.FollowedByReset(after), Circuit.Facts());
        var result = SessionAnalyzer.Analyze([session], new AnalysisOptions { Reference = ReferenceKind.Optimal });

        using var doc = JsonDocument.Parse(AnalyzeCommand.ToJson(result));
        var root = doc.RootElement;

        var laps = root.GetProperty("laps").EnumerateArray()
            .Select(l => (Label: l.GetProperty("lap").GetString()!, Number: l.GetProperty("lapNumber").GetInt32(), Ordinal: l.GetProperty("ordinal").GetInt32()))
            .ToArray();
        Assert.Equal([("L2", 2, 1), ("L3", 3, 1), ("L2#2", 2, 2), ("L3#2", 3, 2)], laps);
        var labels = laps.Select(l => l.Label).ToHashSet();
        Assert.Equal("L2#2", root.GetProperty("best").GetProperty("lap").GetString());
        Assert.All(root.GetProperty("optimal").GetProperty("segments").EnumerateArray(), s => Assert.Contains(s.GetProperty("lap").GetString()!, labels));
        Assert.All(root.GetProperty("iracingDeltaCheck").EnumerateArray(), c =>
        {
            Assert.Contains(c.GetProperty("lap").GetString()!, labels);
            Assert.Contains(c.GetProperty("reference").GetString()!, labels);
        });
    }

    [Fact]
    public async Task Analyze_reports_a_recording_without_complete_laps()
    {
        var path = new SyntheticSession(laps: 0).WriteIbt(_dir.File("short.ibt"));

        var (code, _, error) = await Run("analyze", path);

        Assert.Equal(1, code);
        Assert.Contains("No valid complete lap", error, StringComparison.Ordinal);
    }
}
