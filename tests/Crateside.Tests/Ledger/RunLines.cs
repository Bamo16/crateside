using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core;
using Crateside.Core.Ledger;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Ledger;

/// <summary>Fixture ledgers written as JSONL, one run (or restatement) per line, read into a store.</summary>
internal static class RunLines
{
    public static void Load(LedgerStore store, FakeTimeProvider time, string path)
    {
        if (!File.Exists(path))
            return;

        Dictionary<string, string> videoIds = new(StringComparer.Ordinal);

        foreach (var line in Read(path))
        {
            if (line.Restates is { } restates)
            {
                if (videoIds.TryGetValue(restates, out var videoId) && line.SourceUrl is { } url)
                {
                    time.SetUtcNow(DateTimeOffset.Parse(line.Timestamp!));
                    store.Restate(videoId, url);
                }

                continue;
            }

            var run = ToRun(line);

            if (
                store.Record(run) is RecordOutcome.Recorded
                && run.SourceUrl is { } source
                && YouTubeUrl.TryGetVideoId(source, out var id)
            )
                videoIds[line.Timestamp!] = id;
        }
    }

    /// <summary>Every readable line, in file order.</summary>
    public static IEnumerable<RunLine> Read(string path)
    {
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

            RunLine? line;

            try
            {
                line = JsonSerializer.Deserialize(text, RunLineJsonContext.Default.RunLine);
            }
            catch (JsonException)
            {
                continue;
            }

            if (line is { Timestamp: not null })
                yield return line;
        }
    }

    /// <summary>A run line as a run, with <c>key</c> and <c>mode</c> becoming a tonal centre.</summary>
    public static RunRecord ToRun(RunLine line) =>
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

internal sealed record RunLine
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

    /// <summary>Set on a restatement, naming the run it copies.</summary>
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
[JsonSerializable(typeof(RunLine))]
internal sealed partial class RunLineJsonContext : JsonSerializerContext { }
