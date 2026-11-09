using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace CoCoach.Telemetry.Session;

/// <summary>
/// Extracts <see cref="SessionFacts"/> from iRacing session-info YAML.
/// Call it off the telemetry thread; it is pure and safe to call from any thread.
/// </summary>
/// <remarks>
/// iRacing writes driver and team names unquoted, so a name containing YAML syntax can break a
/// strict parser. When parsing fails, the free-text name fields are quoted and parsing is retried.
/// The names themselves are never read into the result.
/// </remarks>
public static partial class SessionInfoParser
{
    public static SessionFacts Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var root = TryLoad(yaml) ?? TryLoad(QuoteFreeTextFields(yaml));
        if (root is null)
            return new SessionFacts();

        var weekend = Child(root, "WeekendInfo");
        var driverInfo = Child(root, "DriverInfo");
        var playerIdx = ParseInt(Scalar(driverInfo, "DriverCarIdx"));
        var playerCar = FindDriverByCarIdx(driverInfo, playerIdx);

        return new SessionFacts
        {
            TrackDisplayName = Scalar(weekend, "TrackDisplayName"),
            TrackConfigName = Scalar(weekend, "TrackConfigName"),
            TrackName = Scalar(weekend, "TrackName"),
            TrackId = ParseInt(Scalar(weekend, "TrackID")),
            TrackLength = Scalar(weekend, "TrackLength"),
            TrackCity = Scalar(weekend, "TrackCity"),
            TrackCountry = Scalar(weekend, "TrackCountry"),
            EventType = Scalar(weekend, "EventType"),
            Official = ParseInt(Scalar(weekend, "Official")) is { } official ? official != 0 : null,
            SessionTypes = SessionTypes(root),
            PlayerCarIdx = playerIdx,
            PlayerCarScreenName = Scalar(playerCar, "CarScreenName"),
            PlayerCarPath = Scalar(playerCar, "CarPath"),
            PlayerCarId = ParseInt(Scalar(playerCar, "CarID")),
            PlayerCarClassShortName = Scalar(playerCar, "CarClassShortName"),
        };
    }

    static YamlMappingNode? TryLoad(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            return stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (YamlException)
        {
            return null;
        }
    }

    // wrap free-text fields in single quotes (doubling any embedded quote) when they are not quoted already
    internal static string QuoteFreeTextFields(string yaml) =>
        FreeTextFieldRegex().Replace(yaml, match =>
        {
            var value = match.Groups["value"].Value.TrimEnd('\r');
            var trailingCr = match.Groups["value"].Value.EndsWith('\r') ? "\r" : string.Empty;
            if (value.Length >= 2 &&
                ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
                return match.Value;

            return $"{match.Groups["key"].Value} '{value.Replace("'", "''", StringComparison.Ordinal)}'{trailingCr}";
        });

    [GeneratedRegex(@"^(?<key>\s*(?:AbbrevName|TeamName|UserName|Initials|DriverSetupName|CameraName|CarDesignStr|HelmetDesignStr|SuitDesignStr|CarNumberDesignStr|ClubName|DivisionName|CarSponsor_1|CarSponsor_2):)[ \t]+(?<value>\S.*)$", RegexOptions.Multiline)]
    private static partial Regex FreeTextFieldRegex();

    static YamlMappingNode? FindDriverByCarIdx(YamlMappingNode? driverInfo, int? carIdx)
    {
        if (carIdx is null || driverInfo is null)
            return null;
        if (!driverInfo.Children.TryGetValue(new YamlScalarNode("Drivers"), out var drivers) || drivers is not YamlSequenceNode list)
            return null;

        foreach (var entry in list.Children.OfType<YamlMappingNode>())
        {
            if (ParseInt(Scalar(entry, "CarIdx")) == carIdx)
                return entry;
        }
        return null;
    }

    static string[] SessionTypes(YamlMappingNode root)
    {
        var sessionInfo = Child(root, "SessionInfo");
        if (sessionInfo is null ||
            !sessionInfo.Children.TryGetValue(new YamlScalarNode("Sessions"), out var sessions) ||
            sessions is not YamlSequenceNode list)
            return [];

        return list.Children
            .OfType<YamlMappingNode>()
            .Select(s => Scalar(s, "SessionType"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToArray();
    }

    static YamlMappingNode? Child(YamlMappingNode? node, string key) =>
        node is not null && node.Children.TryGetValue(new YamlScalarNode(key), out var child) ? child as YamlMappingNode : null;

    static string? Scalar(YamlMappingNode? node, string key)
    {
        if (node is null || !node.Children.TryGetValue(new YamlScalarNode(key), out var child) || child is not YamlScalarNode scalar)
            return null;
        var value = scalar.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
}
