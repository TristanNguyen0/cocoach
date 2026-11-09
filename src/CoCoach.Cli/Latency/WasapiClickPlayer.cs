using System.Diagnostics;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CoCoach.Cli.Latency;

/// <summary>
/// Plays clicks through WASAPI on the default render device and measures when each one starts.
/// </summary>
/// <remarks>
/// Output runs continuously (silence between clicks), as a live cue player would, so there is no
/// device start-up cost in the measurement. A click starts at the beginning of the next buffer the
/// engine pulls. "Audible" is taken from the device's own audio clock (<c>IAudioClock</c>): the moment
/// its play position reaches the click's first sample. Converter and speaker latency after the
/// endpoint are not visible to software; the negotiated buffer latency is reported alongside.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WasapiClickPlayer : IDisposable
{
    readonly WasapiPlayer _output;
    readonly ClickWaveProvider _provider;
    readonly Thread _monitor;
    readonly CancellationTokenSource _stop = new();
    readonly double _outputBytesPerSecond;

    public WasapiClickPlayer(int latencyMs, bool exclusive, bool lowLatency)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        DeviceName = device.FriendlyName;

        var builder = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithLatency(latencyMs)
            .WithEventSync()
            .WithMmcssThreadPriority("Pro Audio");
        if (exclusive)
            builder = builder.WithExclusiveMode();
        else if (lowLatency)
            builder = builder.WithSharedMode().WithLowLatency(required: false);   // falls back to standard shared mode
        else
            builder = builder.WithSharedMode();
        _output = builder.Build();

        // match the engine's mix format so the shared path needs no resampler
        var mix = _output.DeviceMixFormat;
        _provider = new ClickWaveProvider(mix.SampleRate, mix.Channels);
        _output.Init(_provider);

        var format = _output.OutputWaveFormat;
        _outputBytesPerSecond = format.AverageBytesPerSecond;
        DeclaredLatencyMs = _output.LatencyMilliseconds;
        var path = exclusive ? "exclusive" : _output.LowLatencyActive ? "shared, IAudioClient3 low-latency" : "shared";
        Format = $"{format.SampleRate} Hz, {format.Channels} ch, {format.Encoding}, {path}";
        if (!exclusive && lowLatency && !_output.LowLatencyActive && _output.LowLatencyUnavailableReason is { } why)
            Format += $" (low latency unavailable: {why})";

        _monitor = new Thread(MonitorLoop) { IsBackground = true, Name = "click-latency-monitor", Priority = ThreadPriority.Highest };
    }

    public string DeviceName { get; }

    public string Format { get; }

    /// <summary>The buffer latency WASAPI negotiated (the engine period on the IAudioClient3 path).</summary>
    public int DeclaredLatencyMs { get; }

    /// <summary>Raised on the monitor thread for each click once it has started playing.</summary>
    public event Action<LatencySample>? Measured;

    public void Start()
    {
        _output.Play();
        _monitor.Start();
    }

    /// <summary>Requests a click for the frame that arrived at <paramref name="arrivalTimestamp"/>. False while the previous click is busy.</summary>
    public bool TryClick(long arrivalTimestamp) => _provider.TryTrigger(arrivalTimestamp);

    double DeviceSeconds() => _output.GetPosition() / _outputBytesPerSecond;

    void MonitorLoop()
    {
        var timeout = Stopwatch.Frequency; // give up on a click after one second
        while (!_stop.IsCancellationRequested)
        {
            if (!_provider.Written.TryDequeue(out var click))
            {
                Thread.Sleep(1);
                continue;
            }

            // spin on the device clock until it reaches the click's first sample
            while (!_stop.IsCancellationRequested)
            {
                if (DeviceSeconds() >= click.StartSeconds)
                {
                    Measured?.Invoke(new LatencySample(click.Arrival, click.Detected, click.Written, Stopwatch.GetTimestamp()));
                    break;
                }
                if (Stopwatch.GetTimestamp() - click.Written > timeout)
                    break;
                Thread.Yield();
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_monitor.IsAlive)
            _monitor.Join(TimeSpan.FromSeconds(1));
        _output.Stop();
        _output.Dispose();
        _stop.Dispose();
    }
}
