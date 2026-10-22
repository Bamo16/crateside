using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Warps;

namespace Crateside.Core.Tracks;

public sealed class SampleReadingsReader(
    PresetCatalogReader presets,
    FileIndexReader fileIndex,
    CratesidePaths paths,
    WarpTempoReader warpTempos,
    BeatReader beats,
    WrittenWarps written
)
{
    public async Task<SampleReadings> ReadAsync(CancellationToken ct = default) =>
        new()
        {
            Presets = await presets.ReadAsync(ct),
            LibraryTags = AbletonReads.TagsOrEmpty(fileIndex.Read, paths.LibraryFolder),
            TempoOf = warpTempos.Read,
            BeatsOf = beats.Read,
            WrittenWarp = written.Of,
        };
}
