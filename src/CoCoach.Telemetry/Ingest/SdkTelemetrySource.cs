using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Channels;
using CoCoach.Telemetry.Recording;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;

namespace CoCoach.Telemetry.Ingest;

/// <summary>
/// <see cref="ITelemetrySource"/> built on SVappsLAB.iRacingTelemetrySDK, which reads the live
/// memory-mapped feed and replays .ibt files through one client.
/// </summary>
/// <remarks>
/// The SDK runs in synchronous delivery mode: each row is handed to our handler before the next
/// is read, which is what makes by-name lookup (<c>GetValue</c>) consistent with the row. The handler
/// only copies the row into a <see cref="TelemetryFrame"/> and queues it; all processing happens on
/// the consumer side of <see cref="Frames"/>.
/// <list type="bullet">
/// <item>Replay applies back-pressure, so no frame is ever dropped and results are deterministic.</item>
/// <item>Live never blocks the SDK reader: when the consumer falls behind, the oldest queued frame is
/// dropped and counted in <see cref="DroppedFrames"/>.</item>
/// </list>
/// </remarks>
public sealed class SdkTelemetrySource : ITelemetrySource
{
    const int LiveQueueCapacity = 120;   // 2 s at 60 Hz
    const int ReplayQueueCapacity = 1024;

    readonly ITelemetryClient<DynamicTelemetryData> _client;
    readonly Channel<TelemetryFrame> _frames;
    readonly Channel<SessionInfoUpdate> _sessionInfo;
    readonly ILogger _logger;
    IReadOnlyList<TelemetryVariableInfo> _variables = [];
    HashSet<string>? _available;
    long _index;
    long _dropped;
    int _sessionSequence;
    int _started;

    SdkTelemetrySource(ITelemetryClient<DynamicTelemetryData> client, bool isLive, ILogger logger)
    {
        _client = client;
        _logger = logger;
        IsLive = isLive;

        _frames = isLive
            ? Channel.CreateBounded<TelemetryFrame>(
                new BoundedChannelOptions(LiveQueueCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true,
                },
                _ => Interlocked.Increment(ref _dropped))
            : Channel.CreateBounded<TelemetryFrame>(
                new BoundedChannelOptions(ReplayQueueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                });

        _sessionInfo = Channel.CreateUnbounded<SessionInfoUpdate>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    }

    /// <summary>Opens an .ibt file for replay. Works on every OS.</summary>
    /// <param name="ibtPath">Path to an .ibt file.</param>
    /// <param name="logger">SDK diagnostics; defaults to none.</param>
    /// <param name="playbackSpeed">1 replays in real time; <see cref="int.MaxValue"/> (the default) as fast as possible.</param>
    public static SdkTelemetrySource OpenReplay(string ibtPath, ILogger? logger = null, int playbackSpeed = int.MaxValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(ibtPath);
        var fullPath = Path.GetFullPath(ibtPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"IBT file not found: {fullPath}", fullPath);

        // the SDK sizes the file with FileInfo.Length, which is the link's own size for a symbolic link
        if (File.ResolveLinkTarget(fullPath, returnFinalTarget: true) is { } target)
            fullPath = target.FullName;

        // the SDK only accepts a lower-case ".ibt" extension
        if (!fullPath.EndsWith(".ibt", StringComparison.Ordinal))
            throw new ArgumentException($"Not an .ibt file (the extension must be lower-case .ibt): {fullPath}", nameof(ibtPath));

        logger ??= NullLogger.Instance;
        var client = TelemetryClient<DynamicTelemetryData>.Create(
            logger,
            new IBTOptions(fullPath, playbackSpeed),
            new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous });
        return new SdkTelemetrySource(client, isLive: false, logger);
    }

    /// <summary>
    /// Connects to the live iRacing feed (<c>Local\IRSDKMemMapFileName</c>). Windows only.
    /// Waits quietly until the sim is running; requires <c>irsdkEnableMem=1</c> in app.ini.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static SdkTelemetrySource OpenLive(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The live iRacing feed exists only on Windows.");

        logger ??= NullLogger.Instance;
        var client = TelemetryClient<DynamicTelemetryData>.Create(
            logger,
            ibtOptions: null,
            new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous });
        return new SdkTelemetrySource(client, isLive: true, logger);
    }

    /// <inheritdoc />
    public bool IsLive { get; }

    /// <inheritdoc />
    public ChannelReader<TelemetryFrame> Frames => _frames.Reader;

    /// <inheritdoc />
    public ChannelReader<SessionInfoUpdate> SessionInfo => _sessionInfo.Reader;

    /// <inheritdoc />
    public IReadOnlyList<TelemetryVariableInfo> Variables => Volatile.Read(ref _variables);

    /// <inheritdoc />
    public long DroppedFrames => Interlocked.Read(ref _dropped);

    /// <summary>
    /// Creates the recorder that starts and stops iRacing's disk telemetry (.ibt) recording
    /// through <c>irsdk_BroadcastTelemCommand</c>. Live source on Windows only.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public ITelemetryRecorder CreateRecorder()
    {
        if (!IsLive)
            throw new InvalidOperationException("Recording commands only apply to the live feed.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("iRacing broadcast messages exist only on Windows.");

        return new SdkTelemetryRecorder(_client.SimControl.TelemetryRecording);
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("RunAsync can only be called once per source.");

        var handlers = new TelemetryHandlers<DynamicTelemetryData>
        {
            OnTelemetryUpdate = _ => OnRow(cancellationToken),
            OnRawSessionInfoUpdate = OnSessionInfo,
            OnError = error =>
            {
                _logger.LogWarning(error, "Telemetry SDK error");
                return Task.CompletedTask;
            },
        };

        Exception? failure = null;
        try
        {
            await _client.Monitor(handlers, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // normal stop
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            _frames.Writer.TryComplete(failure);
            _sessionInfo.Writer.TryComplete(failure);
        }
    }

    // runs on the SDK's reader task, once per row. copy out and queue - nothing else
    Task OnRow(CancellationToken cancellationToken)
    {
        var arrival = Stopwatch.GetTimestamp();
        var available = _available ?? RefreshVariables();

        var frame = TelemetryFrameReader.Read(
            name => available.Contains(name) ? _client.GetValue(name) : null,
            _index++,
            arrival);

        if (_frames.Writer.TryWrite(frame))
            return Task.CompletedTask;

        // only replay can be full (live drops the oldest instead), so wait for the consumer
        return _frames.Writer.WriteAsync(frame, cancellationToken).AsTask();
    }

    Task OnSessionInfo(string yaml)
    {
        // a new session can change the variable list (different car), so re-read it
        RefreshVariables();
        _sessionInfo.Writer.TryWrite(new SessionInfoUpdate(_sessionSequence++, yaml));
        return Task.CompletedTask;
    }

    HashSet<string> RefreshVariables()
    {
        var vars = _client.GetTelemetryVariables();
        var infos = vars
            .Select(v => new TelemetryVariableInfo(v.Name, DescribeType(v.Type), v.Length, v.Units, v.Desc))
            .ToArray();
        var names = new HashSet<string>(infos.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);

        Volatile.Write(ref _variables, infos);
        if (names.Count > 0)
            _available = names;
        return names;
    }

    static string DescribeType(Type type)
    {
        var element = type.IsArray ? type.GetElementType()! : type;
        return element == typeof(float) ? "float"
            : element == typeof(double) ? "double"
            : element == typeof(int) ? "int"
            : element == typeof(bool) ? "bool"
            : element == typeof(byte) || element == typeof(string) ? "char"
            : element.Name;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
