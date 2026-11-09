using System.Diagnostics;
using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Pipeline;

namespace CoCoach.Telemetry.Recording;

/// <summary>
/// Starts .ibt recording when the player car goes on track and stops it when the car leaves,
/// so every driving session leaves a GPS-bearing recording behind.
/// </summary>
/// <remarks>
/// "On track" is iRacing's <c>IsOnTrack</c>: the player is in the car with physics running.
/// A state change must hold for <see cref="DebounceFrames"/> consecutive frames before a command
/// is sent, so a single odd frame (a reset, a tow) does not toggle recording.
/// Frames without <c>IsOnTrack</c> are ignored.
/// A gap of more than <see cref="SourceGap"/> between frames means the sim closed or loaded a new session,
/// which ends its recording, so the controller forgets it was recording and starts again on the next stint.
/// </remarks>
public sealed class AutoRecordController(ITelemetryRecorder recorder, int debounceFrames = AutoRecordController.DefaultDebounceFrames) : IFrameObserver
{
    /// <summary>Half a second at 60 Hz.</summary>
    public const int DefaultDebounceFrames = 30;

    /// <summary>A pause in the live feed longer than this means the sim stopped (exit or session load).</summary>
    public static readonly TimeSpan SourceGap = TimeSpan.FromSeconds(2);

    readonly ITelemetryRecorder _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    long _lastArrival;

    bool _candidate;
    int _candidateFrames;

    public int DebounceFrames { get; } = debounceFrames >= 1
        ? debounceFrames
        : throw new ArgumentOutOfRangeException(nameof(debounceFrames), "Must be at least 1.");

    /// <summary>True while this controller has started recording and not yet stopped it.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>Raised after a Start (true) or Stop (false) command was sent.</summary>
    public event Action<bool>? RecordingChanged;

    public void OnFrame(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (_lastArrival != 0 && frame.ArrivalTimestamp - _lastArrival > SourceGap.TotalSeconds * Stopwatch.Frequency)
        {
            // the sim stopped recording on its own; do not send Stop, but start again when back on track
            IsRecording = false;
            _candidateFrames = 0;
        }
        _lastArrival = frame.ArrivalTimestamp;

        if (frame.IsOnTrack is not { } onTrack)
            return;

        if (onTrack == IsRecording)
        {
            // already in the wanted state; forget any half-seen change
            _candidateFrames = 0;
            return;
        }

        if (onTrack != _candidate || _candidateFrames == 0)
        {
            _candidate = onTrack;
            _candidateFrames = 0;
        }

        if (++_candidateFrames < DebounceFrames)
            return;

        _candidateFrames = 0;
        if (onTrack)
            Start();
        else
            Stop();
    }

    /// <summary>The source ended (sim closed or app stopping): stop a recording we started.</summary>
    public void OnCompleted()
    {
        if (IsRecording)
            Stop();
    }

    void Start()
    {
        _recorder.Start();
        IsRecording = true;
        RecordingChanged?.Invoke(true);
    }

    void Stop()
    {
        _recorder.Stop();
        IsRecording = false;
        RecordingChanged?.Invoke(false);
    }
}
