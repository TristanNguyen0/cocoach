using CoCoach.Telemetry.Ibt;
using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Pipeline;
using CoCoach.Tests.Support;

namespace CoCoach.Tests;

/// <summary>End to end: synthetic .ibt file -> SVappsLAB SDK replay -> shared pipeline.</summary>
public sealed class ReplayPipelineTests : IDisposable
{
    readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    static async Task<PipelineResult> Replay(string path)
    {
        await using var source = SdkTelemetrySource.OpenReplay(path);
        return await new TelemetryPipeline().RunAsync(source, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Replays_every_frame_and_times_every_lap()
    {
        var session = new SyntheticSession(laps: 3);
        var path = session.WriteIbt(_dir.File("session.ibt"));

        var result = await Replay(path);

        Assert.Equal(session.FrameCount, result.Frames);
        Assert.Equal(0, result.DroppedFrames);
        Assert.Equal(session.ExpectedLaps.Select(l => l.Lap), result.Laps.Select(l => l.Lap));
        foreach (var (expected, actual) in session.ExpectedLaps.Zip(result.Laps))
        {
            Assert.Equal(expected.LapTime is not null, actual.IsComplete);
            if (expected.LapTime is { } time)
            {
                Assert.Equal(time, actual.LapTime!.Value, 0.001);
                Assert.Equal(time, actual.ReportedLapTime!.Value, 0.001);
            }
            Assert.Equal(expected.MinSpeed, actual.MinSpeed);
            Assert.Equal(expected.MaxSpeed, actual.MaxSpeed);
        }
    }

    [Fact]
    public async Task Replay_and_direct_frames_agree()
    {
        // the same session through the SDK and straight into the pipeline gives identical laps
        var session = new SyntheticSession(laps: 3);
        var path = session.WriteIbt(_dir.File("session.ibt"));

        var replayed = await Replay(path);
        var tracker = new Telemetry.Laps.LapTracker();
        foreach (var frame in session.Frames())
            tracker.OnFrame(frame);
        tracker.OnCompleted();

        Assert.Equal(tracker.Laps, replayed.Laps);
    }

    [Fact]
    public async Task Replay_is_deterministic()
    {
        var path = new SyntheticSession(laps: 4).WriteIbt(_dir.File("session.ibt"));

        var first = await Replay(path);
        var second = await Replay(path);

        Assert.Equal(first.Frames, second.Frames);
        Assert.Equal(first.Laps, second.Laps);
    }

    [Fact]
    public async Task Delivers_session_info_once_and_parses_only_the_player_car()
    {
        var path = new SyntheticSession(laps: 1).WriteIbt(_dir.File("session.ibt"));

        var result = await Replay(path);

        Assert.Equal(1, result.SessionInfoUpdates);
        Assert.NotNull(result.Session);
        Assert.Equal("Synthetic Test Circuit", result.Session.TrackDisplayName);
        Assert.Equal("Test Roadster", result.Session.PlayerCarScreenName);
    }

    [Fact]
    public async Task Missing_variables_become_null_not_errors()
    {
        var session = new SyntheticSession(laps: 2);
        var omit = new[] { TelemetryVars.Speed, TelemetryVars.Brake, TelemetryVars.LapLastLapTime, TelemetryVars.OnPitRoad, TelemetryVars.IsOnTrack };
        var path = session.WriteIbt(_dir.File("sparse.ibt"), omit);

        var frames = new List<TelemetryFrame>();
        await using (var source = SdkTelemetrySource.OpenReplay(path))
        {
            var collector = new Collector(frames);
            var result = await new TelemetryPipeline([collector]).RunAsync(source, TestContext.Current.CancellationToken);
            Assert.DoesNotContain(result.Variables, v => v.Name == TelemetryVars.Speed);
            Assert.Contains(result.Variables, v => v.Name == TelemetryVars.Lap);
            Assert.Equal(session.ExpectedLaps[1].LapTime!.Value, result.Laps[1].LapTime!.Value, 0.001);
        }

        Assert.Equal(session.FrameCount, frames.Count);
        Assert.All(frames, f =>
        {
            Assert.Null(f.Speed);
            Assert.Null(f.Brake);
            Assert.NotNull(f.Lap);
            Assert.NotNull(f.Throttle);
        });
    }

    [Fact]
    public async Task Frame_values_match_the_raw_file()
    {
        var session = new SyntheticSession(laps: 1);
        var path = session.WriteIbt(_dir.File("session.ibt"));

        var frames = new List<TelemetryFrame>();
        await using (var source = SdkTelemetrySource.OpenReplay(path))
            await new TelemetryPipeline([new Collector(frames)]).RunAsync(source, TestContext.Current.CancellationToken);

        var raw = new List<double>();
        using (var file = IbtFile.Open(path))
            file.Scan([file.Find("Speed")!], (_, v) => raw.Add(v));

        Assert.Equal(raw, frames.Select(f => (double)f.Speed!.Value));
        Assert.Equal(Enumerable.Range(0, frames.Count).Select(i => (long)i), frames.Select(f => f.Index));
        Assert.True(frames.Zip(frames.Skip(1)).All(p => p.Second.ArrivalTimestamp >= p.First.ArrivalTimestamp));
    }

    [Fact]
    public async Task A_failing_observer_stops_the_replay_instead_of_hanging()
    {
        var path = new SyntheticSession(laps: 5).WriteIbt(_dir.File("session.ibt"));
        await using var source = SdkTelemetrySource.OpenReplay(path);

        var pipeline = new TelemetryPipeline([new Throwing(after: 100)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.RunAsync(source, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Replays_through_a_symbolic_link()
    {
        var session = new SyntheticSession(laps: 1);
        var target = session.WriteIbt(_dir.File("target.ibt"));
        var link = _dir.File("link.ibt");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"Cannot create symbolic links here: {ex.Message}");
        }

        var result = await Replay(link);

        Assert.Equal(session.FrameCount, result.Frames);
    }

    [Fact]
    public void Rejects_files_the_sdk_cannot_replay()
    {
        Assert.Throws<FileNotFoundException>(() => SdkTelemetrySource.OpenReplay(_dir.File("missing.ibt")));
        var upper = _dir.File("SESSION.IBT");
        File.WriteAllText(upper, "x");
        Assert.Throws<ArgumentException>(() => SdkTelemetrySource.OpenReplay(upper));
    }

    sealed class Collector(List<TelemetryFrame> frames) : IFrameObserver
    {
        public void OnFrame(TelemetryFrame frame) => frames.Add(frame);

        public void OnCompleted()
        {
        }
    }

    sealed class Throwing(int after) : IFrameObserver
    {
        int _seen;

        public void OnFrame(TelemetryFrame frame)
        {
            if (++_seen > after)
                throw new InvalidOperationException("observer failed");
        }

        public void OnCompleted()
        {
        }
    }
}
