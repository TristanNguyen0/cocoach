using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Pipeline;

namespace CoCoach.Telemetry.Events;

/// <summary>
/// Fires once each time a channel rises through <see cref="Threshold"/>, then re-arms only after the
/// value has fallen back below <see cref="ResetBelow"/>. Deterministic, so it gives the latency tool a
/// repeatable trigger (for example Brake crossing 0.5).
/// </summary>
public sealed class ThresholdCrossingDetector : IFrameObserver
{
    readonly Func<TelemetryFrame, float?> _select;
    bool _armed = true;

    public ThresholdCrossingDetector(Func<TelemetryFrame, float?> select, float threshold, float resetBelow)
    {
        ArgumentNullException.ThrowIfNull(select);
        if (resetBelow > threshold)
            throw new ArgumentOutOfRangeException(nameof(resetBelow), "Must not be above the threshold.");
        _select = select;
        Threshold = threshold;
        ResetBelow = resetBelow;
    }

    /// <summary>Brake pedal rising through 0.5, re-armed below 0.2.</summary>
    public static ThresholdCrossingDetector BrakeApplied(float threshold = 0.5f, float resetBelow = 0.2f) =>
        new(f => f.Brake, threshold, resetBelow);

    public float Threshold { get; }
    public float ResetBelow { get; }

    /// <summary>Crossings seen so far.</summary>
    public int Count { get; private set; }

    /// <summary>Raised on the pipeline task with the frame that crossed the threshold.</summary>
    public event Action<TelemetryFrame>? Crossed;

    public void OnFrame(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_select(frame) is not { } value || float.IsNaN(value))
            return;

        if (_armed && value >= Threshold)
        {
            _armed = false;
            Count++;
            Crossed?.Invoke(frame);
        }
        else if (!_armed && value < ResetBelow)
        {
            _armed = true;
        }
    }

    public void OnCompleted()
    {
    }
}
