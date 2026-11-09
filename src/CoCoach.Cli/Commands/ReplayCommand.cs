using System.Globalization;
using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Pipeline;

namespace CoCoach.Cli.Commands;

/// <summary><c>cocoach replay &lt;file.ibt&gt;</c>: run a recording through the shared pipeline and print each lap.</summary>
internal static class ReplayCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length != 1)
        {
            await error.WriteLineAsync("usage: cocoach replay <file.ibt>").ConfigureAwait(false);
            return CliApp.Usage;
        }

        await using var source = SdkTelemetrySource.OpenReplay(args[0], CliLogging.Sdk);
        var pipeline = new TelemetryPipeline();
        var result = await pipeline.RunAsync(source, cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync(Formatting.TrackAndCar(result.Session)).ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"frames: {result.Frames}, laps: {result.Laps.Count}, complete: {result.Laps.Count(l => l.IsComplete)}")).ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync(Formatting.LapHeader).ConfigureAwait(false);
        foreach (var lap in result.Laps)
            await output.WriteLineAsync(Formatting.LapRow(lap)).ConfigureAwait(false);

        var best = result.Laps.Where(l => l.IsComplete && !l.PitRoad).MinBy(l => l.LapTime);
        if (best is not null)
        {
            await output.WriteLineAsync().ConfigureAwait(false);
            await output.WriteLineAsync($"best complete lap: {best.Lap} in {Formatting.LapTime(best.LapTime)}").ConfigureAwait(false);
        }
        return CliApp.Ok;
    }
}
