using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>What a judging session writes, and the only thing it writes (docs/judge.md).</summary>
public sealed record VerdictFile
{
    /// <summary>The most windows one verdict may ask to have detected.</summary>
    public const int MaxWindows = 4;

    public string? Artist { get; init; }
    public string? Title { get; init; }
    public string? Confidence { get; init; }

    public CenterDto? Center { get; init; }
    public bool Mixed { get; init; }
    public IReadOnlyList<SectionDto>? Sections { get; init; }
    public IReadOnlyList<SourceDto>? Sources { get; init; }
    public string? TonicCheck { get; init; }
    public string? Reasoning { get; init; }

    public IReadOnlyList<CandidateDto>? Candidates { get; init; }

    /// <summary>Stretches to detect over before judging again; only with WITHHELD.</summary>
    public IReadOnlyList<WindowDto>? Windows { get; init; }

    /// <summary>The file at <paramref name="path"/> as a run, or what is wrong with it, in words the session can act on.</summary>
    public static VerdictCheck Check(string path)
    {
        VerdictFile? file;

        try
        {
            file = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                VerdictFileJsonContext.Default.VerdictFile
            );
        }
        catch (FileNotFoundException)
        {
            return new VerdictCheck.Unusable($"no verdict file was written at {path}");
        }
        catch (JsonException ex)
        {
            return new VerdictCheck.Unusable($"the verdict file is not valid JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new VerdictCheck.Unusable($"the verdict file could not be read: {ex.Message}");
        }

        return file is null ? new VerdictCheck.Unusable("the verdict file is empty") : file.Check();
    }

    private VerdictCheck Check()
    {
        if (string.IsNullOrWhiteSpace(Artist) || string.IsNullOrWhiteSpace(Title))
            return new VerdictCheck.Unusable("artist and title are both required");

        var confidence = LedgerRun.ParseConfidence(Confidence);

        if (confidence is KeyConfidence.Unknown)
            return new VerdictCheck.Unusable(
                $"confidence must be one of {string.Join(", ", LedgerVocabulary.Confidences)}"
            );

        TonalCenter? center = null;

        if (Center is { } given && !TryRead(given.Tonic, given.Mode, out center, out var problem))
            return new VerdictCheck.Unusable($"center: {problem}");

        List<Section> sections = [];

        foreach (var section in Sections ?? [])
        {
            if (
                !TryRead(section.Tonic, section.Mode, out var sectionCenter, out var sectionProblem)
            )
                return new VerdictCheck.Unusable($"sections, {section.Label}: {sectionProblem}");

            sections.Add(new(section.Label ?? string.Empty, sectionCenter, section.Start));
        }

        List<RunSource> sources = [];

        foreach (var source in Sources ?? [])
        {
            if (EvidenceSource.ParseKind(source.Kind) is not { } kind)
                return new VerdictCheck.Unusable(
                    $"sources: kind \"{source.Kind}\" is not one of authoritative, corroborating, "
                        + "chord_recognizer, algorithmic, ear"
                );

            sources.Add(
                new()
                {
                    Url = source.Url,
                    Kind = kind,
                    States = source.States,
                    StatesKey = source.StatesKey,
                    StatesMode = LedgerVocabulary.ParseMode(source.StatesMode),
                    Note = source.Note,
                    Sections = source.Sections,
                }
            );
        }

        var run = new RunRecord
        {
            RecordedAt = DateTimeOffset.MinValue,
            Song = LedgerVocabulary.SongIdentity(Artist, Title),
            Artist = Artist.Trim(),
            Title = LedgerVocabulary.WithoutStemSuffix(Title.Trim()),
            Confidence = confidence,
            Center = center,
            Mixed = Mixed,
            Sections = sections,
            Sources = sources,
            TonicCheck = TonicCheck,
            Reasoning = Reasoning,
            Candidates = [.. (Candidates ?? []).Select(CandidateOf).OfType<RunCandidate>()],
        };

        if (Windows is not { Count: > 0 } windows)
            return new VerdictCheck.Recordable(run);

        if (confidence is not KeyConfidence.Withheld)
            return new VerdictCheck.Unusable(
                "windows are asked for only with WITHHELD; drop them or withhold"
            );

        if (windows.Count > MaxWindows)
            return new VerdictCheck.Unusable($"ask for at most {MaxWindows} windows");

        var asked = windows
            .Select(window => new RunWindow(
                window.Start,
                window.End,
                window.Label,
                WindowAsker.Judge
            )
            {
                Why = window.Why?.Trim() is { Length: > 0 } why ? why : null,
            })
            .ToList();

        return
            asked.FirstOrDefault(window => window.Start < 0 || window.End <= window.Start)
                is { } bad
            ? new VerdictCheck.Unusable(
                $"windows: {bad.Start}–{bad.End} s is not a stretch of the track"
            )
            : new VerdictCheck.WindowsAsked(asked, run);
    }

    private static RunCandidate? CandidateOf(CandidateDto candidate) =>
        candidate is { Key: { Length: > 0 } key, Why: { Length: > 0 } why }
            ? new(key.Trim(), why.Trim())
            : null;

    private static bool TryRead(
        string? tonic,
        string? mode,
        out TonalCenter center,
        out string problem
    )
    {
        center = null!;
        problem = string.Empty;

        if (!LedgerVocabulary.TryNormalizeTonic(tonic, out var sharp))
        {
            problem = $"\"{tonic}\" is not a tonic (C, C#, D … B)";

            return false;
        }

        if (LedgerVocabulary.ParseMode(mode) is not { } parsed)
        {
            problem =
                $"\"{mode}\" is not one of ionian, dorian, phrygian, lydian, mixolydian, aeolian, locrian";

            return false;
        }

        center = new(sharp, parsed);

        return true;
    }

    public sealed record CenterDto(string? Tonic, string? Mode);

    public sealed record SectionDto(
        string? Label,
        string? Tonic,
        string? Mode,
        double? Start = null
    );

    public sealed record SourceDto
    {
        public string? Url { get; init; }
        public string? Kind { get; init; }
        public string? States { get; init; }
        public string? StatesKey { get; init; }
        public string? StatesMode { get; init; }
        public IReadOnlyList<string>? Sections { get; init; }
        public string? Note { get; init; }
    }

    public sealed record WindowDto(double Start, double End, string? Label, string? Why = null);

    public sealed record CandidateDto(string? Key, string? Why);
}

/// <summary>A verdict file read against the ledger's rules, before crateside fills in what the session cannot know.</summary>
public abstract record VerdictCheck
{
    private VerdictCheck() { }

    /// <summary>A run to record; <paramref name="Note"/> is crateside's own paragraph for its reasoning, outside the judge's budget.</summary>
    public sealed record Recordable(RunRecord Run, string? Note = null) : VerdictCheck;

    /// <summary>A withheld verdict that asks for windows; <see cref="Withheld"/> is recorded if nothing better follows.</summary>
    public sealed record WindowsAsked(IReadOnlyList<RunWindow> Windows, RunRecord Withheld)
        : VerdictCheck;

    public sealed record Unusable(string Problem) : VerdictCheck;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
)]
[JsonSerializable(typeof(VerdictFile))]
internal sealed partial class VerdictFileJsonContext : JsonSerializerContext { }
