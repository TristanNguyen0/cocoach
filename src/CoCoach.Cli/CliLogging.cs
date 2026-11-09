using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace CoCoach.Cli;

/// <summary>SDK diagnostics (dropped live frames, truncated files) to stderr; warnings and above only.</summary>
internal static class CliLogging
{
    static readonly Lazy<ILoggerFactory> Factory = new(() => LoggerFactory.Create(builder => builder
        .SetMinimumLevel(LogLevel.Warning)
        .AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.ColorBehavior = LoggerColorBehavior.Disabled;
        })
        .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)));

    public static ILogger Sdk => Factory.Value.CreateLogger("iRacingSDK");
}
