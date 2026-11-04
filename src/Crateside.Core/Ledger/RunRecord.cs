using Crateside.Core.Tempo;

namespace Crateside.Core.Ledger;

/// <summary>A run as it is written to the ledger, checked by <see cref="LedgerStore.Record"/> (docs/ledger.md).</summary>
public sealed record RunRecord
{
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>The song identity: normalized <c>artist - title</c>, lowercased. What a later run supersedes.</summary>
    public required string Song { get; init; }

    public string? Artist { get; init; }
    public string? Title { get; init; }

    /// <summary>Where the audio came from; required unless <see cref="LocalSource"/> says there is none.</summary>
    public string? SourceUrl { get; init; }

    public bool LocalSource { get; init; }

    public string? InputPath { get; init; }

    /// <summary><c>full_mix</c>, <c>instrumental</c> or <c>vocal</c>.</summary>
    public string? InputKind { get; init; }

    /// <summary>Null means nobody said, never "it has vocals".</summary>
    public bool? HasVocals { get; init; }

    public required KeyConfidence Confidence { get; init; }

    public DecidedBy DecidedBy { get; init; } = DecidedBy.Judge;

    /// <summary>The primary tonal center; null only when withheld.</summary>
    public TonalCenter? Center { get; init; }

    /// <summary>Two modes on one tonic; <see cref="Center"/> is then the harmony's.</summary>
    public bool Mixed { get; init; }

    public IReadOnlyList<Section> Sections { get; init; } = [];

    public IReadOnlyList<RunSource> Sources { get; init; } = [];

    /// <summary>The stretches the evidence was detected over, where it was not the whole track.</summary>
    public IReadOnlyList<RunWindow> Windows { get; init; } = [];

    /// <summary>The user's note to the judge, sent with the windows.</summary>
    public string? Direction { get; init; }

    public string? TonicCheck { get; init; }
    public string? Reasoning { get; init; }

    /// <summary>The keys the judge weighed besides the verdict's; for a WITHHELD, the ones still open.</summary>
    public IReadOnlyList<RunCandidate> Candidates { get; init; } = [];

    /// <summary>The reasoning of the pass that asked for windows, kept once for the run that followed it.</summary>
    public string? WithheldReasoning { get; init; }

    /// <summary>The detectors' report, as JSON.</summary>
    public string? Evidence { get; init; }

    /// <summary>What the lookup found on each site before the judge ran, cited or not, as JSON (#176).</summary>
    public string? Lookups { get; init; }

    /// <summary>The old BPM estimate, from imported history. Never written by a new run.</summary>
    public OldBpm? OldBpm { get; init; }
}

/// <summary>An external source consulted for a verdict, as written.</summary>
public sealed record RunSource
{
    public string? Url { get; init; }
    public string? Via { get; init; }
    public required SourceKind Kind { get; init; }

    /// <summary>Everything the source said about the key, verbatim.</summary>
    public string? States { get; init; }

    /// <summary>What it said as a 24-key name in the ledger's spelling, so it compares.</summary>
    public string? StatesKey { get; init; }

    public DiatonicMode? StatesMode { get; init; }

    public string? Note { get; init; }

    /// <summary>The labels of the sections it covered; null when it did not say.</summary>
    public IReadOnlyList<string>? Sections { get; init; }
}

/// <summary>A key the judge weighed besides the verdict's, and why in one sentence.</summary>
public sealed record RunCandidate(string Key, string Why);

/// <summary>A stretch of the track detected on its own, in seconds.</summary>
public sealed record RunWindow(double Start, double End, string? Label, WindowAsker? AskedBy)
{
    /// <summary>The bars the user gave, as Live counts them; null when given in time or asked for by the judge.</summary>
    public BarSpan? Bars { get; init; }

    /// <summary>What the judge wanted the window to settle, in one sentence.</summary>
    public string? Why { get; init; }

    /// <summary>What the window's section is called: its label, else its span.</summary>
    public string Name =>
        Label is { Length: > 0 } label ? label
        : Bars is { } bars ? $"bars {bars.First}–{bars.Last}"
        : $"{TempoFormat.Clock(Start)}–{TempoFormat.Clock(End)}";
}

/// <summary>Bars <see cref="First"/> to <see cref="Last"/>, both included.</summary>
public sealed record BarSpan(int First, int Last);

/// <summary>Bars as typed: a null side is open, to the start or end of the file.</summary>
public sealed record BarRange(int? First, int? Last);

/// <summary>Who made a verdict: an agent session, crateside's rules, or the person (ADR 0076).</summary>
public enum DecidedBy
{
    Judge,
    Rules,
    Person,
}

/// <summary>Who asked for a window. Null on a window imported from history, which never said.</summary>
public enum WindowAsker
{
    User,
    Judge,
}

/// <summary>The old BPM estimate: its value, where it came from, and beatdetect's reading beside it.</summary>
public sealed record OldBpm(double? Value, string? Source, double? Beatdetect);

/// <summary>Whether a run went into the ledger.</summary>
public abstract record RecordOutcome
{
    private RecordOutcome() { }

    public sealed record Recorded(long Id) : RecordOutcome;

    public sealed record Refused(string Field, string Problem) : RecordOutcome;
}

/// <summary>Whether a restatement went into the ledger.</summary>
public abstract record RestateOutcome
{
    private RestateOutcome() { }

    public sealed record Restated(string Song) : RestateOutcome;

    /// <summary>The song already had that upload: a skip, not a failure.</summary>
    public sealed record AlreadyHeld(string Song) : RestateOutcome;

    public sealed record Refused(string Problem) : RestateOutcome;

    /// <summary>Null when the ledger now has the upload, else why not.</summary>
    public string? Refusal => this is Refused(var problem) ? problem : null;
}
