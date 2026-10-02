using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using AbleKit.Analysis;

namespace Crateside.Core.Warps;

/// <summary>
/// Every transient list crateside wrote into a sidecar, by fingerprint, so it is never taken for
/// Live's, even after Live saves over the warp or the file is moved or renamed (ADR 0047).
/// </summary>
public sealed class TransientOrigins(string? path = null)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<TransientOrigin>> _entries = Load(path);

    public static string DefaultPath { get; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "transients.json");

    /// <summary>Where crateside got these transients, or null if it never wrote them: Live's own.</summary>
    /// <param name="audioPath">The sample whose sidecar holds them; a copy of Live's counts only on the file it was written to.</param>
    public TransientOrigin? Of(IReadOnlyList<Transient> transients, string audioPath)
    {
        if (transients is [])
            return null;

        var name = Path.GetFileName(audioPath);

        lock (_lock)
        {
            return _entries.TryGetValue(Fingerprint(transients), out var origins)
                ? origins.FirstOrDefault(origin =>
                    origin.Detector is not null
                    || string.Equals(origin.Audio, name, StringComparison.OrdinalIgnoreCase)
                )
                : null;
        }
    }

    public void RecordDetected(
        IReadOnlyList<Transient> transients,
        string audioPath,
        string detector
    ) => Record(transients, new TransientOrigin(Path.GetFileName(audioPath), detector, null));

    public void RecordCopied(IReadOnlyList<Transient> transients, string audioPath, string from) =>
        Record(
            transients,
            new TransientOrigin(Path.GetFileName(audioPath), null, Path.GetFileName(from))
        );

    private void Record(IReadOnlyList<Transient> transients, TransientOrigin origin)
    {
        if (transients is [])
            return;

        lock (_lock)
        {
            var fingerprint = Fingerprint(transients);

            if (!_entries.TryGetValue(fingerprint, out var origins))
                _entries[fingerprint] = origins = [];

            if (origins.Contains(origin))
                return;

            origins.Add(origin);

            if (path is not null)
                TransientOriginsJsonContext.Default.DictionaryStringListTransientOrigin.WriteFile(
                    path,
                    _entries
                );
        }
    }

    /// <summary>A hash of the positions alone, which Live keeps as written when it saves over a warp.</summary>
    private static string Fingerprint(IReadOnlyList<Transient> transients)
    {
        var bytes = new byte[transients.Count * sizeof(int)];

        for (var i = 0; i < transients.Count; i++)
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(i * sizeof(int)),
                transients[i].Position
            );

        return Convert.ToHexString(SHA256.HashData(bytes), 0, 16);
    }

    private static Dictionary<string, List<TransientOrigin>> Load(string? path) =>
        path is not null
        && TransientOriginsJsonContext.Default.DictionaryStringListTransientOrigin.ReadFileOrDefault(
            path
        )
            is { } entries
            ? entries
            : [];
}

/// <param name="Audio">The file it was written beside, by name.</param>
/// <param name="Detector">crateside's detector and its version, such as <c>onsets 1</c>; null for a copy of Live's.</param>
/// <param name="CopiedFrom">The file whose Live-detected transients were copied, by name; null when detected.</param>
public sealed record TransientOrigin(string Audio, string? Detector, string? CopiedFrom);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(Dictionary<string, List<TransientOrigin>>))]
internal sealed partial class TransientOriginsJsonContext : JsonSerializerContext { }
