using System.Text.Json;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Verdict;

/// <summary>Every verdict keytag recorded, written as a verdict file, reads back as the same verdict.</summary>
public sealed class VerdictFileRealDataTests : IDisposable
{
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";

    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        $"{Guid.NewGuid():n}.json"
    );

    public VerdictFileRealDataTests() => Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Every_recorded_verdict_survives_the_trip_through_a_verdict_file()
    {
        Assert.SkipUnless(File.Exists(LedgerPath), $"{LedgerPath} not present.");

        var latest = TestLedger.LatestEvidence(LedgerPath);
        var confidence = TestLedger
            .From(LedgerPath)
            .Runs.Where(run => run.Restates is null)
            .GroupBy(run => run.Song, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                song => song.Key,
                song => song.MaxBy(run => run.Timestamp)!.Confidence,
                StringComparer.OrdinalIgnoreCase
            );

        Assert.NotEmpty(latest);

        foreach (var (song, evidence) in latest)
        {
            File.WriteAllText(_path, AsVerdictFile(evidence, confidence[song]));

            var run = Assert.IsType<VerdictCheck.Recordable>(VerdictFile.Check(_path)).Run;

            Assert.Equal(song, run.Song, ignoreCase: true);
            Assert.Equal(evidence.Centre, run.Centre);
            Assert.Equal(evidence.Sections, run.Sections);
            Assert.Null(
                RunValidation.Refusal(
                    RunValidation.Normalize(
                        run with
                        {
                            SourceUrl = "https://music.youtube.com/watch?v=x",
                        }
                    )
                )
            );
        }
    }

    /// <summary>What a session would write for this verdict, in the shape judge-prompt.md gives.</summary>
    private static string AsVerdictFile(RunEvidence evidence, KeyConfidence confidence) =>
        JsonSerializer.Serialize(
            new
            {
                artist = evidence.Artist,
                title = evidence.Title,
                confidence = confidence.ToString().ToUpperInvariant(),
                centre = evidence.Centre is { } centre
                    ? new { tonic = centre.Tonic, mode = centre.Mode.ToString().ToLowerInvariant() }
                    : null,
                mixed = evidence.Mixed,
                sections = evidence.Sections.Select(section => new
                {
                    label = section.Label,
                    tonic = section.Centre.Tonic,
                    mode = section.Centre.Mode.ToString(),
                }),
                sources = evidence.Sources.Select(source => new
                {
                    url = source.Url,
                    kind = source.RecordedKind ?? "corroborating",
                    states = source.States,
                    statesKey = source.StatesKey,
                    statesMode = source.StatesMode,
                    sections = source.Sections,
                    note = source.Note,
                }),
                tonicCheck = evidence.TonicCheck,
                reasoning = evidence.Reasoning,
            }
        );
}
