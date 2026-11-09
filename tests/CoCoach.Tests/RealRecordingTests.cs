using CoCoach.Telemetry.Ibt;
using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Pipeline;
using CoCoach.Tests.Support;

namespace CoCoach.Tests;

/// <summary>
/// Runs against real iRacing recordings in <c>COCOACH_IBT_DIR</c> (default <c>samples/ibt/</c>).
/// Skipped when the directory has no .ibt files; recordings are never committed.
/// </summary>
public sealed class RealRecordingTests
{
    static string[] FilesOrSkip()
    {
        var files = RecordingCorpus.Files();
        if (files.Length == 0)
            Assert.Skip($"No .ibt files in {RecordingCorpus.Directory}. Set {RecordingCorpus.EnvironmentVariable} or run scripts/fetch-sample-ibt.sh.");
        return files;
    }

    [Fact]
    public void Every_recording_inspects()
    {
        foreach (var file in FilesOrSkip())
        {
            var report = IbtInspector.Inspect(file);
            Assert.True(report.Header.TickRate > 0, file);
            Assert.True(report.Header.RecordCount > 0, file);
            Assert.Equal(IbtInspector.ChecklistVariables.Count, report.Checklist.Count);
            // every recording ever made carries the lap coordinate the live loop depends on
            Assert.True(report.Checklist.Single(c => c.Name == "LapDistPct").Present, file);
        }
    }

    [Fact]
    public async Task Every_recording_replays_deterministically_through_the_sdk()
    {
        foreach (var file in FilesOrSkip())
        {
            var first = await Replay(file);
            var second = await Replay(file);

            using var raw = IbtFile.Open(file);
            Assert.Equal(raw.Header.RecordCount, first.Frames);
            Assert.Equal(first.Laps, second.Laps);
        }
    }

    [Fact]
    public async Task Measured_lap_times_agree_with_iRacing()
    {
        foreach (var file in FilesOrSkip())
        {
            var result = await Replay(file);

            // the first timed lap of a session can differ: iRacing times a race-start lap from the start, not the line
            var comparable = result.Laps
                .Where(l => l is { IsComplete: true, PitRoad: false, ReportedLapTime: not null })
                .Skip(1);
            foreach (var lap in comparable)
                Assert.True(Math.Abs(lap.LapTime!.Value - lap.ReportedLapTime!.Value) < 0.05,
                    $"{Path.GetFileName(file)} lap {lap.Lap}: measured {lap.LapTime:0.000} s, iRacing {lap.ReportedLapTime:0.000} s");
        }
    }

    static async Task<PipelineResult> Replay(string file)
    {
        await using var source = SdkTelemetrySource.OpenReplay(file);
        return await new TelemetryPipeline().RunAsync(source, TestContext.Current.CancellationToken);
    }
}
