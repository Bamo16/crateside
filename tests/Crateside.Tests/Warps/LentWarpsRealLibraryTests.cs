using Crateside.Core;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class LentWarpsRealLibraryTests
{
    private static readonly CratesidePaths Paths = new();

    [Fact]
    public async Task A_staged_stem_still_holding_the_warp_crateside_lent_it_is_not_lent_it_again()
    {
        Assert.SkipUnless(Directory.Exists(Paths.StagingFolder), "Staging is not here.");

        var written = new WrittenWarps(Paths.WrittenWarpsFile);
        var writer = new SidecarWriter(
            new FfmpegAudioDecoder(),
            new OnsetDetector(),
            written,
            new TransientOrigins()
        );
        var siblings = WarpSiblings.Scan([Paths.StagingFolder, Paths.LibraryFolder]);
        var lent = siblings
            .In(Paths.StagingFolder)
            .Where(target => written.BorrowedFrom(target.Path) is not null)
            .ToList();

        Assert.SkipWhen(lent is [], "No staged stem holds a lent warp.");

        List<string> relent = [];

        foreach (var target in lent)
        {
            var plan = await writer.PrepareBorrowAsync(
                target.Path,
                siblings.AuthoritiesFor(target),
                siblings.SameKindAs(target),
                TestContext.Current.CancellationToken
            );

            if (plan is SidecarPlan.Ready)
                relent.Add(Path.GetFileName(target.Path));
        }

        Assert.Empty(relent);
    }
}
