namespace CoCoach.Tests.Support;

/// <summary>A fresh temporary directory, deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cocoach-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}

/// <summary>Locates real .ibt recordings for the optional real-data tests.</summary>
internal static class RecordingCorpus
{
    public const string EnvironmentVariable = "COCOACH_IBT_DIR";

    /// <summary><c>COCOACH_IBT_DIR</c>, or <c>samples/ibt/</c> under the repository root.</summary>
    public static string Directory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
                return System.IO.Path.GetFullPath(configured);
            return System.IO.Path.Combine(RepositoryRoot(), "samples", "ibt");
        }
    }

    public static string[] Files()
    {
        var dir = Directory;
        return System.IO.Directory.Exists(dir)
            ? System.IO.Directory.GetFiles(dir, "*.ibt", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray()
            : [];
    }

    static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "cocoach.slnx")))
                return dir.FullName;
        }
        return System.IO.Directory.GetCurrentDirectory();
    }
}
