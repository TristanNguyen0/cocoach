using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using CoCoach.Analysis.Alignment;
using CoCoach.Analysis.Corners;
using CoCoach.Telemetry.Session;

namespace CoCoach.Analysis.Track;

/// <summary>Identifies a track layout: iRacing's track id plus its configuration.</summary>
/// <param name="TrackId">WeekendInfo.TrackID, when known.</param>
/// <param name="TrackName">WeekendInfo.TrackName (iRacing's internal name), used when there is no id.</param>
/// <param name="Config">WeekendInfo.TrackConfigName; empty for a track with one layout.</param>
public sealed record TrackKey(int? TrackId, string? TrackName, string? Config)
{
    public static TrackKey From(SessionFacts? facts) =>
        new(facts?.TrackId, facts?.TrackName ?? facts?.TrackDisplayName, facts?.TrackConfigName ?? string.Empty);

    /// <summary>True when both keys name the same layout (id when both have one, otherwise the name).</summary>
    public bool SameLayout(TrackKey other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var sameTrack = TrackId is not null && other.TrackId is not null
            ? TrackId == other.TrackId
            : string.Equals(TrackName, other.TrackName, StringComparison.OrdinalIgnoreCase);
        return sameTrack && string.Equals(Config ?? string.Empty, other.Config ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A file-system-safe name for this layout.</summary>
    public string Slug
    {
        get
        {
            var track = TrackId?.ToString(CultureInfo.InvariantCulture) ?? Sanitise(TrackName ?? "unknown");
            var config = Sanitise(Config ?? string.Empty);
            return config.Length == 0 ? $"track-{track}" : $"track-{track}-{config}";
        }
    }

    static string Sanitise(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}

/// <summary>How a track map's geometry was obtained.</summary>
public enum TrackMapSource
{
    /// <summary>Averaged .ibt GPS (Lat/Lon/Alt) in a local east-north-up frame.</summary>
    Gps,

    /// <summary>Integrated speed and yaw rate with loop closure at start/finish (no GPS in the recordings).</summary>
    DeadReckoning,
}

/// <summary>
/// A track layout as a function of <c>LapDist</c>: a centreline on the same 1 m grid as every
/// <see cref="LapTrace"/>, its heading and curvature, estimated edges, and the detected corners.
/// Coordinates are metres east (X) and north (Y) of <see cref="Origin"/> (GPS maps), or of the
/// start/finish line in an arbitrary rotation (dead-reckoned maps).
/// </summary>
public sealed record TrackModel
{
    /// <summary>Format version of the persisted model.</summary>
    public int Version { get; init; } = 1;

    public required TrackKey Key { get; init; }

    public required double Length { get; init; }

    public required double Step { get; init; }

    public required TrackMapSource Source { get; init; }

    /// <summary>Number of clean laps averaged into the centreline.</summary>
    public required int LapsUsed { get; init; }

    /// <summary>Anchor of the ENU frame; <see langword="null"/> for a dead-reckoned map.</summary>
    public GeoOrigin? Origin { get; init; }

    public required double[] X { get; init; }
    public required double[] Y { get; init; }

    /// <summary>Height above <see cref="Origin"/>, metres (GPS maps only).</summary>
    public double[]? Z { get; init; }

    /// <summary>Direction of travel, radians counter-clockwise from east (from +X).</summary>
    public required double[] Heading { get; init; }

    /// <summary>Signed curvature, 1/m (positive = left turn), as used for corner detection.</summary>
    public required double[] Curvature { get; init; }

    /// <summary>
    /// Lateral offset of the left and right track edge from the centreline, metres (left positive, right negative);
    /// NaN where no lap gave evidence. Estimated from the lateral envelope of laps and off-track samples
    /// (GPS maps only).
    /// </summary>
    public double[]? LeftEdge { get; init; }

    public double[]? RightEdge { get; init; }

    /// <summary>Dead-reckoned maps: distance between the integrated end point and the start before loop closure, metres.</summary>
    public double? ClosureError { get; init; }

    /// <summary>WeekendInfo.TrackNumTurns, when the recordings carried it.</summary>
    public int? ExpectedTurns { get; init; }

    /// <summary>Detected corners, in order of distance.</summary>
    public IReadOnlyList<Corner> Corners { get; init; } = [];

    [JsonIgnore]
    public int Count => X.Length;

    /// <summary>
    /// Lateral offset of <paramref name="lap"/> from the centreline at every grid point, metres
    /// (positive = left of the centreline). <see langword="null"/> unless this is a GPS map and the lap has GPS.
    /// </summary>
    public double[]? LateralOffsets(LapTrace lap)
    {
        ArgumentNullException.ThrowIfNull(lap);
        if (Source != TrackMapSource.Gps || Origin is null || lap.Lat is null || lap.Lon is null)
            return null;

        var n = Math.Min(Count, lap.Count);
        var result = new double[n];
        var any = false;
        for (var i = 0; i < n; i++)
        {
            if (!double.IsFinite(lap.Lat[i]) || !double.IsFinite(lap.Lon[i]))
            {
                result[i] = double.NaN;
                continue;
            }
            var (e, nth, _) = Geodesy.ToEnu(Origin, lap.Lat[i], lap.Lon[i], lap.Alt is { } alt && double.IsFinite(alt[i]) ? alt[i] : Origin.Alt);
            var h = Heading[i];
            result[i] = -(e - X[i]) * Math.Sin(h) + (nth - Y[i]) * Math.Cos(h);
            any = true;
        }
        return any ? result : null;
    }
}
