using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crateside.Core.Ledger;

/// <summary>
/// keytag's <c>results/runs.jsonl</c> read as ledger rows, for the import that repeats at each
/// read until the judge writes to crateside itself (#111).
/// </summary>
internal static class KeytagImport
{
    /// <summary>
    /// Lines left out of the import. The 2026-08-01 "segment reanalysis" of A Horny Money World was
    /// re-recorded unchanged under the song's own identity on 2026-08-12.
    /// </summary>
    private static readonly HashSet<string> Skipped = ["2026-08-01T21:36:01.800560+00:00"];

    /// <summary>Every readable line, in file order; a malformed one is skipped, as it may be caught mid-write.</summary>
    public static IEnumerable<KeytagLine> Read(string path)
    {
        if (!File.Exists(path))
            yield break;

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } text)
        {
            if (text.AsSpan().IsWhiteSpace())
                continue;

            KeytagLine? line;

            try
            {
                line = JsonSerializer.Deserialize(text, KeytagJsonContext.Default.KeytagLine);
            }
            catch (JsonException)
            {
                continue;
            }

            if (line is { Timestamp: { } timestamp } && !Skipped.Contains(timestamp))
                yield return line;
        }
    }

    /// <summary>A run line as a run, with <c>key</c> and <c>mode</c> becoming a tonal centre.</summary>
    public static RunRecord ToRun(KeytagLine line) =>
        new()
        {
            RecordedAt = DateTimeOffset.Parse(line.Timestamp!),
            Song = line.Song ?? string.Empty,
            Artist = line.Artist,
            Title = line.Title,
            SourceUrl = line.SourceUrl,
            LocalSource = line.SourceUrl is null,
            InputPath = line.InputPath,
            InputKind = line.InputKind,
            HasVocals = line.HasVocals,
            Confidence = LedgerRun.ParseConfidence(line.Verdict?.Confidence),
            Centre = TonalCentre.FromKey(line.Verdict?.Key) is { } plain
                ? plain with
                {
                    Mode = LedgerVocabulary.ParseMode(line.Verdict?.Mode) ?? plain.Mode,
                }
                : null,
            Sources =
            [
                .. (line.Sources ?? []).Select(source => new RunSource
                {
                    Url = source.Url,
                    Via = source.Via,
                    Kind = EvidenceSource.ParseKind(source.Kind) ?? SourceKind.Unknown,
                    States = source.States,
                    StatesKey = source.StatesKey,
                    StatesMode = LedgerVocabulary.ParseMode(source.StatesMode),
                    Note = source.Note,
                }),
            ],
            Windows =
                line.Machine is { ValueKind: JsonValueKind.Object } machine
                && machine.TryGetProperty("segment", out var segment)
                && segment is { ValueKind: JsonValueKind.Array }
                && segment.GetArrayLength() is 2
                    ? [new RunWindow(segment[0].GetDouble(), segment[1].GetDouble(), null, null)]
                    : [],
            TonicCheck = line.TonicCheck,
            Reasoning = line.Reasoning,
            Evidence = line.Machine is { ValueKind: JsonValueKind.Object } evidence
                ? evidence.GetRawText()
                : null,
            OldBpm = line.Bpm is { } bpm ? new OldBpm(bpm.Value, bpm.Source, bpm.Beatdetect) : null,
        };
}

internal sealed record KeytagLine
{
    /// <summary>Kept as written: it is the line's identity, and what a restatement points at.</summary>
    public string? Timestamp { get; init; }

    public string? Song { get; init; }
    public string? Artist { get; init; }
    public string? Title { get; init; }
    public string? SourceUrl { get; init; }
    public string? InputPath { get; init; }
    public string? InputKind { get; init; }
    public bool? HasVocals { get; init; }
    public VerdictDto? Verdict { get; init; }
    public IReadOnlyList<SourceDto>? Sources { get; init; }
    public string? TonicCheck { get; init; }
    public string? Reasoning { get; init; }
    public JsonElement? Machine { get; init; }
    public BpmDto? Bpm { get; init; }

    /// <summary>Set on a restatement, naming the run it copies (keytag ADR 0008).</summary>
    public string? Restates { get; init; }

    internal sealed record VerdictDto
    {
        public string? Key { get; init; }
        public string? Mode { get; init; }
        public string? Confidence { get; init; }
    }

    internal sealed record SourceDto
    {
        public string? Url { get; init; }
        public string? Via { get; init; }
        public string? Kind { get; init; }
        public string? States { get; init; }
        public string? StatesKey { get; init; }
        public string? StatesMode { get; init; }
        public string? Note { get; init; }
    }

    internal sealed record BpmDto
    {
        public double? Value { get; init; }
        public string? Source { get; init; }
        public double? Beatdetect { get; init; }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(KeytagLine))]
internal sealed partial class KeytagJsonContext : JsonSerializerContext { }
