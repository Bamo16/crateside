using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class PageStatementsTests
{
    private static FetchedPage Page(params TabSection[] sections) =>
        new()
        {
            Site = "Hooktheory",
            Url = "https://www.hooktheory.com/theorytab/view/x/y",
            Keys = [new("C# minor", "Verse", null), new("F# Dorian", "Outro", null)],
            Tab = new HooktheoryTab { Sections = sections },
        };

    private static TabSection Section(string name, params IReadOnlyList<TabKey> keys) =>
        new(name, null, null, null) { Keys = keys };

    [Fact]
    public void A_section_that_changes_key_inside_itself_says_so()
    {
        var page = Page(
            Section("Verse", new TabKey(1, "C# minor")),
            Section("Outro", new TabKey(1, "F# dorian"), new TabKey(65, "F# mixolydian"))
        );

        Assert.Equal(
            "C# minor; Outro: F# dorian → F# mixolydian",
            PageStatements.Of(page, ["C# minor", "F# Dorian"])
        );
    }

    [Fact]
    public void Sections_that_stay_in_one_key_state_just_the_key()
    {
        var page = Page(
            Section("Verse", new TabKey(1, "C# minor")),
            Section("Outro", new TabKey(1, "F# dorian"), new TabKey(65, "F# dorian"))
        );

        Assert.Equal("C# minor; F# Dorian", PageStatements.Of(page, ["C# minor", "F# Dorian"]));
    }

    [Fact]
    public void A_page_with_no_score_states_its_keys_as_before()
    {
        var page = Page();

        Assert.Equal("F# Dorian", PageStatements.Of(page, ["F# Dorian", "F# Dorian"]));
    }
}
