using System.Threading.Channels;

namespace CoCoach.Telemetry.Ingest;

/// <summary>
/// One ingest abstraction for both telemetry sources: the live iRacing feed and .ibt replay.
/// Both yield the same <see cref="TelemetryFrame"/> stream, so everything downstream can be
/// built and tested against recorded files.
/// </summary>
/// <remarks>
/// Call <see cref="RunAsync"/> once and read <see cref="Frames"/> and <see cref="SessionInfo"/>
/// concurrently. Both channels complete when the source ends (end of file, or cancellation).
/// Session info arrives on its own channel, only when iRacing's <c>sessionInfoUpdate</c> counter
/// changes, and is never parsed on the thread that copies telemetry rows.
/// </remarks>
public interface ITelemetrySource : IAsyncDisposable
{
    /// <summary>True for the live sim feed, false for file replay.</summary>
    bool IsLive { get; }

    /// <summary>Telemetry rows, in order.</summary>
    ChannelReader<TelemetryFrame> Frames { get; }

    /// <summary>Raw session-info YAML, one item per <c>sessionInfoUpdate</c> change.</summary>
    ChannelReader<SessionInfoUpdate> SessionInfo { get; }

    /// <summary>
    /// The variables the source carries. Empty until the source has connected
    /// (the first frame or session update has arrived).
    /// </summary>
    IReadOnlyList<TelemetryVariableInfo> Variables { get; }

    /// <summary>Frames dropped because the consumer fell behind (live feed only; replay never drops).</summary>
    long DroppedFrames { get; }

    /// <summary>Produces frames until the source ends or <paramref name="cancellationToken"/> fires.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}

/// <summary>A session-info YAML document, delivered when iRacing's <c>sessionInfoUpdate</c> counter changes.</summary>
/// <param name="Sequence">Zero-based count of updates delivered by this source.</param>
/// <param name="Yaml">The raw YAML. It contains every driver's name and custid: never log or transmit it.</param>
public sealed record SessionInfoUpdate(int Sequence, string Yaml);

/// <summary>Describes one telemetry variable a source carries.</summary>
public sealed record TelemetryVariableInfo(string Name, string Type, int Count, string Unit, string Description);
