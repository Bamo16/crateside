using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>The naming rule against the real staging folder, where titles have parentheses too.</summary>
public class StemFileNameRealLibraryTests
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";

    private static bool Available => RealLibrary.HasStems(StagingRoot);

    private static IEnumerable<string> Staged() =>
        Directory.EnumerateFiles(StagingRoot).Where(StagingBoard.IsStemFile);

    /// <summary>
    /// A prefix, not equality: the name says <c>(Drums)</c> where the tag says <c>Drums - htdemucs_ft</c>.
    /// </summary>
    [Fact]
    public void The_last_parentheses_hold_a_preset_rather_than_part_of_the_title()
    {
        Assert.SkipUnless(Available, "The staging root must be present.");

        var files = Staged().ToArray();

        Assert.NotEmpty(files);

        foreach (var path in files)
        {
            var preset = StemTagReader.Read(path)?.Provenance?.Preset;
            var name = Path.GetFileNameWithoutExtension(path);
            var kept = name[StemFileName.WithoutPreset(path).Length..].Trim().Trim('(', ')');

            // A source's parentheses hold its label, so only a preset can be checked against the tag.
            if (preset is not null)
                Assert.StartsWith(kept, preset, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Renaming_a_real_file_changes_the_name_and_nothing_else()
    {
        Assert.SkipUnless(Available, "The staging root must be present.");

        foreach (var path in Staged())
        {
            var renamed = StemFileName.Renamed(path, "Somebody - Something");

            Assert.Equal(Path.GetDirectoryName(path), Path.GetDirectoryName(renamed));
            Assert.Equal(Path.GetExtension(path), Path.GetExtension(renamed));
            Assert.StartsWith(
                "Somebody - Something",
                Path.GetFileName(renamed),
                StringComparison.Ordinal
            );

            Assert.Equal(path, StemFileName.Renamed(renamed, StemFileName.WithoutPreset(path)));
        }
    }
}
