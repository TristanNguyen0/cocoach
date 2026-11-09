using CoCoach.Telemetry.Ingest;
using CoCoach.Telemetry.Pipeline;

namespace CoCoach.Telemetry.Laps;

/// <summary>Summary of one lap seen in the telemetry stream.</summary>
/// <param name="Lap">iRacing's lap number.</param>
/// <param name="LapTime">
/// Measured lap time in seconds: the interpolated start/finish crossing at the end minus the one at the start.
/// <see langword="null"/> unless both crossings were seen (see <see cref="IsComplete"/>).
/// </param>
/// <param name="ReportedLapTime">iRacing's own <c>LapLastLapTime</c> for this lap, when it published a valid one.</param>
/// <param name="MinSpeed">Lowest speed on the lap, metres per second (null when the source has no Speed).</param>
/// <param name="MaxSpeed">Highest speed on the lap, metres per second.</param>
/// <param name="Frames">On-track frames counted for this lap.</param>
/// <param name="IsComplete">
/// Both start/finish crossings were seen as a genuine wrap of <c>LapDistPct</c> (from near 1 to near 0)
/// and the car stayed on track in between.
/// </param>
/// <param name="PitRoad">The car was on pit road at some point during the lap.</param>
public sealed record LapSummary(
    int Lap,
    double? LapTime,
    double? ReportedLapTime,
    float? MinSpeed,
    float? MaxSpeed,
    int Frames,
    bool IsComplete,
    bool PitRoad);

/// <summary>
/// Splits a frame stream into laps and summarises each one. Deterministic: the same frames always
/// produce the same summaries, which is what lets every later phase be tested against .ibt replays.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A lap ends when <c>Lap</c> increases by one. The crossing time is interpolated between the two
/// frames either side of the line using <c>LapDistPct</c>, so lap times are not quantised to 1/60 s.
/// An increment without a wrap of <c>LapDistPct</c> (iRacing bumps <c>Lap</c> when a car starts just
/// behind the line or leaves the pits) still starts a new lap, but that lap cannot be complete.</item>
/// <item>Any other change of <c>Lap</c> (a reset, a new session) closes the current lap as incomplete.</item>
/// <item>Frames with <c>IsOnTrack == false</c> are skipped and mark the lap incomplete.</item>
/// <item>A closed lap is held back for up to <see cref="ReportedTimeWindowSeconds"/> of sim time so that
/// iRacing's <c>LapLastLapTime</c>, which it publishes shortly after the crossing, can be attached.</item>
/// </list>
/// </remarks>
public sealed class LapTracker : IFrameObserver
{
    /// <summary>How long after a crossing to wait for iRacing's own lap time.</summary>
    public const double ReportedTimeWindowSeconds = 3.0;

    readonly List<LapSummary> _laps = [];
    LapState? _current;
    PendingLap? _pending;
    TelemetryFrame? _previous;

    /// <summary>Raised for each lap as soon as its summary is final.</summary>
    public event Action<LapSummary>? LapCompleted;

    /// <summary>Every lap emitted so far, in order.</summary>
    public IReadOnlyList<LapSummary> Laps => _laps;

    public void OnFrame(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Lap is not { } lap || frame.SessionTime is not { } time)
            return;

        ResolvePending(frame, time);

        if (frame.IsOnTrack == false)
        {
            // in the garage or a replay of someone else; this lap can no longer be timed
            if (_current is not null)
                _current.Interrupted = true;
            _previous = null;
            return;
        }

        if (_current is null)
        {
            _current = new LapState(lap, startTime: null);
        }
        else if (lap == _current.Lap + 1)
        {
            var (crossing, verified) = CrossingTime(_previous, frame, time);
            CloseCurrent(crossing, verified, frame);
            _current = new LapState(lap, verified ? crossing : null);
        }
        else if (lap != _current.Lap)
        {
            Emit(_current.ToSummary(endTime: null, reported: null));
            _current = new LapState(lap, startTime: null);
        }

        _current.Add(frame);
        _previous = frame;
    }

    /// <summary>End of stream: release any held-back lap and the unfinished current lap.</summary>
    public void OnCompleted()
    {
        if (_pending is not null)
        {
            Emit(_pending.Summary);
            _pending = null;
        }

        if (_current is { Frames: > 0 })
            Emit(_current.ToSummary(endTime: null, reported: null));
        _current = null;
        _previous = null;
    }

    void CloseCurrent(double crossing, bool verified, TelemetryFrame frame)
    {
        // a lap still waiting for its reported time is final now
        if (_pending is not null)
        {
            Emit(_pending.Summary);
            _pending = null;
        }

        var summary = _current!.ToSummary(verified ? crossing : null, reported: null);
        _pending = new PendingLap(summary, crossing, frame.LapLastLapTime);
    }

    void ResolvePending(TelemetryFrame frame, double time)
    {
        if (_pending is null)
            return;

        var reported = frame.LapLastLapTime;
        if (reported is { } value && value > 0 && reported != _pending.LastLapTimeAtCrossing)
        {
            Emit(_pending.Summary with { ReportedLapTime = value });
            _pending = null;
        }
        else if (time - _pending.CrossingTime > ReportedTimeWindowSeconds)
        {
            // iRacing may publish the same value when two laps tie to the ms, or -1 for an invalid lap
            var fallback = _pending.LastLapTimeAtCrossing is { } atCrossing && atCrossing > 0
                && _pending.Summary.LapTime is { } measured && Math.Abs(measured - atCrossing) < 0.05
                ? (double?)atCrossing
                : null;
            Emit(_pending.Summary with { ReportedLapTime = fallback });
            _pending = null;
        }
    }

    void Emit(LapSummary summary)
    {
        _laps.Add(summary);
        LapCompleted?.Invoke(summary);
    }

    // interpolate the start/finish crossing between the frame before the line and the one after it.
    // verified is false when the two frames do not show LapDistPct wrapping from the end of one lap
    // to the start of the next, so the crossing time is not trustworthy
    internal static (double Time, bool Verified) CrossingTime(TelemetryFrame? before, TelemetryFrame after, double afterTime)
    {
        if (before?.SessionTime is not { } t0 || before.LapDistPct is not { } p0 || after.LapDistPct is not { } p1)
            return (afterTime, false);

        if (p0 < 0.9f || p1 > 0.1f || afterTime <= t0 || afterTime - t0 > 0.5)
            return (afterTime, false);

        // LapDistPct can read a hair below zero just after the line
        var toLine = 1.0 - p0;
        var past = Math.Max(0.0, p1);
        var span = toLine + past;
        if (span <= 0)
            return (afterTime, true);

        return (t0 + (afterTime - t0) * (toLine / span), true);
    }

    sealed record PendingLap(LapSummary Summary, double CrossingTime, float? LastLapTimeAtCrossing);

    sealed class LapState(int lap, double? startTime)
    {
        public int Lap { get; } = lap;
        public double? StartTime { get; } = startTime;
        public bool Interrupted { get; set; }
        public int Frames { get; private set; }
        float? _min;
        float? _max;
        bool _pitRoad;

        public void Add(TelemetryFrame frame)
        {
            Frames++;
            if (frame.OnPitRoad == true)
                _pitRoad = true;
            if (frame.Speed is { } speed && float.IsFinite(speed))
            {
                _min = _min is { } min ? Math.Min(min, speed) : speed;
                _max = _max is { } max ? Math.Max(max, speed) : speed;
            }
        }

        public LapSummary ToSummary(double? endTime, double? reported)
        {
            var complete = StartTime is not null && endTime is not null && !Interrupted;
            return new LapSummary(
                Lap,
                complete ? endTime!.Value - StartTime!.Value : null,
                reported,
                _min,
                _max,
                Frames,
                complete,
                _pitRoad);
        }
    }
}
