using System.Text.Json;

namespace Crateside.Core.Naming;

/// <summary>
/// The names StemForge has already been asked for, kept in <c>%APPDATA%\crateside\names.json</c>
/// apart from the user's own (ADR 0014).
/// </summary>
public sealed class ResolvedNameStore
{
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
                File.WriteAllText(
                    _path,
                    JsonSerializer.Serialize(
                        names,
                        NamingJsonContext.Default.DictionaryStringResolvedName
                    )
                );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
                ? JsonSerializer.Deserialize(
                    File.ReadAllText(_path),
                    NamingJsonContext.Default.DictionaryStringResolvedName
                ) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }
}
