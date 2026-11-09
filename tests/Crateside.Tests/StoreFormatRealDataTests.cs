using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Crateside.Core;
using Crateside.Core.Genres;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;

namespace Crateside.Tests;

/// <summary>Every file crateside keeps on this machine reads and writes back as it was.</summary>
public partial class StoreFormatRealDataTests
{
    private static readonly string Data = Environment.SpecialFolder.ApplicationData.GetFolderPath(
        "crateside"
    );

    private static readonly string Working =
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath("crateside", "working");

    [Fact]
    public void Genres_round_trip() =>
        RoundTrips("genres.json", GenreJsonContext.Default.ListGenreRecord);

    [Fact]
    public void Names_round_trip() =>
        RoundTrips("names.json", NamingJsonContext.Default.DictionaryStringResolvedName);

    [Fact]
    public void Jobs_round_trip_once_respelled()
    {
        var path = Path.Combine(Data, "jobs.jsonl");
        Assert.SkipUnless(File.Exists(path), "Needs this machine's jobs.jsonl.");

        var folder = Path.Combine(
            Path.GetTempPath(),
            "crateside-tests",
            Guid.NewGuid().ToString("n")
        );
        var copy = Path.Combine(folder, "jobs.jsonl");

        Directory.CreateDirectory(folder);

        try
        {
            File.Copy(path, copy);
            new JobStatusSpelling(copy, Path.Combine(folder, "backups"), TimeProvider.System).Run();

            foreach (
                var (line, index) in File.ReadLines(copy).Select((line, index) => (line, index))
            )
            {
                if (!string.IsNullOrWhiteSpace(line))
                    Same(line, JobsJsonContext.Default.JobUpdate, $"jobs.jsonl:{index + 1}");
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Actions_round_trip() =>
        EveryLineRoundTrips("actions.jsonl", JobsJsonContext.Default.UserAction);

    [Fact]
    public void Deletions_round_trip() =>
        EveryLineRoundTrips("deletions.jsonl", JobsJsonContext.Default.Deletion);

    [Fact]
    public void Acquisitions_round_trip()
    {
        Assert.SkipUnless(Directory.Exists(Working), "Needs this machine's working sets.");

        foreach (
            var file in Directory.EnumerateFiles(
                Working,
                WorkingSet.AcquisitionFileName,
                SearchOption.AllDirectories
            )
        )
            Same(File.ReadAllText(file), WorkingSetJsonContext.Default.Acquisition, file);
    }

    [Fact]
    public void Paused_reads_the_pascal_case_it_was_written_in_before_2026_09_27()
    {
        const string old = """
            {
              "Separation": true,
              "Judging": false,
              "Backfill": true,
              "BackfillJudgedAt": "2026-09-27T12:19:00+00:00"
            }
            """;

        var path = Path.Combine(Path.GetTempPath(), $"crateside-paused-{Guid.NewGuid():n}.json");
        File.WriteAllText(path, old);

        try
        {
            var read = new PauseStore(path).Read();

            Assert.Equal(new PausedStages { Separation = true, Backfill = true }, read);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void RoundTrips<T>(string name, JsonTypeInfo<T> info)
    {
        var path = Path.Combine(Data, name);
        Assert.SkipUnless(File.Exists(path), $"Needs this machine's {name}.");

        Same(File.ReadAllText(path), info, name);
    }

    private static void EveryLineRoundTrips<T>(string name, JsonTypeInfo<T> info)
    {
        var path = Path.Combine(Data, name);
        Assert.SkipUnless(File.Exists(path), $"Needs this machine's {name}.");

        foreach (var (line, index) in File.ReadLines(path).Select((line, index) => (line, index)))
        {
            if (!string.IsNullOrWhiteSpace(line))
                Same(line, info, $"{name}:{index + 1}");
        }
    }

    private static void Same<T>(string json, JsonTypeInfo<T> info, string where)
    {
        var written = JsonSerializer.Serialize(JsonSerializer.Deserialize(json, info)!, info);

        Assert.True(
            JsonNode.DeepEquals(
                Canonical(JsonNode.Parse(json)),
                Canonical(JsonNode.Parse(written))
            ),
            $"{where} changed on a round trip:\n{json}\n---\n{written}"
        );
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}")]
    private static partial Regex Instant { get; }

    // A null written and a null left out read back the same, as do two spellings of one instant.
    private static JsonNode? Canonical(JsonNode? node) =>
        node switch
        {
            JsonObject obj => new JsonObject(
                obj.Where(pair => pair.Value is not null)
                    .Select(pair => KeyValuePair.Create(pair.Key, Canonical(pair.Value)))
            ),
            JsonArray array => new JsonArray([.. array.Select(Canonical)]),
            JsonValue value
                when value.TryGetValue(out string? text)
                    && Instant.IsMatch(text)
                    && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, out var at) =>
                JsonValue.Create(at.ToString("O", CultureInfo.InvariantCulture)),
            _ => node?.DeepClone(),
        };
}
