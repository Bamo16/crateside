using AbleKit.Tags;
using Crateside.Core.Genres;

namespace Crateside.Core.Ableton;

/// <summary>crateside's reading of the keywords Live holds for a file.</summary>
public static class FileTagsReading
{
    private const string KeyCategory = "Key";
    private const string TypeCategory = "Type";

    extension(FileTags tags)
    {
        /// <summary>
        /// The tonic and mode keywords recombined in the ledger's spelling (<c>G# major</c>), or null
        /// for no key or only half of one.
        /// </summary>
        public string? Key => ReadKey(tags.Keywords).Key;

        /// <summary>True when the file has a tonic but no mode, or a mode but no tonic.</summary>
        public bool HasPartialKey => ReadKey(tags.Keywords).IsPartial;

        /// <summary>Stricter than "has an entry": a file tagged only <c>Type|Vocal</c> has no key.</summary>
        public bool IsKeyTagged => tags.Key is not null;

        /// <summary>The <c>Type|</c> keyword: Instrumental, Vocal, Loop.</summary>
        public string? Type =>
            Values(tags.Keywords, TypeCategory).FirstOrDefault(value => !value.Contains('|'));

        /// <summary>The <c>Genres|</c> keywords, in the order the entry holds them (ADR 0088).</summary>
        public IReadOnlyList<string> Genres => [.. Values(tags.Keywords, GenreVocabulary.Group)];
    }

    /// <summary>Live spells the tonic with ♯/♭ (<c>Key|G♯</c>); the ledger with ASCII.</summary>
    private static (string? Key, bool IsPartial) ReadKey(IReadOnlyList<string> keywords)
    {
        string? tonic = null;
        string? mode = null;

        foreach (var value in Values(keywords, KeyCategory))
        {
            if (ModeTags.IsModal(value))
                continue;

            if (IsMode(value))
                mode ??= value.ToLowerInvariant();
            else
                tonic ??= value.Replace('♯', '#').Replace('♭', 'b');
        }

        return (tonic, mode) switch
        {
            ({ } t, { } m) => ($"{t} {m}", false),
            (null, null) => (null, false),
            _ => (null, true), // half a key is not a key
        };
    }

    private static bool IsMode(string value) =>
        value.Equals("Major", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Minor", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Values(IReadOnlyList<string> keywords, string category)
    {
        var prefix = $"{category}|";

        return keywords
            .Where(keyword => keyword.StartsWith(prefix, StringComparison.Ordinal))
            .Select(keyword => keyword[prefix.Length..])
            .Where(value => value.Length > 0);
    }
}
