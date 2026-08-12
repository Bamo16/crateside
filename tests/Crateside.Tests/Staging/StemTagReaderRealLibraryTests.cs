using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>
/// Asserts the reader against the real Sample Staging folder, as the handoff asks. These
/// are machine-bound by design — the point is to prove the inference against actual files,
/// not against fixtures we wrote ourselves. They skip where the folder is absent.
/// </summary>
public class StemTagReaderRealLibraryTests
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";

    private static bool StagingPresent => Directory.Exists(StagingRoot);

    private static string[] Stems() =>
        Directory.GetFiles(StagingRoot, "*.flac", SearchOption.TopDirectoryOnly);

    [Fact]
    public void Every_staged_stem_yields_a_source_video_id()
    {
        Assert.SkipUnless(StagingPresent, $"{StagingRoot} not present on this machine.");

        var reader = new StemTagReader();
        var stems = Stems();

        Assert.NotEmpty(stems);

        var unjoinable = stems
            .Select(path => (Name: Path.GetFileName(path), Tags: reader.Read(path)))
            .Where(s => s.Tags?.SourceVideoId is null)
            .Select(s => s.Name)
            .ToArray();

        Assert.Empty(unjoinable);
    }

    [Fact]
    public void Stems_of_one_song_agree_on_the_video_id_and_differ_by_preset()
    {
        Assert.SkipUnless(StagingPresent, $"{StagingRoot} not present on this machine.");

        var reader = new StemTagReader();

        // Song-level rollup depends on this: the video ID is the song, the preset is the stem.
        var songs = Stems()
            .Select(reader.Read)
            .Where(t => t?.SourceVideoId is not null)
            .GroupBy(t => t!.SourceVideoId)
            .ToArray();

        Assert.NotEmpty(songs);

        foreach (var song in songs)
        {
            var presets = song.Select(s => s!.Provenance!.Preset).ToArray();
            Assert.Equal(presets.Length, presets.Distinct().Count());
        }
    }
}
