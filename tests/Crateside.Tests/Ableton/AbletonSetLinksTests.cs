using System.IO.Compression;
using System.Text;
using AbleKit.Sets;
using Crateside.Core;
using Crateside.Core.Ableton;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Ableton;

/// <summary>AbleKit does the finding and relinking; crateside names the sets and labels the backups.</summary>
public sealed class AbletonSetLinksTests : IDisposable
{
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
    public void A_relink_names_the_sets_and_keeps_a_crateside_backup()
    {
        Set("Mashup One Project/Mashup One.als", "C:/Samples/A (Vocal - Full).flac");
        var links = new AbletonSetLinks(
            new CratesidePaths { AbletonProjects = _root },
            new FakeTimeProvider(new(2026, 9, 27, 10, 15, 0, TimeSpan.Zero))
        );

        Assert.Equal(["Mashup One"], links.Using(["C:/Samples/A (Vocal - Full).flac"]));

        var relink = links.Relink([
            new SampleMove("C:/Samples/A (Vocal - Full).flac", "C:/Samples/B (Vocal - Full).flac"),
        ]);

        Assert.Equal(["Mashup One"], relink.Relinked);
        Assert.True(
            File.Exists(
                Path.Combine(
                    _root,
                    "Mashup One Project",
                    "Backup",
                    "Mashup One [crateside 2026-09-27 101500].als"
                )
            )
        );
    }

    [Theory]
    [InlineData(
        "Internet Gucci (S3RL x Kreayshawn) - Ableton Live 12 Lite",
        "Internet Gucci (S3RL x Kreayshawn)"
    )]
    [InlineData("50 Scent (fusq x 50 Cent)* - Ableton Live 12 Lite", "50 Scent (fusq x 50 Cent)")]
    [InlineData("A - B - Ableton Live 12 Suite", "A - B")]
    [InlineData("Ableton Live 12 Lite", null)]
    [InlineData("", null)]
    public void The_open_set_is_read_off_abletons_window_title(string title, string? set)
    {
        Assert.Equal(set, AbletonSession.SetFromTitle(title));
    }

    private void Set(string relative, string sample)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        gzip.Write(
            Encoding.UTF8.GetBytes(
                "<Ableton><SampleRef><FileRef>\r\n"
                    + $"\t<RelativePath Value=\"../../Samples/{Path.GetFileName(sample)}\" />\r\n"
                    + $"\t<Path Value=\"{sample}\" />\r\n"
                    + "</FileRef></SampleRef></Ableton>\r\n"
            )
        );
    }
}
