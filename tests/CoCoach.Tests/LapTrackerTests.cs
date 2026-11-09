using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Laps;
using CoCoach.Tests.Support;

namespace CoCoach.Tests;

public sealed class LapTrackerTests
{
    static IReadOnlyList<LapSummary> Track(IEnumerable<TelemetryFrame> frames)
    {
        var tracker = new LapTracker();
        foreach (var frame in frames)
            tracker.OnFrame(frame);
        tracker.OnCompleted();
        return tracker.Laps;
    }

    [Fact]
    public void Splits_laps_and_times_them_to_the_millisecond()
    {
        var session = new SyntheticSession(laps: 3);
        var laps = Track(session.Frames());

        Assert.Equal(session.ExpectedLaps.Select(l => l.Lap), laps.Select(l => l.Lap));
        foreach (var (expected, actual) in session.ExpectedLaps.Zip(laps))
        {
            Assert.Equal(expected.LapTime is not null, actual.IsComplete);
            if (expected.LapTime is { } time)
                Assert.Equal(time, actual.LapTime!.Value, 0.001);
            Assert.Equal(expected.MinSpeed, actual.MinSpeed);
            Assert.Equal(expected.MaxSpeed, actual.MaxSpeed);
            Assert.Equal(expected.Frames, actual.Frames);
        }
    }

    [Fact]
    public void Out_lap_and_unfinished_last_lap_are_partial()
    {
        var laps = Track(new SyntheticSession(laps: 2).Frames());

        Assert.False(laps[0].IsComplete);
        Assert.Null(laps[0].LapTime);
        Assert.False(laps[^1].IsComplete);
        Assert.Null(laps[^1].LapTime);
        Assert.All(laps.Skip(1).SkipLast(1), l => Assert.True(l.IsComplete));
    }

    [Fact]
    public void Attaches_the_lap_time_iRacing_publishes_after_the_line()
    {
        var session = new SyntheticSession(laps: 3);
        var laps = Track(session.Frames());

        foreach (var lap in laps.Where(l => l.IsComplete))
            Assert.Equal(lap.LapTime!.Value, lap.ReportedLapTime!.Value, 0.001);
        Assert.Null(laps[0].ReportedLapTime);
    }

    [Fact]
    public void Tolerates_sources_without_optional_channels()
    {
        var session = new SyntheticSession(laps: 2);
        var omit = new[] { TelemetryVars.Speed, TelemetryVars.LapLastLapTime, TelemetryVars.IsOnTrack, TelemetryVars.OnPitRoad };
        var laps = Track(session.Frames(omit));

        Assert.Equal(session.ExpectedLaps.Count, laps.Count);
        Assert.All(laps, l => Assert.Null(l.MinSpeed));
        Assert.All(laps, l => Assert.Null(l.ReportedLapTime));
        Assert.Equal(session.ExpectedLaps[1].LapTime!.Value, laps[1].LapTime!.Value, 0.001);
    }

    [Fact]
    public void Ignores_frames_without_lap_or_session_time()
    {
        var laps = Track(new SyntheticSession(laps: 2).Frames([TelemetryVars.Lap]));
        Assert.Empty(laps);
    }

    [Fact]
    public void Lap_increment_without_a_line_wrap_is_not_a_complete_lap()
    {
        // iRacing bumps Lap when the car starts just behind the line: LapDistPct goes 0.0 -> 0.995
        var frames = new List<TelemetryFrame>
        {
            new() { Lap = 0, SessionTime = 10.000, LapDistPct = 0.000f, Speed = 0 },
            new() { Lap = 1, SessionTime = 10.017, LapDistPct = 0.995f, Speed = 1 },
            new() { Lap = 1, SessionTime = 12.000, LapDistPct = 0.999f, Speed = 10 },
            new() { Lap = 2, SessionTime = 12.017, LapDistPct = 0.001f, Speed = 10 },
            new() { Lap = 2, SessionTime = 80.000, LapDistPct = 0.999f, Speed = 10 },
            new() { Lap = 3, SessionTime = 80.017, LapDistPct = 0.001f, Speed = 10 },
        };

        var laps = Track(frames);

        Assert.False(laps.Single(l => l.Lap == 1).IsComplete);
        var lap2 = laps.Single(l => l.Lap == 2);
        Assert.True(lap2.IsComplete);
        Assert.Equal(68.0, lap2.LapTime!.Value, 0.01);
    }

    [Fact]
    public void Leaving_the_car_interrupts_the_lap()
    {
        var frames = new SyntheticSession(laps: 3).Frames().ToList();
        // pull the driver out for a second in the middle of lap 3
        var lap3 = frames.FindIndex(f => f.Lap == 3) + 600;
        for (var i = lap3; i < lap3 + 60; i++)
            frames[i] = frames[i] with { IsOnTrack = false };

        var laps = Track(frames);

        Assert.False(laps.Single(l => l.Lap == 3).IsComplete);
        Assert.True(laps.Single(l => l.Lap == 2).IsComplete);
        Assert.True(laps.Single(l => l.Lap == 4).IsComplete);
    }

    [Fact]
    public void Lap_reset_closes_the_current_lap_as_partial()
    {
        var frames = new List<TelemetryFrame>
        {
            new() { Lap = 5, SessionTime = 1, LapDistPct = 0.5f, Speed = 30 },
            new() { Lap = 5, SessionTime = 2, LapDistPct = 0.6f, Speed = 31 },
            new() { Lap = 0, SessionTime = 3, LapDistPct = 0.1f, Speed = 0 },
        };

        var laps = Track(frames);

        Assert.Equal([5, 0], laps.Select(l => l.Lap));
        Assert.All(laps, l => Assert.False(l.IsComplete));
    }

    [Fact]
    public void Same_frames_always_give_the_same_laps()
    {
        var session = new SyntheticSession(laps: 4);
        Assert.Equal(Track(session.Frames()), Track(session.Frames()));
    }

    [Fact]
    public void Crossing_time_is_interpolated_between_ticks()
    {
        var before = new TelemetryFrame { SessionTime = 10.0, LapDistPct = 0.99f };
        var after = new TelemetryFrame { SessionTime = 10.1, LapDistPct = 0.03f };

        var (time, verified) = LapTracker.CrossingTime(before, after, 10.1);

        Assert.True(verified);
        Assert.Equal(10.025, time, 0.0001);
    }
}
