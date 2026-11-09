using System.Diagnostics;
using System.Runtime.InteropServices;
using CoCoach.Cli.Latency;
using CoCoach.Telemetry.Events;
using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Recording;

namespace CoCoach.Tests;

public sealed class AutoRecordControllerTests
{
    sealed class FakeRecorder : ITelemetryRecorder
    {
        public List<string> Commands { get; } = [];

        public void Start() => Commands.Add("start");

        public void Stop() => Commands.Add("stop");
    }

    static TelemetryFrame Frame(bool? onTrack) => new() { IsOnTrack = onTrack };

    static void Feed(AutoRecordController controller, bool? onTrack, int frames)
    {
        for (var i = 0; i < frames; i++)
            controller.OnFrame(Frame(onTrack));
    }

    [Fact]
    public void Starts_when_the_car_goes_on_track_and_stops_when_it_leaves()
    {
        var recorder = new FakeRecorder();
        var controller = new AutoRecordController(recorder, debounceFrames: 30);

        Feed(controller, false, 100);
        Assert.Empty(recorder.Commands);

        Feed(controller, true, 29);
        Assert.Empty(recorder.Commands);
        Feed(controller, true, 1);
        Assert.Equal(["start"], recorder.Commands);
        Assert.True(controller.IsRecording);

        Feed(controller, true, 500);
        Feed(controller, false, 30);
        Assert.Equal(["start", "stop"], recorder.Commands);
        Assert.False(controller.IsRecording);
    }

    [Fact]
    public void A_short_blip_does_not_toggle_recording()
    {
        var recorder = new FakeRecorder();
        var controller = new AutoRecordController(recorder, debounceFrames: 30);

        Feed(controller, true, 30);
        for (var i = 0; i < 10; i++)
        {
            Feed(controller, false, 29);
            Feed(controller, true, 1);
        }

        Assert.Equal(["start"], recorder.Commands);
    }

    [Fact]
    public void Frames_without_IsOnTrack_are_ignored()
    {
        var recorder = new FakeRecorder();
        var controller = new AutoRecordController(recorder, debounceFrames: 1);

        Feed(controller, null, 100);
        Assert.Empty(recorder.Commands);
    }

    [Fact]
    public void Stops_a_recording_it_started_when_the_source_ends()
    {
        var recorder = new FakeRecorder();
        var controller = new AutoRecordController(recorder, debounceFrames: 1);

        controller.OnCompleted();
        Assert.Empty(recorder.Commands);

        Feed(controller, true, 1);
        controller.OnCompleted();
        Assert.Equal(["start", "stop"], recorder.Commands);
    }

    [Fact]
    public void Starts_again_after_the_sim_restarts_mid_stint()
    {
        var recorder = new FakeRecorder();
        var controller = new AutoRecordController(recorder, debounceFrames: 1);
        var tick = Stopwatch.Frequency / 60;
        long now = 1;

        controller.OnFrame(new TelemetryFrame { IsOnTrack = true, ArrivalTimestamp = now += tick });
        controller.OnFrame(new TelemetryFrame { IsOnTrack = true, ArrivalTimestamp = now += tick });
        Assert.Equal(["start"], recorder.Commands);

        // iRacing closes and comes back: no frames for 10 s, then the car is on track again
        now += 10 * Stopwatch.Frequency;
        controller.OnFrame(new TelemetryFrame { IsOnTrack = true, ArrivalTimestamp = now });
        Assert.Equal(["start", "start"], recorder.Commands);
    }

    [Fact]
    public void Recorder_interface_exposes_only_start_and_stop()
    {
        // the only broadcast cocoach may send is TelemCommand start/stop
        var methods = typeof(ITelemetryRecorder).GetMethods().Select(m => m.Name).Order();
        Assert.Equal(["Start", "Stop"], methods);
    }
}

public sealed class ThresholdCrossingDetectorTests
{
    [Fact]
    public void Fires_once_per_brake_application()
    {
        var detector = ThresholdCrossingDetector.BrakeApplied();
        var fired = new List<long>();
        detector.Crossed += f => fired.Add(f.Index);

        float?[] brake = [0, 0.3f, 0.6f, 0.9f, 0.55f, 0.4f, 0.3f, 0.6f, 0.1f, 0.7f, null, 0.8f, 0];
        for (var i = 0; i < brake.Length; i++)
            detector.OnFrame(new TelemetryFrame { Index = i, Brake = brake[i] });

        // 0.4/0.3 do not re-arm (reset is below 0.2); 0.1 does
        Assert.Equal([2, 9], fired);
        Assert.Equal(2, detector.Count);
    }

    [Fact]
    public void Reset_level_cannot_exceed_threshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ThresholdCrossingDetector.BrakeApplied(0.5f, 0.6f));
    }
}

public sealed class LatencyTests
{
    [Fact]
    public void Stats_match_hand_computed_percentiles()
    {
        var stats = LatencyStats.From([10, 20, 30, 40, 50, double.NaN])!;

        Assert.Equal(5, stats.Count);
        Assert.Equal(10, stats.Min);
        Assert.Equal(50, stats.Max);
        Assert.Equal(30, stats.Mean);
        Assert.Equal(30, stats.P50);
        Assert.Equal(48, stats.P95, 6);
        Assert.Null(LatencyStats.From([]));
    }

    [Fact]
    public void Sample_splits_total_into_stages()
    {
        var f = Stopwatch.Frequency;
        var sample = new LatencySample(0, f / 1000, 3 * f / 1000, 13 * f / 1000);

        Assert.Equal(1, sample.PipelineMs, 3);
        Assert.Equal(2, sample.BufferWaitMs, 3);
        Assert.Equal(10, sample.DeviceQueueMs, 3);
        Assert.Equal(13, sample.TotalMs, 3);
    }

    [Fact]
    public void Click_starts_at_the_next_buffer_and_records_its_stream_position()
    {
        long now = 1000;
        var provider = new ClickWaveProvider(48000, 2, () => now);
        var buffer = new byte[480 * 2 * sizeof(float)]; // 10 ms

        provider.Read(buffer);
        Assert.True(MemoryMarshal.Cast<byte, float>(buffer).ToArray().All(s => s == 0));

        Assert.True(provider.TryTrigger(arrivalTimestamp: 900));
        Assert.False(provider.TryTrigger(arrivalTimestamp: 950)); // busy

        now = 1200;
        provider.Read(buffer);
        var samples = MemoryMarshal.Cast<byte, float>(buffer).ToArray();
        Assert.Contains(samples, s => s != 0);
        Assert.Equal(samples[0], samples[1]); // both channels carry the click

        Assert.True(provider.Written.TryDequeue(out var click));
        Assert.Equal(900, click.Arrival);
        Assert.Equal(1000, click.Detected);
        Assert.Equal(1200, click.Written);
        Assert.Equal(0.010, click.StartSeconds, 9);

        // a 15 ms click spans two 10 ms buffers, then the provider is idle again
        provider.Read(buffer);
        Assert.False(provider.Busy);
        Assert.True(provider.TryTrigger(arrivalTimestamp: 2000));
        Assert.Equal(3 * 480, provider.FramesWritten);
    }
}
