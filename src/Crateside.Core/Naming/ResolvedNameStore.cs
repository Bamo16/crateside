namespace Crateside.Core.Naming;

/// <summary>
/// The names StemForge has already been asked for, kept in <c>%APPDATA%\crateside\names.json</c>
/// apart from the user's own (ADR 0014).
/// </summary>
public sealed class ResolvedNameStore(string? path = null)
{
    private readonly string _path =
        path ?? Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "names.json");
    private readonly Lock _gate = new();

    private Dictionary<string, ResolvedName>? _cached;

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
                NamingJsonContext.Default.DictionaryStringResolvedName.WriteFile(_path, names);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The name is still in memory, and re-resolving costs one metadata fetch.
            }
        }
    }

    private Dictionary<string, ResolvedName> TryReadFile() =>
        NamingJsonContext.Default.DictionaryStringResolvedName.ReadFileOrDefault(_path) ?? [];
}
