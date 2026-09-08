using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>
/// Where a stem's name ends and its preset begins — the one rule renaming and grouping share.
/// </summary>
public class StemFileNameTests
{
    private static readonly string Folder = Path.Combine("P:", "Sample Staging");

    private static string In(string name) => Path.Combine(Folder, name);

    [Theory]
    [InlineData("99th Street (Vocal - Full).flac", "Yes - No (Vocal - Full).flac")]
    [InlineData("99th Street (Drums).flac", "Yes - No (Drums).flac")]
    [InlineData("99th Street.flac", "Yes - No.flac")]
    [InlineData("99th Street (Vocal - Full).wav", "Yes - No (Vocal - Full).wav")]
    public void The_preset_and_the_extension_are_kept(string before, string after)
    {
        Assert.Equal(In(after), StemFileName.Renamed(In(before), "Yes - No"));
    }

    [Fact]
    public void A_name_with_parentheses_of_its_own_only_loses_the_trailing_one()
    {
        // Titles carry parentheticals — "(Radio Edit)", "(feat. X)" — and only the last one is
        // StemForge's. Anything else is part of the name and stays.
        Assert.Equal(
            In("Yes - No (Radio Edit) (Vocal - Full).flac"),
            StemFileName.Renamed(In("99th Street (Vocal - Full).flac"), "Yes - No (Radio Edit)")
        );
    }

    [Fact]
    public void Renaming_to_the_name_a_file_already_has_changes_nothing()
    {
        // What lets an interrupted rename be finished rather than restarted.
        var path = In("Yes - No (Vocal - Full).flac");

        Assert.Equal(path, StemFileName.Renamed(path, "Yes - No"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("A/B", false)]
    [InlineData("A: B", false)]
    [InlineData("A?", false)]
    [InlineData("Starjunk 95 - 99th Street", true)]
    public void A_name_is_usable_only_if_a_file_could_carry_it(string? name, bool usable)
    {
        // Whitespace needs its own check: a name of spaces passes every length test and then
        // names a file nobody can find.
        Assert.Equal(usable, StemFileName.IsUsable(name));
    }
}
