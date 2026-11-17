using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Laps;
using CoCoach.Telemetry.Pipeline;
using CoCoach.Telemetry.Session;
using Microsoft.Extensions.Logging;

namespace CoCoach.Analysis.Recording;

/// <summary>
/// One recorded session held in memory: every frame, the laps the shared lap tracker found in it,
/// and the privacy-safe session facts. This is the input to every analysis step.
/// </summary>
/// <param name="Label">A short label for the session, such as the file name. Never a driver name.</param>
/// <param name="Facts">Track and player car, when the source carried session info.</param>
/// <param name="Frames">Every frame, in order; <see cref="TelemetryFrame.Index"/> equals the position in this list.</param>
/// <param name="Laps">Laps as <see cref="LapTracker"/> reported them.</param>
public sealed record RecordedSession(
    string Label,
    SessionFacts? Facts,
    IReadOnlyList<TelemetryFrame> Frames,
    IReadOnlyList<LapSummary> Laps)
{
    /// <summary>Frames before and after <paramref name="lap"/>, inclusive of one frame either side of the line.</summary>
    internal IEnumerable<TelemetryFrame> FramesAround(LapSummary lap)
    {
        var first = Math.Max(0, lap.FirstFrameIndex - 1);
        var last = Math.Min(Frames.Count - 1, lap.LastFrameIndex + 1);
        for (var i = first; i <= last; i++)
            yield return Frames[(int)i];
    }
}

/// <summary>Keeps every frame the pipeline hands it, so a session can be analysed after the run.</summary>
public sealed class FrameRecorder : IFrameObserver
{
    readonly List<TelemetryFrame> _frames = [];

    public IReadOnlyList<TelemetryFrame> Frames => _frames;

    public void OnFrame(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frames.Add(frame);
    }

    public void OnCompleted()
    {
    }
}

/// <summary>Loads sessions from .ibt files through the shared pipeline, or from frames already in memory.</summary>
public static class SessionLoader
{
    /// <summary>Replays <paramref name="ibtPath"/> through the shared pipeline as fast as possible and keeps every frame.</summary>
    public static async Task<RecordedSession> LoadIbtAsync(string ibtPath, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        await using var source = SdkTelemetrySource.OpenReplay(ibtPath, logger);
        var recorder = new FrameRecorder();
        var pipeline = new TelemetryPipeline([recorder]);
        var result = await pipeline.RunAsync(source, cancellationToken).ConfigureAwait(false);
        return Normalise(Path.GetFileName(ibtPath), result.Session, recorder.Frames, result.Laps);
    }

    /// <summary>Builds a session from frames already in memory, running the same lap tracker the pipeline runs.</summary>
    public static RecordedSession FromFrames(string label, IEnumerable<TelemetryFrame> frames, SessionFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var tracker = new LapTracker();
        var kept = new List<TelemetryFrame>();
        foreach (var frame in frames)
        {
            kept.Add(frame);
            tracker.OnFrame(frame);
        }
        tracker.OnCompleted();
        return Normalise(label, facts, kept, tracker.Laps.ToArray());
    }

    // laps refer to frames by Index, so indices must equal list positions
    static RecordedSession Normalise(string label, SessionFacts? facts, IReadOnlyList<TelemetryFrame> frames, IReadOnlyList<LapSummary> laps)
    {
        var contiguous = true;
        for (var i = 0; i < frames.Count && contiguous; i++)
            contiguous = frames[i].Index == i;
        if (!contiguous)
            throw new ArgumentException("Frame indices must run 0, 1, 2, ... in stream order.", nameof(frames));
        return new RecordedSession(label, facts, frames, laps);
    }
}
