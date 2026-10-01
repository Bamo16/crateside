using Crateside.Core.Samples;

namespace Crateside.Tests.Samples;

/// <summary>Where a stem's name ends and its preset begins.</summary>
public sealed class StemFileNameTests
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
        // Only the last parenthetical is StemForge's.
        Assert.Equal(
            In("Yes - No (Radio Edit) (Vocal - Full).flac"),
            StemFileName.Renamed(In("99th Street (Vocal - Full).flac"), "Yes - No (Radio Edit)")
        );
    }

    [Fact]
    public void Renaming_to_the_name_a_file_already_has_changes_nothing()
    {
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
        Assert.Equal(usable, StemFileName.IsUsable(name));
    }
}
