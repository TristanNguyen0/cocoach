using System.Globalization;
using System.Runtime.Versioning;
using CoCoach.Cli.Latency;
using CoCoach.Telemetry.Events;
using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Pipeline;

namespace CoCoach.Cli.Commands;

/// <summary>
/// <c>cocoach latency</c>: plays a click through WASAPI each time the brake rises through a threshold and
/// logs how long it took from the telemetry row arriving to the click starting on the audio device.
/// Windows only.
/// </summary>
internal static class LatencyCommand
{
    internal sealed record Options(int Count, float Threshold, string? ReplayPath, int LatencyMs, bool Exclusive, bool LowLatency, string? CsvPath);

    internal static Options? Parse(string[] args, TextWriter error)
    {
        int count = 20, latencyMs = 10;
        float threshold = 0.5f;
        string? replay = null, csv = null;
        var exclusive = false;
        var lowLatency = true;
        var inv = CultureInfo.InvariantCulture;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--count": count = int.Parse(Next(), inv); break;
                case "--threshold": threshold = float.Parse(Next(), inv); break;
                case "--replay": replay = Next(); break;
                case "--latency-ms": latencyMs = int.Parse(Next(), inv); break;
                case "--exclusive": exclusive = true; break;
                case "--no-low-latency": lowLatency = false; break;
                case "--csv": csv = Next(); break;
                default:
                    error.WriteLine($"unknown option '{args[i]}'");
                    return null;
            }
        }

        if (count < 1 || threshold is <= 0 or >= 1 || latencyMs < 1)
        {
            error.WriteLine("--count must be at least 1, --threshold between 0 and 1, --latency-ms at least 1");
            return null;
        }
        return new Options(count, threshold, replay, latencyMs, exclusive, lowLatency, csv);
    }

    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        Options? options;
        try
        {
            options = Parse(args, error);
        }
        catch (FormatException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            options = null;
        }
        if (options is null)
            return Task.FromResult(CliApp.Usage);

        if (!OperatingSystem.IsWindows())
            return CliApp.WindowsOnlyAsync("latency", error);

        return RunWindowsAsync(options, output, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    static async Task<int> RunWindowsAsync(Options options, TextWriter output, CancellationToken cancellationToken)
    {
        var inv = CultureInfo.InvariantCulture;
        using var player = new WasapiClickPlayer(options.LatencyMs, options.Exclusive, options.LowLatency);
        await using var source = options.ReplayPath is null
            ? SdkTelemetrySource.OpenLive(CliLogging.Sdk)
            : SdkTelemetrySource.OpenReplay(options.ReplayPath, CliLogging.Sdk, playbackSpeed: 1);

        var samples = new List<LatencySample>();
        var skipped = 0;
        using var done = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        StreamWriter? csv = options.CsvPath is null ? null : new StreamWriter(options.CsvPath);
        await using var _ = csv;
        csv?.WriteLine("n,pipeline_ms,buffer_wait_ms,device_queue_ms,total_ms");

        player.Measured += sample =>
        {
            int n;
            lock (samples)
            {
                samples.Add(sample);
                n = samples.Count;
            }
            output.WriteLine(string.Create(inv,
                $"click {n,3}: total {sample.TotalMs,6:0.0} ms  (pipeline {sample.PipelineMs,5:0.0}, wait for buffer {sample.BufferWaitMs,5:0.0}, device queue {sample.DeviceQueueMs,5:0.0})"));
            csv?.WriteLine(string.Create(inv, $"{n},{sample.PipelineMs:0.000},{sample.BufferWaitMs:0.000},{sample.DeviceQueueMs:0.000},{sample.TotalMs:0.000}"));
            if (n >= options.Count)
                done.Cancel();
        };

        var trigger = ThresholdCrossingDetector.BrakeApplied(options.Threshold);
        trigger.Crossed += frame =>
        {
            if (!player.TryClick(frame.ArrivalTimestamp))
                Interlocked.Increment(ref skipped);
        };

        await output.WriteLineAsync($"audio: {player.DeviceName}; {player.Format}; buffer latency requested {options.LatencyMs} ms, negotiated {player.DeclaredLatencyMs} ms").ConfigureAwait(false);
        await output.WriteLineAsync(options.ReplayPath is null
            ? string.Create(inv, $"Waiting for iRacing. Press and release the brake past {options.Threshold:0.##} (sitting in the car is enough). {options.Count} clicks; Ctrl+C to stop early.")
            : string.Create(inv, $"Replaying {options.ReplayPath} in real time; clicking on each brake application past {options.Threshold:0.##}.")).ConfigureAwait(false);

        player.Start();
        var pipeline = new TelemetryPipeline([trigger]);
        var result = await pipeline.RunAsync(source, done.Token).ConfigureAwait(false);

        LatencySample[] all;
        lock (samples)
            all = samples.ToArray();

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(inv,
            $"frames: {result.Frames}, triggers: {trigger.Count}, clicks measured: {all.Length}, skipped while busy: {skipped}, frames dropped: {result.DroppedFrames}")).ConfigureAwait(false);
        if (all.Length == 0)
        {
            await output.WriteLineAsync("No clicks measured.").ConfigureAwait(false);
            return CliApp.Failure;
        }

        await output.WriteLineAsync("telemetry arrival -> click start on device clock:").ConfigureAwait(false);
        await output.WriteLineAsync($"  total           {LatencyStats.From(all.Select(s => s.TotalMs))}").ConfigureAwait(false);
        await output.WriteLineAsync($"  pipeline        {LatencyStats.From(all.Select(s => s.PipelineMs))}").ConfigureAwait(false);
        await output.WriteLineAsync($"  buffer wait     {LatencyStats.From(all.Select(s => s.BufferWaitMs))}").ConfigureAwait(false);
        await output.WriteLineAsync($"  device queue    {LatencyStats.From(all.Select(s => s.DeviceQueueMs))}").ConfigureAwait(false);
        await output.WriteLineAsync("  (converter and speaker delay after the audio endpoint is not visible to software and is not included)").ConfigureAwait(false);
        return CliApp.Ok;
    }
}
