using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration.Handoff;

/// <summary>
/// Library files the user approved re-keying to their verdict, kept in
/// <c>%APPDATA%\crateside\key-corrections.json</c> until the backing pass writes them (ADR 0052).
/// </summary>
public sealed class KeyCorrections(string? path = null)
{
    private readonly string _path =
        path
        ?? Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "crateside",
            "key-corrections.json"
        );
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _approved = new(0);

    /// <summary>What the last backing pass found, or empty before it has run.</summary>
    public IReadOnlyList<KeyDisagreement> Disagreements { get; private set; } = [];

    /// <summary>Each approved path and the verdict the user saw for it.</summary>
    public IReadOnlyDictionary<string, string> Approved()
    {
        lock (_gate)
            return Read();
    }

    /// <summary>Approves these files, and wakes the backing pass to write them.</summary>
    public void Approve(IEnumerable<KeyDisagreement> files)
    {
        lock (_gate)
        {
            var approved = new Dictionary<string, string>(Read(), StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
                approved[file.Path] = file.Verdict;

            KeyCorrectionsJsonContext.Default.DictionaryStringString.WriteFile(_path, approved);
        }

        _approved.Release();
    }

    /// <summary>
    /// Takes the backing pass's latest finding, and forgets approvals it no longer contains: written,
    /// or overtaken by a new verdict.
    /// </summary>
    public void Found(IReadOnlyList<KeyDisagreement> disagreements)
    {
        Disagreements = disagreements;

        lock (_gate)
        {
            var approved = Read();
            var kept = approved
                .Where(pair =>
                    disagreements.Any(file =>
                        file.Path.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)
                        && file.Verdict.Equals(pair.Value, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToDictionary(StringComparer.OrdinalIgnoreCase);

            if (kept.Count != approved.Count)
                KeyCorrectionsJsonContext.Default.DictionaryStringString.WriteFile(_path, kept);
        }
    }

    /// <summary>Waits out the delay, or less if the user approves something meanwhile.</summary>
    public Task<bool> WaitAsync(TimeSpan delay, CancellationToken ct) =>
        _approved.WaitAsync(delay, ct);

    private Dictionary<string, string> Read() =>
        new(
            KeyCorrectionsJsonContext.Default.DictionaryStringString.ReadFileOrDefault(_path) ?? [],
            StringComparer.OrdinalIgnoreCase
        );
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class KeyCorrectionsJsonContext : JsonSerializerContext;
