using System.Text.Json;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Plans;

namespace Crateside.Core.Orchestration.Separation;

/// <summary>
/// Asks StemForge what it can make, once per process.
/// </summary>
public sealed class PresetCatalogReader(
    IProcessRunner processes,
    string stemforge = "stemforge-cli"
)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PresetCatalog? _cached;

    /// <summary>
    /// The catalog, or <see cref="PresetCatalog.Empty"/> — never cached, never thrown — when the
    /// separator could not be asked.
    /// </summary>
    public async Task<PresetCatalog> ReadAsync(CancellationToken ct = default)
    {
        if (_cached is { } hit)
            return hit;

        await _gate.WaitAsync(ct);

        try
        {
            if (_cached is { } waited)
                return waited;

            var read = await RunAsync(ct);

            if (!read.IsEmpty)
                _cached = read;

            return read;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PresetCatalog> RunAsync(CancellationToken ct)
    {
        ProcessOutcome outcome;

        try
        {
            outcome = await processes.RunAsync(
                new ProcessRequest(stemforge, ["presets", "--json"]),
                ct
            );
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
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
                JsonSerializer.Deserialize(
                    stdout[start..],
                    StemForgeJsonContext.Default.ListSeparatorPreset
                )
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
