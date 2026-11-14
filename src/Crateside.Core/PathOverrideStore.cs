using System.Text.Json.Serialization;

namespace Crateside.Core;

/// <summary>
/// The folders the user changed in Settings, kept in <c>%APPDATA%\crateside\paths.json</c> and
/// layered over <c>appsettings.json</c> at the next start.
/// </summary>
public sealed class PathOverrideStore(CratesidePaths paths)
{
    private readonly Lock _gate = new();

    private readonly string _path = paths.PathOverridesFile;

    internal PathOverrideStore(string path)
        : this(new CratesidePaths { PathOverridesFile = path }) { }

    /// <summary>Never throws: an unreadable file reads as nothing changed.</summary>
    public PathOverrides Read()
    {
        lock (_gate)
            return
                PathOverrideJsonContext.Default.PathOverridesFile.ReadFileOrDefault(_path)
                    is { Paths: { } paths }
                ? paths
                : new();
    }

    public void Remember(PathOverrides chosen)
    {
        lock (_gate)
            PathOverrideJsonContext.Default.PathOverridesFile.WriteFile(
                _path,
                new PathOverridesFile { Paths = chosen }
            );
    }
}

public sealed record PathOverridesFile
{
    public PathOverrides Paths { get; init; } = new();
}

public sealed record PathOverrides
{
    public string? StagingFolder { get; init; }

    public string? LibraryFolder { get; init; }

    public string? AbletonProjects { get; init; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(PathOverridesFile))]
internal sealed partial class PathOverrideJsonContext : JsonSerializerContext { }
