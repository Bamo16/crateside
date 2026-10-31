using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Marks;
using Crateside.Core.Naming;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Tracks;

/// <summary>A track's entry in each per-song store, by the key it is filed under: a video ID or its song (ADR 0071).</summary>
public sealed record FiledRecords
{
    public IReadOnlyDictionary<string, GenreRecord> Genres { get; init; } =
        new Dictionary<string, GenreRecord>();

    public IReadOnlyDictionary<string, FamiliarityRecord> Familiarity { get; init; } =
        new Dictionary<string, FamiliarityRecord>();

    public IReadOnlyDictionary<string, TrackMarkRecord> Marks { get; init; } =
        new Dictionary<string, TrackMarkRecord>();

    /// <summary>The names StemForge gave when asked, by video ID (ADR 0014).</summary>
    public IReadOnlyDictionary<string, ResolvedName> ResolvedNames { get; init; } =
        new Dictionary<string, ResolvedName>();

    /// <summary>The user's standing instructions, the name they gave included, by video ID.</summary>
    public IReadOnlyDictionary<string, TrackInstruction> Instructions { get; init; } =
        new Dictionary<string, TrackInstruction>();
}

/// <summary>Reads the per-song stores as they are on disk, never moving a record.</summary>
public sealed class FiledRecordsReader(
    GenreStore genres,
    FamiliarityStore familiarity,
    TrackMarkStore marks,
    ResolvedNameStore names,
    TrackInstructionStore instructions
)
{
    /// <summary>Every entry filed under any of <paramref name="keys"/>, under the key the store holds it by.</summary>
    public FiledRecords Read(IReadOnlyCollection<string> keys) =>
        new()
        {
            Genres = FiledUnder(genres.Read(), keys),
            Familiarity = FiledUnder(familiarity.Read(), keys),
            Marks = FiledUnder(marks.Read(), keys),
            ResolvedNames = FiledUnder(names.Read(), keys),
            Instructions = FiledUnder(
                instructions.Read().ToDictionary(instruction => instruction.VideoId),
                keys
            ),
        };

    private static Dictionary<string, T> FiledUnder<T>(
        IReadOnlyDictionary<string, T> store,
        IReadOnlyCollection<string> keys
    ) =>
        store
            .Where(entry => keys.Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(StringComparer.Ordinal);
}
