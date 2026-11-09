using CoCoach.Telemetry.Ingest;

namespace CoCoach.Telemetry.Pipeline;

/// <summary>
/// Receives every frame the pipeline processes, in order, on the pipeline's consumer task.
/// Implementations must be fast and must not block: they run inside the 60 Hz loop.
/// </summary>
public interface IFrameObserver
{
    void OnFrame(TelemetryFrame frame);

    /// <summary>Called once when the source has ended.</summary>
    void OnCompleted();
}
