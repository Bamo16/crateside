using System.IO.Compression;
using System.Text;
using Crateside.Core;
using Crateside.Core.Ableton;

namespace Crateside.Tests.Ableton;

public class AbletonSetsTests : IDisposable
{
    private const string Library = "P:/RYAN/Ableton/Mashup Samples";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_set_that_uses_a_sample_by_path_is_named()
    {
        Set(
            "Mashup One Project/Mashup One.als",
            $"{Library}/T &amp; Sugah - TumDaDaDum (Vocal - Full).flac"
        );
        Set("Mashup Two Project/Mashup Two.als", $"{Library}/Something Else (Vocal - Full).flac");

        var sets = AbletonSets.Using(
            _root,
            [@"P:\RYAN\Ableton\Mashup Samples\T & Sugah - TumDaDaDum (Vocal - Full).flac"]
        );

        Assert.Equal(["Mashup One"], sets);
    }

    [Fact]
    public void Abletons_backups_are_not_sets()
    {
        Set(
            "Mashup One Project/Backup/Mashup One [2026-09-01 101010].als",
            $"{Library}/A (Vocal - Full).flac"
        );

        Assert.Empty(AbletonSets.Using(_root, [$"{Library}/A (Vocal - Full).flac"]));
    }

    [Fact]
    public void A_file_that_is_not_a_set_is_passed_over()
    {
        var path = Path.Combine(_root, "Broken Project", "Broken.als");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not gzip");

        Assert.Empty(AbletonSets.Using(_root, [$"{Library}/A (Vocal - Full).flac"]));
    }

    private void Set(string relative, string samplePath)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        gzip.Write(
            Encoding.UTF8.GetBytes(
                $"""
                <Ableton><SampleRef><FileRef>
                    <RelativePath Value="../../Mashup Samples/x.flac" />
                    <Path Value="{samplePath}" />
                </FileRef></SampleRef></Ableton>
                """
            )
        );
    }
}

/// <summary>The lookup against this machine's own sets.</summary>
public class AbletonSetsRealLibraryTests
{
    private static readonly CratesidePaths Paths = new();

    [Fact]
    public void A_sample_a_set_uses_is_found_in_it()
    {
        Assert.SkipUnless(Directory.Exists(Paths.AbletonProjects), "No Ableton projects here.");

        var sets = AbletonSets.Using(
            Paths.AbletonProjects,
            [Path.Combine(Paths.MashupSamples, "Fat Dog - Running (Instrumental - Full).flac")]
        );

        Assert.Contains("Hell for Leather (Aiobahn x Fat Dog)", sets);
    }
}
