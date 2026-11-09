using System.Globalization;
using System.Runtime.Versioning;
using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Pipeline;
using CoCoach.Telemetry.Recording;

namespace CoCoach.Cli.Commands;

/// <summary>
/// <c>cocoach live [--no-record]</c>: the same per-lap summary as replay, from the live sim, with
/// .ibt recording started when the car goes on track and stopped when it leaves. Windows only.
/// </summary>
internal static class LiveCommand
{
    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return CliApp.WindowsOnlyAsync("live", error);

        var record = !args.Contains("--no-record");
        return RunWindowsAsync(record, output, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    static async Task<int> RunWindowsAsync(bool record, TextWriter output, CancellationToken cancellationToken)
    {
        await using var source = SdkTelemetrySource.OpenLive(CliLogging.Sdk);

        var observers = new List<IFrameObserver>();
        if (record)
        {
            var autoRecord = new AutoRecordController(source.CreateRecorder());
            autoRecord.RecordingChanged += on => output.WriteLine(on
                ? "[rec] car on track: sent TelemCommand Start (.ibt recording on)"
                : "[rec] car left track: sent TelemCommand Stop (.ibt recording off)");
            observers.Add(autoRecord);
        }

        var pipeline = new TelemetryPipeline(observers);
        pipeline.SessionChanged += facts =>
        {
            output.WriteLine();
            output.WriteLine(Formatting.TrackAndCar(facts));
            output.WriteLine(Formatting.LapHeader);
        };
        pipeline.Laps.LapCompleted += lap => output.WriteLine(Formatting.LapRow(lap));

        await output.WriteLineAsync("Waiting for iRacing (needs irsdkEnableMem=1 in Documents\\iRacing\\app.ini). Ctrl+C to stop.").ConfigureAwait(false);
        await output.WriteLineAsync(record
            ? "Auto-recording is on: .ibt files go to Documents\\iRacing\\telemetry."
            : "Auto-recording is off.").ConfigureAwait(false);

        var result = await pipeline.RunAsync(source, cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"stopped. frames: {result.Frames}, dropped by a slow consumer: {result.DroppedFrames}, laps: {result.Laps.Count}")).ConfigureAwait(false);
        return CliApp.Ok;
    }
}
