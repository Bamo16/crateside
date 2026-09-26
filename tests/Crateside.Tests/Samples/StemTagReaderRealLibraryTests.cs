using Crateside.Core.Samples;

namespace Crateside.Tests.Samples;

/// <summary>The reader against the real Sample Staging folder.</summary>
public class StemTagReaderRealLibraryTests
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";

    private static bool StagingPresent => RealLibrary.HasStems(StagingRoot);

    private static string[] Stems() =>
        Directory.GetFiles(StagingRoot, "*.flac", SearchOption.TopDirectoryOnly);

    [Fact]
    public void Every_staged_stem_yields_a_source_video_id()
    {
        Assert.SkipUnless(StagingPresent, $"{StagingRoot} not present on this machine.");

        var stems = Stems();

        Assert.NotEmpty(stems);

        var unjoinable = stems
            .Select(path => (Name: Path.GetFileName(path), Tags: StemTagReader.Read(path)))
            .Where(s => s.Tags?.SourceVideoId is null)
            .Select(s => s.Name)
            .ToArray();

        Assert.Empty(unjoinable);
    }

    [Fact]
    public void Stems_of_one_song_agree_on_the_video_id_and_differ_by_preset()
    {
        Assert.SkipUnless(StagingPresent, $"{StagingRoot} not present on this machine.");

        var songs = Stems()
            .Select(StemTagReader.Read)
            .Where(t => t?.SourceVideoId is not null)
            .GroupBy(t => t!.SourceVideoId)
            .ToArray();

        Assert.NotEmpty(songs);

        foreach (var song in songs)
        {
            // An adopted sample has a label and no preset (ADR 0022); the kept source has neither.
            var named = song.Select(s => s!.Provenance!.Preset ?? s!.Provenance!.Label).ToArray();

            Assert.Equal(named.Length, named.Distinct().Count());
        }
    }
}
