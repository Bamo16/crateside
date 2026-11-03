using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Ledger;

namespace Crateside.Demo;

/// <summary>A verdict as crateside recorded it for a well-known song, kept in verdicts.json so the demo shows real evidence.</summary>
internal sealed record Verdict
{
    public required string Artist { get; init; }
    public required string Title { get; init; }
    public required KeyConfidence Confidence { get; init; }
    public string? Tonic { get; init; }
    public DiatonicMode? Mode { get; init; }
    public bool Mixed { get; init; }
    public string? Reasoning { get; init; }
    public string? TonicCheck { get; init; }
    public string? WithheldReasoning { get; init; }
    public IReadOnlyList<VerdictSection> Sections { get; init; } = [];
    public IReadOnlyList<VerdictSource> Sources { get; init; } = [];
    public IReadOnlyList<RunCandidate> Candidates { get; init; } = [];
    public required JsonElement Evidence { get; init; }

    public TonalCenter? Center => Tonic is { } tonic && Mode is { } mode ? new(tonic, mode) : null;

    public static IReadOnlyList<Verdict> All { get; } = Load();

    public static Verdict Of(string artist, string title) =>
        All.Single(verdict => verdict.Artist == artist && verdict.Title == title);

    private static List<Verdict> Load()
    {
        using var stream = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream("verdicts.json")!;

        return JsonSerializer.Deserialize(stream, VerdictJsonContext.Default.ListVerdict)!;
    }
}

internal sealed record VerdictSection(string Label, string Tonic, DiatonicMode Mode, double? Start);

internal sealed record VerdictSource
{
    public string? Url { get; init; }
    public string? Via { get; init; }
    public SourceKind Kind { get; init; }
    public string? States { get; init; }
    public string? StatesKey { get; init; }
    public DiatonicMode? StatesMode { get; init; }
    public string? Note { get; init; }
    public IReadOnlyList<string>? Sections { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(List<Verdict>))]
internal sealed partial class VerdictJsonContext : JsonSerializerContext { }
