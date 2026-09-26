using Crateside.Core.Ableton;

namespace Crateside.Tests.Ableton;

/// <summary>The XMP contract, asserted against both real roots.</summary>
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

    [Fact]
    public void A_name_under_two_roots_still_means_its_own_roots_file()
    {
        Assert.SkipUnless(
            Directory.Exists(StagingRoot) && Directory.Exists(MashupRoot),
            "Both Ableton roots must be present."
        );

        // The two roots' stores have different UUIDs; the reader keys on the containing root.
        var reader = new AbletonFolderInfoReader();
        var staging = reader.Read(StagingRoot);
        var mashup = reader.Read(MashupRoot);

        // A name can be under both roots on purpose; it must mean its own root's file.
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

        // Asserts the shape, not the count: the user may fix them at any time.
        var partial = new AbletonFolderInfoReader()
            .Read(MashupRoot)
            .Entries.Where(e => e.HasPartialKey)
            .ToArray();

        Assert.All(partial, e => Assert.Null(e.Key));
        Assert.All(partial, e => Assert.False(e.IsKeyTagged));
    }
}
