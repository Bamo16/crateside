using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>
/// The naming rule against the real staging folder — the files renaming would actually move.
/// </summary>
/// <remarks>
/// The rule keeps whatever is in the last parentheses and replaces everything before it. That is
/// only right while the last parentheses hold a preset, and titles have parentheses of their own:
/// <c>1991 - Jungle (Colin Hennerz Remix) (Instrumental - Full)</c> is a real name in this
/// library. A fixture cannot answer whether the assumption holds; the folder can.
/// </remarks>
public class StemFileNameRealLibraryTests
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";

    private static bool Available => Directory.Exists(StagingRoot);

    private static IEnumerable<string> Staged() =>
        Directory.EnumerateFiles(StagingRoot).Where(StagingBoard.IsStemFile);

    /// <summary>
    /// What the rule keeps is a preset, not part of a title.
    /// </summary>
    /// <remarks>
    /// <strong>The two spellings of a preset are not the same string.</strong> The filename says
    /// <c>(Drums)</c> where the provenance says <c>Drums - htdemucs_ft</c> — the separator's model
    /// reaches the tag and not the name. So the name is a <em>prefix</em> of the tag, which is
    /// what this asserts; equality was tried against the real folder and is false.
    /// </remarks>
    [Fact]
    public void The_last_parentheses_hold_a_preset_rather_than_part_of_the_title()
    {
        Assert.SkipUnless(Available, "The staging root must be present.");

        var files = Staged().ToArray();

        Assert.NotEmpty(files);

        foreach (var path in files)
        {
            // Read off the file's own tags rather than a list of preset names, so this keeps
            // answering if StemForge grows another one.
            var preset = StemTagReader.Read(path)?.Provenance?.Preset;
            var name = Path.GetFileNameWithoutExtension(path);
            var kept = name[StemFileName.WithoutPreset(path).Length..].Trim().Trim('(', ')');

            if (preset is null)
                Assert.Equal(string.Empty, kept);
            else
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

            // And it is reversible, which is what Revert relies on.
            Assert.Equal(path, StemFileName.Renamed(renamed, StemFileName.WithoutPreset(path)));
        }
    }
}
