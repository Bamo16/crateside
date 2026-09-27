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

    [Fact]
    public void Relinking_rewrites_the_path_and_the_relative_path_and_nothing_else()
    {
        var set = Set(
            "Mashup One Project/Mashup One.als",
            $"{Library}/Glen Check - Dazed &amp; Confused (Vocal - Full).flac",
            $"{Library}/Untouched (Vocal - Full).flac"
        );
        var before = Xml(set);

        var outcome = AbletonSets.Relink(
            _root,
            [
                new(
                    @"P:\RYAN\Ableton\Mashup Samples\Glen Check - Dazed & Confused (Vocal - Full).flac",
                    @"P:\RYAN\Ableton\Mashup Samples\Dazed & Confused (Vocal - Full).flac"
                ),
            ],
            Now
        );

        Assert.Equal(["Mashup One"], outcome.Relinked);
        Assert.Equal(
            before
                .Replace(
                    "../../Mashup Samples/Glen Check - Dazed &amp; Confused (Vocal - Full).flac",
                    "../../Mashup Samples/Dazed &amp; Confused (Vocal - Full).flac"
                )
                .Replace(
                    $"{Library}/Glen Check - Dazed &amp; Confused (Vocal - Full).flac",
                    $"{Library}/Dazed &amp; Confused (Vocal - Full).flac"
                ),
            Xml(set)
        );
    }

    [Fact]
    public void Relinking_keeps_the_set_as_it_was_in_the_projects_backup_folder()
    {
        var set = Set("Mashup One Project/Mashup One.als", $"{Library}/A (Vocal - Full).flac");
        var before = File.ReadAllBytes(set);

        AbletonSets.Relink(
            _root,
            [new($"{Library}/A (Vocal - Full).flac", $"{Library}/B (Vocal - Full).flac")],
            Now
        );

        var backup = Path.Combine(
            _root,
            "Mashup One Project",
            "Backup",
            "Mashup One [crateside 2026-09-27 101500].als"
        );
        Assert.Equal(before, File.ReadAllBytes(backup));
    }

    [Fact]
    public void A_set_that_does_not_use_the_file_is_not_touched()
    {
        var set = Set("Mashup Two Project/Mashup Two.als", $"{Library}/Other (Vocal - Full).flac");
        var written = File.GetLastWriteTimeUtc(set);

        var outcome = AbletonSets.Relink(
            _root,
            [new($"{Library}/A (Vocal - Full).flac", $"{Library}/B (Vocal - Full).flac")],
            Now
        );

        Assert.Empty(outcome.Relinked);
        Assert.Equal(written, File.GetLastWriteTimeUtc(set));
        Assert.False(Directory.Exists(Path.Combine(_root, "Mashup Two Project", "Backup")));
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

    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 15, 0, TimeSpan.Zero);

    private static string Xml(string set)
    {
        using var file = File.OpenRead(set);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        return reader.ReadToEnd();
    }

    private string Set(string relative, params string[] samplePaths)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        gzip.Write(
            Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Ableton>\r\n"
                    + string.Concat(
                        samplePaths.Select(sample =>
                            $"<SampleRef><FileRef>\r\n"
                            + $"\t<RelativePath Value=\"../../Mashup Samples/{sample[(sample.LastIndexOf('/') + 1)..]}\" />\r\n"
                            + $"\t<Path Value=\"{sample}\" />\r\n"
                            + "\t<OriginalFileSize Value=\"123\" />\r\n"
                            + "</FileRef><Name Value=\"clip\" /></SampleRef>\r\n"
                        )
                    )
                    + "</Ableton>\r\n"
            )
        );

        return path;
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

/// <summary>Relinking copies of this machine's own sets.</summary>
public class AbletonSetsRelinkRealDataTests : IDisposable
{
    private static readonly CratesidePaths Paths = new();

    private readonly string _copy = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_copy))
            Directory.Delete(_copy, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Relinking_a_real_sample_changes_only_the_lines_that_name_it()
    {
        Assert.SkipUnless(Directory.Exists(Paths.AbletonProjects), "No Ableton projects here.");

        foreach (
            var set in Directory.EnumerateFiles(
                Paths.AbletonProjects,
                "*.als",
                SearchOption.AllDirectories
            )
        )
        {
            var relative = Path.GetRelativePath(Paths.AbletonProjects, set);

            if (relative.Split(Path.DirectorySeparatorChar).Contains("Backup"))
                continue;

            var target = Path.Combine(_copy, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(set, target);
        }

        var from = Path.Combine(
            Paths.MashupSamples,
            "Glen Check - Dazed & Confused (Instrumental - Full).flac"
        );
        var to = Path.Combine(Paths.MashupSamples, "Renamed & Relinked (Instrumental - Full).flac");
        var sets = AbletonSets.Using(_copy, [from]);
        var before = sets.ToDictionary(name => name, name => Lines(Find(name)));

        var outcome = AbletonSets.Relink(_copy, [new(from, to)], DateTimeOffset.UtcNow);

        Assert.NotEmpty(sets);
        Assert.Equal(sets, outcome.Relinked);
        Assert.Empty(outcome.Failed);
        Assert.Empty(AbletonSets.Using(_copy, [from]));
        Assert.Equal(sets, AbletonSets.Using(_copy, [to]));

        foreach (var name in sets)
        {
            var after = Lines(Find(name));

            Assert.Equal(before[name].Length, after.Length);

            var changed = before[name].Zip(after).Where(pair => pair.First != pair.Second).ToList();

            Assert.NotEmpty(changed);
            Assert.All(
                changed,
                pair =>
                    Assert.Equal(
                        pair.First.Replace(
                            "Glen Check - Dazed &amp; Confused",
                            "Renamed &amp; Relinked"
                        ),
                        pair.Second
                    )
            );
        }
    }

    private string Find(string name) =>
        Directory
            .EnumerateFiles(_copy, $"{name}.als", SearchOption.AllDirectories)
            .First(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}Backup{Path.DirectorySeparatorChar}")
            );

    private static string[] Lines(string set)
    {
        using var file = File.OpenRead(set);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        return reader.ReadToEnd().Split('\n');
    }
}
