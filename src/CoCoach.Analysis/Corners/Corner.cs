using System.Text.Json.Serialization;

namespace CoCoach.Analysis.Corners;

public enum CornerDirection
{
    Left,
    Right,
}

/// <summary>
/// One corner of a track layout. Every position is a <c>LapDist</c> in metres, fixed for the track,
/// so the same corner can be compared across laps.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Start"/> to <see cref="End"/> is the corner's analysis window: from the typical braking
/// onset (or shortly before turn-in when nobody brakes) to the typical return to full throttle.</item>
/// <item><see cref="TurnIn"/> to <see cref="Exit"/> is the geometric corner, where curvature exceeds the detection threshold.</item>
/// <item>The entry, mid and exit sub-segments are Start-TurnIn, TurnIn-Exit and Exit-End.</item>
/// <item><see cref="SegmentStart"/> to <see cref="SegmentEnd"/> tiles the whole lap (boundaries halfway
/// between neighbouring windows); the optimal composite takes the best lap per segment.</item>
/// </list>
/// </remarks>
public sealed record Corner
{
    /// <summary>1-based, in order of distance from the start/finish line.</summary>
    public required int Number { get; init; }

    /// <summary>"T1", "T2", ... (in detection order; not necessarily the circuit's official numbering).</summary>
    public required string Name { get; init; }

    public required CornerDirection Direction { get; init; }

    public required double Start { get; init; }
    public required double TurnIn { get; init; }

    /// <summary>Geometric apex: the point of highest curvature.</summary>
    public required double Apex { get; init; }

    public required double Exit { get; init; }
    public required double End { get; init; }

    public required double SegmentStart { get; init; }
    public required double SegmentEnd { get; init; }

    /// <summary>Total change of heading through the corner, degrees (always positive).</summary>
    public required double HeadingChange { get; init; }

    /// <summary>Tightest radius, metres (1 / peak curvature).</summary>
    public required double MinRadius { get; init; }

    /// <summary>+1 for a left-hand corner, -1 for a right-hander.</summary>
    [JsonIgnore]
    public int Sign => Direction == CornerDirection.Left ? 1 : -1;
}
