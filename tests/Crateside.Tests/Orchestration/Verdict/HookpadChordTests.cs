using System.Text.Json;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class HookpadChordTests
{
    private static readonly JsonElement Shared = JsonSerializer.Deserialize<JsonElement>(
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "SourcePages", "hookpad-labels.json")
        )
    );

    private static IEnumerable<JsonElement> SharedSections =>
        Shared.GetProperty("sections").EnumerateArray();

    private static HooktheoryPageReader.ScoreDto Score(JsonElement section) =>
        section.GetProperty("score").Deserialize(HooktheoryJsonContext.Default.ScoreDto)!;

    private static JsonElement Section(string page, string name) =>
        SharedSections.Single(section =>
            section.GetProperty("page").GetString()!.EndsWith(page, StringComparison.Ordinal)
            && section.GetProperty("section").GetString() == name
        );

    [Fact]
    public void Every_chord_of_the_saved_scores_gets_the_label_the_harness_gives_it()
    {
        foreach (var section in SharedSections)
        {
            List<string> expected =
            [
                .. section
                    .GetProperty("labels")
                    .EnumerateArray()
                    .Select(label => label.GetString()!)
                    .Where(label => label != "X"),
            ];

            Assert.Equal(expected, HookpadChord.Read(Score(section)).Select(chord => chord.Label));
        }
    }

    [Fact]
    public void Epics_chorus_reads_its_minor_bars_by_the_key_track_not_the_sections_first_key()
    {
        var chords = HookpadChord.Read(Score(Section("faith-no-more/epic", "Chorus")));

        Assert.Equal(
            ["C:maj", "D:maj", "E:min", "G:maj", "B:min/5"],
            chords
                .Where(chord => chord.Beat is >= 9 and < 17)
                .Select(chord => chord.Label)
                .Distinct()
        );
        Assert.DoesNotContain(chords, chord => chord.Label is "C#:min" or "D#:dim");
    }

    [Fact]
    public void A_section_that_changes_key_shows_its_chords_by_key_instead_of_the_pages_numerals()
    {
        var epic = Section("faith-no-more/epic", "Chorus");
        var score = Score(epic);
        var lines = VerdictRunner.PagesLines([
            new(
                "Hooktheory",
                LookupStatus.Found,
                [
                    new FetchedPage
                    {
                        Site = "Hooktheory",
                        Url = "https://www.hooktheory.com/theorytab/view/faith-no-more/epic",
                        Tab = new HooktheoryTab
                        {
                            Sections =
                            [
                                new TabSection(
                                    "Chorus",
                                    "I V vi viio I viio vi I iii V64 iii",
                                    null,
                                    null
                                )
                                {
                                    Keys =
                                    [
                                        .. score.Keys!.Select(key => new TabKey(
                                            key.Beat,
                                            $"{key.Tonic} {key.Scale}"
                                        )),
                                    ],
                                    Harmony = HookpadChord.Read(score),
                                },
                            ],
                        },
                    },
                ]
            ),
        ]);

        Assert.Contains(
            lines,
            line =>
                line.StartsWith("  - Chorus, its chords by its score", StringComparison.Ordinal)
                && line.Contains(
                    "E major from beat 1: E:maj B:maj | E minor from beat 9: C:maj D:maj E:min D:maj C:maj E:min G:maj B:min/5 G:maj | E major from beat 17",
                    StringComparison.Ordinal
                )
        );
        Assert.DoesNotContain("  - Chorus: I V vi viio I viio vi I iii V64 iii", lines);
    }
}
