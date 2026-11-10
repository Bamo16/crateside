using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

/// <summary>Picking one section of a run to read what was found for it (#180).</summary>
public sealed class SectionEvidenceTests
{
    private static readonly Section A = new("A", new("C", DiatonicMode.Ionian), 0);
    private static readonly Section B = new("B (Am/Dm)", new("A", DiatonicMode.Aeolian), 60);
    private static readonly Section Return = new("A return", new("C", DiatonicMode.Ionian), 120);

    private static readonly RunWindow Chorus = new(62, 90, "B", WindowAsker.Judge);
    private static readonly RunWindow Opening = new(0, 30, "A", WindowAsker.User);

    private static readonly RunEvidence Run = new()
    {
        Timestamp = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
        Song = "Mike Diva - Our Glorious Leader",
        Center = A.Center,
        Sections = [A, B, Return],
        Readings = [new(Opening, [], null, A), new(Chorus, [], null, B)],
        Sources =
        [
            new() { Url = "https://example.com/everything", States = "C major" },
            new()
            {
                Url = "https://example.com/bridge",
                States = "A minor",
                Sections = ["B (Am/Dm)"],
            },
            new()
            {
                Url = "https://example.com/the-rest",
                States = "C major",
                Sections = ["A", "A return"],
            },
        ],
    };

    [Fact]
    public void A_section_is_found_by_its_label_whatever_the_case()
    {
        Assert.Equal(B, SectionEvidence.Of(Run, "b (am/dm)")?.Section);
    }

    [Fact]
    public void A_label_the_run_has_no_section_for_finds_nothing()
    {
        Assert.Null(SectionEvidence.Of(Run, "outro"));
    }

    [Fact]
    public void The_windows_that_settled_the_section_come_with_it_and_no_others()
    {
        var window = Assert.Single(SectionEvidence.Of(Run, "B (Am/Dm)")!.Windows);

        Assert.Equal(Chorus, window.Window);
        Assert.Empty(SectionEvidence.Of(Run, "A return")!.Windows);
    }

    [Fact]
    public void A_source_that_named_the_section_is_set_apart_from_those_that_covered_the_whole_song()
    {
        var bridge = SectionEvidence.Of(Run, "B (Am/Dm)")!;

        Assert.Equal(["https://example.com/bridge"], bridge.Naming.Select(source => source.Url));
        Assert.Equal(
            ["https://example.com/everything"],
            bridge.WholeSong.Select(source => source.Url)
        );
    }

    [Fact]
    public void A_source_that_named_other_sections_says_nothing_of_this_one()
    {
        var opening = SectionEvidence.Of(Run, "A")!;

        Assert.DoesNotContain(opening.Naming, source => source.Url == "https://example.com/bridge");
        Assert.Equal(["https://example.com/the-rest"], opening.Naming.Select(source => source.Url));
    }
}
