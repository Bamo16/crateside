using System.Text.Json.Serialization;
using Crateside.Core.Ledger;

namespace Crateside.Core.Ableton;

/// <summary>
/// Which Live Collection each meaning is written to, kept in <c>%APPDATA%\crateside\collection-settings.json</c>.
/// The first read decides once: an install with ledger history keeps the numbers crateside used
/// before this was a choice, a fresh one writes no Collections (#166).
/// </summary>
public sealed class CollectionSettingsStore(CratesidePaths paths, LedgerStore ledger)
{
    private readonly string _path = paths.CollectionSettingsFile;

    private readonly Lock _gate = new();

    internal CollectionSettingsStore(string path, LedgerStore ledger)
        : this(new CratesidePaths { CollectionSettingsFile = path }, ledger) { }

    /// <summary>Never throws: an unreadable file reads as every meaning off.</summary>
    public CollectionSettings Read()
    {
        lock (_gate)
        {
            if (
                CollectionSettingsJsonContext.Default.CollectionSettings.ReadFileOrDefault(_path) is
                { } chosen
            )
                return chosen;

            if (File.Exists(_path))
                return new CollectionSettings();

            var first = ledger.Read().Runs.Count > 0 ? CollectionSettings.Maintainers : new();

            Write(first);

            return first;
        }
    }

    /// <summary>Throws when <see cref="CollectionSettings.Problem"/> is not null; check it first.</summary>
    public void Save(CollectionSettings settings)
    {
        if (settings.Problem() is { } problem)
            throw new ArgumentException(problem, nameof(settings));

        lock (_gate)
            Write(settings);
    }

    private void Write(CollectionSettings settings)
    {
        try
        {
            CollectionSettingsJsonContext.Default.CollectionSettings.WriteFile(_path, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(CollectionSettings))]
internal sealed partial class CollectionSettingsJsonContext : JsonSerializerContext { }
