using System.Runtime.Versioning;
using SVappsLAB.iRacingTelemetrySDK.SimControl;

namespace CoCoach.Telemetry.Recording;

/// <summary>
/// Starts and stops iRacing's disk telemetry (.ibt) recording.
/// This is the only thing cocoach ever sends to the sim.
/// </summary>
public interface ITelemetryRecorder
{
    /// <summary>Sends <c>irsdk_BroadcastTelemCommand</c> Start.</summary>
    void Start();

    /// <summary>Sends <c>irsdk_BroadcastTelemCommand</c> Stop.</summary>
    void Stop();
}

/// <summary>
/// <see cref="ITelemetryRecorder"/> over the SDK's telemetry-recording commands.
/// It deliberately exposes only Start and Stop; no other broadcast command is reachable from here.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SdkTelemetryRecorder(ITelemetryRecordingCommands commands) : ITelemetryRecorder
{
    public void Start() => commands.Start();

    public void Stop() => commands.Stop();
}
