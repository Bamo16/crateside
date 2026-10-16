namespace Crateside.Core.Ledger;

/// <summary>keytag's contract for a run, carried over: what is repaired on the way in, and what is refused.</summary>
internal static class RunValidation
{
    private static readonly HashSet<string> ChordRecogniserHosts = ["chordify.net", "chordu.com"];

    private static readonly HashSet<string> InputKinds = ["full_mix", "instrumental", "vocal"];

    /// <summary>Text repaired, links split from their provenance, and each source's key in the ledger's spelling.</summary>
    public static RunRecord Normalize(RunRecord run) =>
        run with
        {
            Song = LedgerVocabulary.RepairText(run.Song.Trim()),
            Artist = Repaired(run.Artist),
            Title = Repaired(run.Title),
            SourceUrl = run.SourceUrl?.Trim() is { Length: > 0 } url ? url : null,
            Sections =
            [
                .. run.Sections.Select(section => section with { Label = section.Label.Trim() }),
            ],
            Sources = [.. run.Sources.Select(Normalize)],
            Direction = Blank(run.Direction),
            TonicCheck = Blank(run.TonicCheck),
            Reasoning = Blank(run.Reasoning),
        };

    /// <summary>Why the run cannot be stored, or null. Refused only where a wrong value is worse than no run.</summary>
    public static RecordOutcome.Refused? Refusal(RunRecord run)
    {
        if (run.Song.Length is 0)
            return new("song", "a song identity is required");

        if (Damaged(run) is { } field)
            return new(
                field,
                "this arrived mis-encoded and could not be repaired; write the file as UTF-8"
            );

        if (run.Confidence is KeyConfidence.Unknown)
            return new(
                "confidence",
                $"expected {string.Join(" | ", LedgerVocabulary.Confidences)}"
            );

        if (run is { Confidence: not KeyConfidence.Withheld, Centre: null })
            return new("centre", "a tonal centre is required unless the verdict is WITHHELD");

        if (run is { Centre: { } centre } && !TonalCentre.Tonics.Contains(centre.Tonic))
            return new("centre.tonic", $"{centre.Tonic} is not a tonic in the ledger's spelling");

        if (run is { SourceUrl: null, LocalSource: false })
            return new(
                "source_url",
                "the URL this run's audio came from; say local_source for a file with none"
            );

        if (run is { SourceUrl: { } url } && !IsHttp(url))
            return new("source_url", $"{url} is not an http(s) URL");

        if (run is { InputKind: { } kind } && !InputKinds.Contains(kind))
            return new("input_kind", $"expected {string.Join(" | ", InputKinds)}");

        foreach (var section in run.Sections)
        {
            if (section.Label.Length is 0)
                return new("sections", "every section needs a label");

            if (!TonalCentre.Tonics.Contains(section.Centre.Tonic))
                return new(
                    "sections",
                    $"{section.Label}: {section.Centre.Tonic} is not a tonic in the ledger's spelling"
                );
        }

        if (run.Sources.Any(source => source.Kind is SourceKind.Unknown or SourceKind.Unclassified))
            return new(
                "sources.kind",
                "expected authoritative | corroborating | algorithmic | chord_recogniser | ear"
            );

        foreach (var window in run.Windows)
        {
            if (window.Start < 0 || window.End <= window.Start)
                return new(
                    "windows",
                    $"{window.Start}–{window.End} s is not a stretch of the track"
                );
        }

        return null;
    }

    public static bool IsHttp(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var link) && link.Scheme is "http" or "https";

    private static RunSource Normalize(RunSource source)
    {
        var (url, via) = LedgerVocabulary.SplitUrl(source.Url);

        return source with
        {
            Url = url,
            Via = Blank(source.Via) ?? via,
            // The hard rule beats anything written about the source: a chord recogniser's key
            // may never support a verdict.
            Kind =
                Host(url) is { } host && ChordRecogniserHosts.Contains(host)
                    ? SourceKind.ChordRecogniser
                    : source.Kind,
            StatesKey =
                LedgerVocabulary.NormalizeKey(source.StatesKey)
                ?? LedgerVocabulary.NormalizeKey(source.States)
                ?? LedgerVocabulary.CollapseModalStatement(source.States),
            StatesMode = source.StatesMode ?? ModeStated(source.States),
            Note = Blank(source.Note),
            Sections = source.Sections is { } sections
                ? [.. sections.Select(label => label.Trim()).Where(label => label.Length > 0)]
                : null,
        };
    }

    /// <summary>A mode the prose names with its tonic (<c>E Mixolydian</c>); a bare word is not enough.</summary>
    private static DiatonicMode? ModeStated(string? states) =>
        LedgerVocabulary.CollapseModalStatement(states) is null
            ? null
            : LedgerVocabulary.ParseMode(states);

    private static string? Damaged(RunRecord run) =>
        new (string Field, string? Text)[]
        {
            ("song", run.Song),
            ("artist", run.Artist),
            ("title", run.Title),
        }
            .FirstOrDefault(name => name.Text is { } text && LedgerVocabulary.IsDamaged(text))
            .Field;

    private static string? Host(string? url) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out var link)
            ? link.Host.ToLowerInvariant().Replace("www.", string.Empty)
            : null;

    private static string? Repaired(string? text) =>
        Blank(text) is { } kept ? LedgerVocabulary.RepairText(kept) : null;

    private static string? Blank(string? text) =>
        text?.Trim() is { Length: > 0 } kept ? kept : null;
}
