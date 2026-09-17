using System.Text.Json;
using Crateside.Core.Queue;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Asks StemForge what it can make, once per process.
/// </summary>
/// <remarks>
/// The catalog changes only when audio-separator is upgraded underneath StemForge, so a restart
/// is a fine way to pick that up.
/// </remarks>
public sealed class PresetCatalogReader(string stemforge = "stemforge-cli")
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PresetCatalog? _cached;

    /// <summary>
    /// The catalog, or <see cref="PresetCatalog.Empty"/> when the separator could not be asked.
    /// Never throws: not having StemForge on PATH is an answer, not a fault.
    /// </summary>
    /// <remarks>
    /// Only a non-empty read is cached: empty is how both failures read, and caching one would
    /// pin a first-run failure for the life of the process.
    /// </remarks>
    public async Task<PresetCatalog> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is { } hit)
            return hit;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_cached is { } waited)
                return waited;

            var read = await RunAsync(cancellationToken);

            if (!read.IsEmpty)
                _cached = read;

            return read;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PresetCatalog> RunAsync(CancellationToken cancellationToken)
    {
        ProcessOutcome outcome;

        try
        {
            outcome = await ExternalProcess.RunAsync(
                stemforge,
                ["presets", "--json"],
                workingDirectory: null,
                cancellationToken
            );
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
            return PresetCatalog.Empty;
        }

        // A non-zero exit is how "no presets found" is reported, and it prints an empty array —
        // so the JSON is read either way rather than trusted to the exit code.
        return Parse(outcome.StandardOutput);
    }

    internal static PresetCatalog Parse(string stdout)
    {
        // The command prints a JSON array; anything it logged before it is not ours to interpret.
        var start = stdout.IndexOf('[');

        if (start < 0)
            return PresetCatalog.Empty;

        try
        {
            return
                JsonSerializer.Deserialize<List<SeparatorPreset>>(stdout[start..])
                    is { Count: > 0 } presets
                ? new PresetCatalog { Presets = presets }
                : PresetCatalog.Empty;
        }
        catch (JsonException)
        {
            return PresetCatalog.Empty;
        }
    }
}
