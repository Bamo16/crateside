using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Microsoft.Extensions.Options;

namespace Crateside.Core.WorkLists;

/// <summary>
/// <c>%APPDATA%\crateside\queue.json</c>, holding both the playlist settings and the track
/// instructions, so a save of either never drops the other. The API key is never here.
/// </summary>
public sealed class QueueFile(IOptions<PlaylistOptions> fallback, string? path = null)
{
    private readonly string _path =
        path ?? Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "queue.json");
    private readonly PlaylistOptions _fallback = fallback.Value;
    private readonly Lock _gate = new();

    private QueueDocument? _cached;

    /// <summary>When the file was last written as of <see cref="_cached"/>, or null for no file.</summary>
    private DateTime? _cachedStamp;

    /// <summary>
    /// What the user saved, falling back to configuration for a first run. Never throws: an
    /// unreadable file degrades to the fallback.
    /// </summary>
    internal QueueDocument Read()
    {
        lock (_gate)
            return Current();
    }

    /// <summary>
    /// Reads, applies, writes — the one path every change takes, so saving one setting cannot
    /// drop another.
    /// </summary>
    internal void Save(Func<QueueDocument, QueueDocument> change)
    {
        lock (_gate)
        {
            var document = change(Current());

            QueueFileJsonContext.Default.QueueDocument.WriteFile(_path, document);

            _cachedStamp = StampOf(_path);
            _cached = document;
        }
    }

    /// <summary>
    /// The document as the file has it now, re-read when its stamp shows an edit from outside.
    /// Callers hold <see cref="_gate"/>.
    /// </summary>
    private QueueDocument Current()
    {
        var stamp = StampOf(_path);

        if (_cached is { } cached && stamp == _cachedStamp)
            return cached;

        var stored = QueueFileJsonContext.Default.QueueDocument.ReadFileOrDefault(_path);

        _cachedStamp = stamp;

        return _cached = new QueueDocument
        {
            PlaylistId = stored?.PlaylistId ?? _fallback.PlaylistId,
            RefreshMinutes = stored?.RefreshMinutes,
            ConsumeOnArrival = stored?.ConsumeOnArrival is true,
            Tracks = [.. (stored?.Tracks ?? []).Select(Converted)],
        };
    }

    private static DateTime? StampOf(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;

    /// <summary>
    /// Turns a pre-<see cref="StemPlan"/> instruction into one, clearing the old field so this
    /// can go once no instruction carries it.
    /// </summary>
    private static TrackInstruction Converted(TrackInstruction stored) =>
        stored is { Plan: null, WasInstrumentalRelease: true }
            ? stored with
            {
                Plan = StemPlan.InstrumentalRelease,
                WasInstrumentalRelease = false,
            }
            : stored with
            {
                WasInstrumentalRelease = false,
            };
}

/// <summary>What <c>queue.json</c> holds.</summary>
internal sealed record QueueDocument
{
    public string? PlaylistId { get; init; }

    public int? RefreshMinutes { get; init; }

    public bool ConsumeOnArrival { get; init; }

    public IReadOnlyList<TrackInstruction> Tracks { get; init; } = [];
}
