using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class VerdictFileTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        $"{Guid.NewGuid():n}.json"
    );

    public VerdictFileTests() => Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

    public void Dispose() => File.Delete(_path);

    private VerdictCheck Check(string json)
    {
        File.WriteAllText(_path, json);

        return VerdictFile.Check(_path);
    }

    private static string Verdict(string extra = "", string confidence = "HIGH") =>
        $$"""
            { "artist": "Real McCoy", "title": "Run Away (Instrumental)", "confidence": "{{confidence}}"{{extra}} }
            """;

    [Fact]
    public void A_flat_tonic_and_a_plain_mode_word_are_read_in_the_ledgers_spelling()
    {
        var run = Assert
            .IsType<VerdictCheck.Recordable>(
                Check(Verdict(""", "centre": { "tonic": "Bb", "mode": "minor" }"""))
            )
            .Run;

        Assert.Equal(new TonalCenter("A#", DiatonicMode.Aeolian), run.Center);
    }

    [Fact]
    public void The_song_identity_is_built_from_artist_and_title_without_the_stem_suffix()
    {
        var run = Assert
            .IsType<VerdictCheck.Recordable>(Check(Verdict(confidence: "WITHHELD")))
            .Run;

        Assert.Equal("real mccoy - run away", run.Song);
        Assert.Equal("Run Away", run.Title);
    }

    [Fact]
    public void A_sections_start_is_read_and_a_missing_one_is_left_null()
    {
        var run = Assert
            .IsType<VerdictCheck.Recordable>(
                Check(
                    Verdict(
                        """
                        , "centre": { "tonic": "C#", "mode": "ionian" },
                          "sections": [
                            { "label": "Verse", "tonic": "C#", "mode": "ionian" },
                            { "label": "Chorus", "tonic": "A#", "mode": "aeolian", "start": 62.5 }
                          ]
                        """
                    )
                )
            )
            .Run;

        Assert.Equal([null, 62.5], run.Sections.Select(section => section.Start));
    }

    [Fact]
    public void Sections_and_the_sections_a_source_covered_are_kept()
    {
        var run = Assert
            .IsType<VerdictCheck.Recordable>(
                Check(
                    Verdict(
                        """
                        , "centre": { "tonic": "C#", "mode": "ionian" },
                          "sections": [{ "label": "Chorus", "tonic": "A#", "mode": "aeolian" }],
                          "sources": [{ "url": "https://www.hooktheory.com/x", "kind": "authoritative", "states": "Bb minor", "sections": ["Chorus"] }]
                        """
                    )
                )
            )
            .Run;

        Assert.Equal(
            new Section("Chorus", new("A#", DiatonicMode.Aeolian)),
            Assert.Single(run.Sections)
        );
        Assert.Equal(["Chorus"], Assert.Single(run.Sources).Sections);
    }

    [Theory]
    [InlineData("", "no verdict file")]
    [InlineData("{ not json", "not valid JSON")]
    [InlineData("""{ "title": "x", "confidence": "HIGH" }""", "artist and title")]
    [InlineData("""{ "artist": "a", "title": "x", "confidence": "SURE" }""", "confidence must be")]
    public void An_unusable_file_says_what_to_fix(string json, string problem)
    {
        var check = json is "" ? VerdictFile.Check(_path) : Check(json);

        Assert.Contains(problem, Assert.IsType<VerdictCheck.Unusable>(check).Problem);
    }

    [Fact]
    public void A_mode_outside_the_seven_is_named_in_the_refusal()
    {
        var check = Check(Verdict(""", "centre": { "tonic": "A", "mode": "blues" }"""));

        Assert.Contains("\"blues\"", Assert.IsType<VerdictCheck.Unusable>(check).Problem);
    }

    [Fact]
    public void An_algorithmic_site_can_be_recorded_but_an_unknown_kind_cannot()
    {
        var check = Check(
            Verdict(
                """, "sources": [{ "url": "https://tunebat.com/x", "kind": "tier 3" }]""",
                confidence: "WITHHELD"
            )
        );

        Assert.Contains("tier 3", Assert.IsType<VerdictCheck.Unusable>(check).Problem);
    }

    [Fact]
    public void Windows_come_back_as_the_judges_with_the_withheld_verdict()
    {
        var check = Assert.IsType<VerdictCheck.WindowsAsked>(
            Check(
                Verdict(
                    """, "windows": [{ "start": 33, "end": 48.5, "label": "Chorus" }]""",
                    confidence: "WITHHELD"
                )
            )
        );

        Assert.Equal(
            new RunWindow(33, 48.5, "Chorus", WindowAsker.Judge),
            Assert.Single(check.Windows)
        );
        Assert.Equal(KeyConfidence.Withheld, check.Withheld.Confidence);
    }

    [Theory]
    [InlineData("HIGH", """[{ "start": 1, "end": 2 }]""", "only with WITHHELD")]
    [InlineData("WITHHELD", """[{ "start": 5, "end": 2 }]""", "not a stretch")]
    [InlineData(
        "WITHHELD",
        """[{ "start": 1, "end": 2 }, { "start": 1, "end": 2 }, { "start": 1, "end": 2 }, { "start": 1, "end": 2 }, { "start": 1, "end": 2 }]""",
        "at most 4"
    )]
    public void Windows_are_refused_where_they_cannot_be_run(
        string confidence,
        string windows,
        string problem
    )
    {
        var check = Check(Verdict($", \"windows\": {windows}", confidence));

        Assert.Contains(problem, Assert.IsType<VerdictCheck.Unusable>(check).Problem);
    }
}
