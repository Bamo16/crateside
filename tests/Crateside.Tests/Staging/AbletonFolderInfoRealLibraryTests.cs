using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>
/// The XMP contract is the only load-bearing inference in the design, so it is asserted
/// against both real roots rather than against fixtures we wrote ourselves. Machine-bound by
/// design; skips where the roots are absent.
/// </summary>
public class AbletonFolderInfoRealLibraryTests
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";
    private const string MashupRoot = @"P:\RYAN\Ableton\Mashup Samples";

    public static TheoryData<string> Roots => [StagingRoot, MashupRoot];

    [Theory]
    [MemberData(nameof(Roots))]
    public void Reads_every_entry_in_a_real_root(string root)
    {
        Assert.SkipUnless(Directory.Exists(root), $"{root} not present on this machine.");

        var info = new AbletonFolderInfoReader().Read(root);

        Assert.NotEmpty(info.Entries);
        Assert.All(info.Entries, e => Assert.NotEmpty(e.RelativePath));
        Assert.All(info.Entries, e => Assert.NotEmpty(e.Keywords));
    }

    // Removed 2026-09-02: Every_entry_points_at_a_file_that_exists. It asserted that Ableton
    // migrates entries when files move and drops them when files vanish — "if this ever fails,
    // the index is not self-cleaning after all". It did fail, and the premise turned out to be
    // wrong rather than the library: Ableton prunes its own index and leaves the folder XMP
    // alone, so five entries for departed files sat in staging while the browser showed those
    // files correctly tagged in their new home (ADR 0003, 2026-09-02).
    //
    // Nothing crateside does depends on the store being tidy, and the user finishing a track
    // falsifies it, so the assertion was a false alarm waiting to happen. What crateside does
    // depend on — that a stale entry is never consulted — is asserted in
    // StagingBoardRealLibraryTests, where it belongs and where ordinary work cannot break it.

    [Fact]
    public void A_name_under_two_roots_still_means_its_own_roots_file()
    {
        Assert.SkipUnless(
            Directory.Exists(StagingRoot) && Directory.Exists(MashupRoot),
            "Both Ableton roots must be present."
        );

        // This used to also assert the two roots share one UUID, on the inference that it is
        // per-install. Measured false on 2026-09-02: Mashup Samples is
        // dc66a3fa-0fe1-5352-… and Sample Staging is 37a64010-9abd-4a98-…, a name-derived v5
        // beside a random v4. ADR 0003 records crateside creating the staging store from
        // nothing, which is consistent with it having minted that v4 — consistent with, not
        // demonstrated. Nothing depended on the inference either way: the reader keys on the
        // containing root and never on the filename, which is what the rest of this asserts.
        var reader = new AbletonFolderInfoReader();
        var staging = reader.Read(StagingRoot);
        var mashup = reader.Read(MashupRoot);

        // The two sets used to be asserted disjoint, which was an observation about the library
        // rather than a property of the reader: re-cutting or swapping a track whose old samples
        // are still in Mashup Samples puts the same name under both roots on purpose. What has
        // to hold is that a name means its own root's file.
        foreach (
            var shared in staging
                .Entries.Select(e => e.RelativePath)
                .Intersect(
                    mashup.Entries.Select(e => e.RelativePath),
                    StringComparer.OrdinalIgnoreCase
                )
        )
        {
            Assert.True(staging.TryGet(Path.Combine(StagingRoot, shared), out _));
            Assert.False(staging.TryGet(Path.Combine(MashupRoot, shared), out _));
            Assert.False(mashup.TryGet(Path.Combine(StagingRoot, shared), out _));
        }
    }

    [Fact]
    public void Keys_recombine_into_one_of_the_24_ledger_spellings()
    {
        Assert.SkipUnless(Directory.Exists(MashupRoot), $"{MashupRoot} not present.");

        string[] tonics = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        var valid = tonics
            .SelectMany(t => new[] { $"{t} major", $"{t} minor" })
            .ToHashSet(StringComparer.Ordinal);

        var keys = new AbletonFolderInfoReader()
            .Read(MashupRoot)
            .Entries.Where(e => e.IsKeyTagged)
            .Select(e => e.Key!)
            .ToArray();

        Assert.NotEmpty(keys);
        Assert.All(keys, k => Assert.Contains(k, valid));
    }

    [Fact]
    public void Half_tagged_files_are_reported_as_partial_not_as_keyed()
    {
        Assert.SkipUnless(Directory.Exists(MashupRoot), $"{MashupRoot} not present.");

        // As of 2026-08-12 the Mashup Samples root holds exactly two of these: both Fedde Le
        // Grand "Put Your Hands Up For Detroit" stems carry Key|D with no mode. This asserts
        // the shape, not the count — the user may fix them at any time.
        var partial = new AbletonFolderInfoReader()
            .Read(MashupRoot)
            .Entries.Where(e => e.HasPartialKey)
            .ToArray();

        Assert.All(partial, e => Assert.Null(e.Key));
        Assert.All(partial, e => Assert.False(e.IsKeyTagged));
    }
}
