using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Laps;
using CoCoach.Telemetry.Session;

namespace CoCoach.Telemetry.Pipeline;

/// <summary>What a pipeline run saw.</summary>
public sealed record PipelineResult(
    long Frames,
    long DroppedFrames,
    IReadOnlyList<LapSummary> Laps,
    SessionFacts? Session,
    int SessionInfoUpdates,
    IReadOnlyList<TelemetryVariableInfo> Variables);

/// <summary>
/// The shared pipeline that both live and replay run through: one source, frames handed in order to
/// the lap tracker and any extra observers, session info parsed on its own task.
/// Later phases (track model, reference comparison, cue scheduler) plug in as observers.
/// </summary>
public sealed class TelemetryPipeline
{
    readonly List<IFrameObserver> _observers = [];

    public TelemetryPipeline(IEnumerable<IFrameObserver>? observers = null)
    {
        Laps = new LapTracker();
        _observers.Add(Laps);
        if (observers is not null)
            _observers.AddRange(observers);
    }

    /// <summary>The lap tracker every pipeline runs. Subscribe to <see cref="LapTracker.LapCompleted"/> for live output.</summary>
    public LapTracker Laps { get; }

    /// <summary>Raised on the session task each time a session-info update has been parsed.</summary>
    public event Action<SessionFacts>? SessionChanged;

    /// <summary>Runs <paramref name="source"/> to completion (end of file, or cancellation for live).</summary>
    public async Task<PipelineResult> RunAsync(ITelemetrySource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        SessionFacts? latest = null;
        var sessionUpdates = 0;
        long frames = 0;

        var produce = Task.Run(() => source.RunAsync(stop.Token), CancellationToken.None);

        var sessions = Task.Run(async () =>
        {
            await foreach (var update in source.SessionInfo.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                var facts = SessionInfoParser.Parse(update.Yaml);
                Volatile.Write(ref latest, facts);
                Interlocked.Increment(ref sessionUpdates);
                SessionChanged?.Invoke(facts);
            }
        }, CancellationToken.None);

        var consume = Task.Run(async () =>
        {
            await foreach (var frame in source.Frames.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                frames++;
                foreach (var observer in _observers)
                    observer.OnFrame(frame);
            }
        }, CancellationToken.None);

        try
        {
            // a consumer failure must stop the producer, or a replay would wait forever on a full queue
            var first = await Task.WhenAny(produce, consume).ConfigureAwait(false);
            if (first.IsFaulted)
                await stop.CancelAsync().ConfigureAwait(false);

            await Task.WhenAll(produce, consume, sessions).ConfigureAwait(false);
        }
        finally
        {
            foreach (var observer in _observers)
                observer.OnCompleted();
        }

        return new PipelineResult(
            frames,
            source.DroppedFrames,
            Laps.Laps.ToArray(),
            Volatile.Read(ref latest),
            Volatile.Read(ref sessionUpdates),
            source.Variables);
    }
}
