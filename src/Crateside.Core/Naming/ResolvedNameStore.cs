using System.Text.Json;

namespace Crateside.Core.Naming;

/// <summary>
/// The names StemForge has already been asked for, kept between runs.
/// </summary>
/// <remarks>
/// Stored in <c>%APPDATA%\crateside\names.json</c>, not in <c>queue.json</c>: a stated name is
/// the user's and a resolved one is a fact about an upload (ADR 0014).
/// </remarks>
public sealed class ResolvedNameStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly Lock _gate = new();

    private Dictionary<string, ResolvedName>? _cached;

    public ResolvedNameStore(string? path = null) =>
        _path =
            path
            ?? Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "names.json");

    /// <summary>Never throws — an unreadable or corrupt file reads as nothing resolved yet.</summary>
    public IReadOnlyDictionary<string, ResolvedName> Read()
    {
        lock (_gate)
            return _cached ??= TryReadFile();
    }

    public void Save(string videoId, ResolvedName name)
    {
        lock (_gate)
        {
            var names = _cached ??= TryReadFile();

            names[videoId] = name;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(names, SerializerOptions));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The name is still in memory, and re-resolving costs one metadata fetch.
            }
        }
    }

    private Dictionary<string, ResolvedName> TryReadFile()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, ResolvedName>>(
                    File.ReadAllText(_path),
                    SerializerOptions
                ) ?? []
                : [];
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return [];
        }
    }
}
