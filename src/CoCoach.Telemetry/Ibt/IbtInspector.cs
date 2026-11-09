using CoCoach.Telemetry.Session;

namespace CoCoach.Telemetry.Ibt;

/// <summary>Present/absent result for one variable, with its unit and the range of values recorded.</summary>
public sealed record VariableCheck
{
    public required string Name { get; init; }
    public required bool Present { get; init; }
    public string? Type { get; init; }
    public int? Count { get; init; }
    public string? Unit { get; init; }
    public string? Description { get; init; }

    /// <summary>Values read (records times array length).</summary>
    public long Samples { get; init; }

    /// <summary>Values that were neither zero nor NaN. Present-but-always-zero means the sim does not fill it.</summary>
    public long NonZeroSamples { get; init; }

    public double? Min { get; init; }
    public double? Max { get; init; }

    public bool Populated => NonZeroSamples > 0;
}

/// <summary>Everything <c>cocoach inspect</c> reports about one .ibt file.</summary>
public sealed record IbtInspection
{
    public required string File { get; init; }
    public required long FileBytes { get; init; }
    public required IbtHeader Header { get; init; }
    public required SessionFacts Session { get; init; }
    public required IReadOnlyList<VariableCheck> Checklist { get; init; }

    /// <summary>Every 360 Hz sub-tick (<c>_ST</c>) variable in the file.</summary>
    public required IReadOnlyList<VariableCheck> SubTickVariables { get; init; }

    public required int TotalVariables { get; init; }

    /// <summary>Lat and Lon are present and carry non-zero values: track maps can be built from this file.</summary>
    public bool HasGps => Has("Lat") && Has("Lon");

    /// <summary>Every per-tyre kerb channel is present.</summary>
    public bool HasKerbChannels => Checklist.Where(c => c.Name.EndsWith("_RumblePitch", StringComparison.Ordinal)).All(c => c.Present);

    bool Has(string name) => Checklist.FirstOrDefault(c => c.Name == name) is { Present: true, Populated: true };
}

/// <summary>Builds an <see cref="IbtInspection"/>: header facts, track and player car, and the variable checklist.</summary>
public static class IbtInspector
{
    /// <summary>The variables the research report relies on, checked in every file.</summary>
    public static IReadOnlyList<string> ChecklistVariables { get; } =
    [
        "Lat", "Lon", "Alt",
        "LapDist", "LapDistPct",
        "Speed", "Throttle", "Brake", "SteeringWheelAngle", "Gear",
        "YawRate", "YawNorth", "VelocityX", "VelocityY", "LatAccel", "LongAccel",
        "PlayerTrackSurface", "PlayerTrackSurfaceMaterial",
        "TireLF_RumblePitch", "TireRF_RumblePitch", "TireLR_RumblePitch", "TireRR_RumblePitch",
        "LapDeltaToBestLap", "LapDeltaToOptimalLap",
    ];

    public const string SubTickSuffix = "_ST";

    public static IbtInspection Inspect(string path)
    {
        var fullPath = Path.GetFullPath(path);
        using var file = IbtFile.Open(fullPath);

        var session = SessionInfoParser.Parse(file.ReadSessionInfoYaml());

        var subTick = file.Variables
            .Where(v => v.Name.EndsWith(SubTickSuffix, StringComparison.Ordinal))
            .OrderBy(v => v.Name, StringComparer.Ordinal)
            .ToArray();

        var checklistPresent = ChecklistVariables
            .Select(file.Find)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToArray();

        var toScan = checklistPresent.Concat(subTick).DistinctBy(v => v.Name).ToArray();
        var stats = toScan.ToDictionary(v => v.Name, _ => new Stats(), StringComparer.Ordinal);
        file.Scan(toScan, (v, value) => stats[v.Name].Add(value));

        VariableCheck Check(string name)
        {
            var v = file.Find(name);
            if (v is null)
                return new VariableCheck { Name = name, Present = false };
            var s = stats[v.Name];
            return new VariableCheck
            {
                Name = v.Name,
                Present = true,
                Type = v.TypeName,
                Count = v.Count,
                Unit = v.Unit,
                Description = v.Description,
                Samples = s.Samples,
                NonZeroSamples = s.NonZero,
                Min = s.Min,
                Max = s.Max,
            };
        }

        return new IbtInspection
        {
            File = fullPath,
            FileBytes = new FileInfo(fullPath).Length,
            Header = file.Header,
            Session = session,
            Checklist = ChecklistVariables.Select(Check).ToArray(),
            SubTickVariables = subTick.Select(v => Check(v.Name)).ToArray(),
            TotalVariables = file.Variables.Count,
        };
    }

    sealed class Stats
    {
        public long Samples;
        public long NonZero;
        public double? Min;
        public double? Max;

        public void Add(double value)
        {
            Samples++;
            if (double.IsNaN(value))
                return;
            if (value != 0)
                NonZero++;
            if (double.IsInfinity(value))
                return;
            Min = Min is { } min ? Math.Min(min, value) : value;
            Max = Max is { } max ? Math.Max(max, value) : value;
        }
    }
}
