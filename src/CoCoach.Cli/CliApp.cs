using CoCoach.Cli.Commands;

namespace CoCoach.Cli;

/// <summary>Command-line entry point: <c>cocoach &lt;command&gt; [args]</c>.</summary>
internal static class CliApp
{
    public const int Ok = 0;
    public const int Failure = 1;
    public const int Usage = 2;

    const string Help = """
        cocoach - AI driving coach for iRacing (Phase 0 tools)

        Usage:
          cocoach inspect <file.ibt> [--json]   Header facts, track and car, and the variable checklist
          cocoach replay <file.ibt>             Replay a recording through the pipeline; per-lap summary
          cocoach live [--no-record]            Per-lap summary from the live sim, auto-recording .ibt (Windows)
          cocoach latency [options]             Telemetry-arrival-to-audio-start latency (Windows)
              --count <n>                       Stop after n clicks (default 20)
              --threshold <0-1>                 Brake level that triggers a click (default 0.5)
              --replay <file.ibt>               Trigger from a real-time replay instead of the live sim
              --latency-ms <n>                  Requested WASAPI buffer latency (default 10)
              --exclusive                       WASAPI exclusive mode instead of shared
              --no-low-latency                  Do not request the IAudioClient3 low-latency shared path
              --csv <file>                      Also write every measurement to a CSV file
          cocoach help                          This text
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            await output.WriteLineAsync(Help).ConfigureAwait(false);
            return args.Length == 0 ? Usage : Ok;
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            var rest = args[1..];
            return args[0] switch
            {
                "inspect" => InspectCommand.Run(rest, output, error),
                "replay" => await ReplayCommand.RunAsync(rest, output, error, cts.Token).ConfigureAwait(false),
                "live" => await LiveCommand.RunAsync(rest, output, error, cts.Token).ConfigureAwait(false),
                "latency" => await LatencyCommand.RunAsync(rest, output, error, cts.Token).ConfigureAwait(false),
                _ => await UnknownAsync(args[0], error).ConfigureAwait(false),
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or ArgumentException)
        {
            await error.WriteLineAsync($"error: {ex.Message}").ConfigureAwait(false);
            return Failure;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    static async Task<int> UnknownAsync(string command, TextWriter error)
    {
        await error.WriteLineAsync($"Unknown command '{command}'. Run 'cocoach help'.").ConfigureAwait(false);
        return Usage;
    }

    internal static async Task<int> WindowsOnlyAsync(string command, TextWriter error)
    {
        await error.WriteLineAsync($"'{command}' needs the iRacing sim, which runs only on Windows.").ConfigureAwait(false);
        return Failure;
    }
}
