using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace CoCoach.Cli.Latency;

/// <summary>A click whose first sample has been handed to the audio device.</summary>
/// <param name="Arrival">Telemetry arrival timestamp of the triggering frame.</param>
/// <param name="Detected">When the click was requested.</param>
/// <param name="Written">When the audio engine pulled the buffer that starts with the click.</param>
/// <param name="StartSeconds">Stream position of the click's first sample, in seconds since playback started.</param>
internal readonly record struct WrittenClick(long Arrival, long Detected, long Written, double StartSeconds);

/// <summary>
/// An endless 32-bit float stream of silence that starts a short click at the beginning of the next
/// buffer the audio engine pulls after <see cref="TryTrigger"/>. Plain managed code: it carries no
/// WASAPI dependency, so it is tested on any OS.
/// </summary>
internal sealed class ClickWaveProvider : IWaveProvider
{
    readonly float[] _click;
    readonly Func<long> _clock;
    readonly int _channels;
    long _framesWritten;
    int _clickFrame = -1;          // next click frame to write, -1 when idle
    long _pendingArrival;
    long _pendingDetected;
    int _pending;                  // 1 when a trigger is waiting for the next Read

    public ClickWaveProvider(int sampleRate, int channels, Func<long>? clock = null, double clickMs = 15, double frequencyHz = 1500)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _channels = channels;
        _clock = clock ?? Stopwatch.GetTimestamp;

        // a short decaying tone burst: sharp onset, easy to hear over engine noise
        var frames = Math.Max(1, (int)(sampleRate * clickMs / 1000.0));
        _click = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            var t = (double)i / sampleRate;
            _click[i] = (float)(0.8 * Math.Sin(2 * Math.PI * frequencyHz * t) * Math.Exp(-5.0 * i / frames));
        }
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>Clicks written to the device, in order. The latency monitor drains this.</summary>
    public ConcurrentQueue<WrittenClick> Written { get; } = new();

    /// <summary>Frames handed out so far.</summary>
    public long FramesWritten => Interlocked.Read(ref _framesWritten);

    /// <summary>True while a click is queued or still playing.</summary>
    public bool Busy => Volatile.Read(ref _pending) == 1 || Volatile.Read(ref _clickFrame) >= 0;

    /// <summary>Requests a click. Returns false (and does nothing) while the previous click is still queued or playing.</summary>
    public bool TryTrigger(long arrivalTimestamp)
    {
        if (Busy)
            return false;
        _pendingArrival = arrivalTimestamp;
        _pendingDetected = _clock();
        Volatile.Write(ref _pending, 1);
        return true;
    }

    public int Read(Span<byte> buffer)
    {
        var samples = MemoryMarshal.Cast<byte, float>(buffer);
        var frames = samples.Length / _channels;
        samples.Clear();

        if (Interlocked.Exchange(ref _pending, 0) == 1)
        {
            var startSeconds = (double)Interlocked.Read(ref _framesWritten) / WaveFormat.SampleRate;
            Volatile.Write(ref _clickFrame, 0);
            Written.Enqueue(new WrittenClick(_pendingArrival, _pendingDetected, _clock(), startSeconds));
        }

        var clickFrame = Volatile.Read(ref _clickFrame);
        if (clickFrame >= 0)
        {
            var n = Math.Min(frames, _click.Length - clickFrame);
            for (var f = 0; f < n; f++)
            {
                var value = _click[clickFrame + f];
                for (var c = 0; c < _channels; c++)
                    samples[f * _channels + c] = value;
            }
            clickFrame += n;
            Volatile.Write(ref _clickFrame, clickFrame >= _click.Length ? -1 : clickFrame);
        }

        Interlocked.Add(ref _framesWritten, frames);
        return frames * _channels * sizeof(float);
    }
}
