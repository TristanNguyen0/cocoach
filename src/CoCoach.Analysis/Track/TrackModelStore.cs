using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoCoach.Analysis.Track;

/// <summary>Persists track models per layout (track id plus configuration).</summary>
/// <remarks>A file store for now; the SQLite store is a later slice and will implement the same interface.</remarks>
public interface ITrackModelStore
{
    /// <summary>The stored model for <paramref name="key"/>, or <see langword="null"/> when there is none.</summary>
    TrackModel? Load(TrackKey key);

    /// <summary>Stores <paramref name="model"/>, replacing any earlier model of the same layout.</summary>
    void Save(TrackModel model);
}

/// <summary>Stores each layout's model as one JSON file in a directory.</summary>
public sealed class JsonFileTrackModelStore : ITrackModelStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    public JsonFileTrackModelStore(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory = Path.GetFullPath(directory);
    }

    public string Directory { get; }

    public string PathFor(TrackKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Path.Combine(Directory, key.Slug + ".json");
    }

    public TrackModel? Load(TrackKey key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;
        using var stream = File.OpenRead(path);
        var model = JsonSerializer.Deserialize<TrackModel>(stream, Options);
        return model is not null && model.Key.SameLayout(key) ? model : null;
    }

    public void Save(TrackModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(model.Key);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, model, Options);
        File.Move(temp, path, overwrite: true);
    }
}
