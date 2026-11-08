using System.IO.Compression;
using Crateside.Core;
using Crateside.Core.Imports;
using Crateside.Core.Plans;

namespace Crateside.Tests.Imports;

public sealed class InboxScannerTests : IDisposable
{
    private readonly IntakeWorld _world = new();

    public void Dispose() => _world.Dispose();

    private InboxScanner Scanner =>
        field ??= new InboxScanner(_world.Files, _world.Paths, _world.Time);

    private string Inbox => _world.Paths.Inbox;

    private InboxScan SettleAndScan()
    {
        Scanner.Scan();
        _world.Time.Advance(InboxScanner.StillFor);

        return Scanner.Scan();
    }

    [Fact]
    public void A_file_is_taken_in_only_once_its_size_has_held_still_and_is_then_consumed()
    {
        var file = IntakeWorld.TaggedFlac(Inbox, "song.flac");

        Assert.Empty(Scanner.Scan().Outcomes);
        _world.Time.Advance(InboxScanner.StillFor - TimeSpan.FromSeconds(1));
        Assert.Empty(Scanner.Scan().Outcomes);
        Assert.True(File.Exists(file));

        _world.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.IsType<IntakeOutcome.TakenIn>(Assert.Single(Scanner.Scan().Outcomes));
        Assert.False(File.Exists(file));
        Assert.Single(_world.Intake.Read());
    }

    [Fact]
    public void A_file_that_is_still_growing_is_left_alone()
    {
        var file = IntakeWorld.TaggedFlac(Inbox, "song.flac");

        Scanner.Scan();
        _world.Time.Advance(InboxScanner.StillFor);
        File.AppendAllText(file, "more");

        Assert.Empty(Scanner.Scan().Outcomes);
        Assert.True(File.Exists(file));
    }

    [Theory]
    [InlineData(".part")]
    [InlineData(".crdownload")]
    [InlineData(".tmp")]
    public void A_partial_download_is_ignored_and_not_even_named(string extension)
    {
        File.WriteAllText(
            Path.Combine(Directory.CreateDirectory(Inbox).FullName, $"song.flac{extension}"),
            "x"
        );

        var scan = SettleAndScan();

        Assert.Empty(scan.Outcomes);
        Assert.Empty(scan.Left);
    }

    [Fact]
    public void Anything_else_is_left_where_it_is_and_named_once()
    {
        var note = Path.Combine(Directory.CreateDirectory(Inbox).FullName, "readme.txt");

        File.WriteAllText(note, "x");

        Assert.Equal([note], Scanner.Scan().Left);
        Assert.Empty(Scanner.Scan().Left);
        Assert.True(File.Exists(note));
    }

    [Fact]
    public void A_zip_is_extracted_into_tracks_and_deleted()
    {
        var staging = Path.Combine(_world.Root, "zipsource");
        var first = IntakeWorld.TaggedFlac(staging, "1-01. One.flac", seed: "1");
        var second = IntakeWorld.TaggedFlac(staging, "1-02. Two.flac", seed: "2");
        var zip = Path.Combine(Directory.CreateDirectory(Inbox).FullName, "album.zip");

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(first, "album/1-01. One.flac");
            archive.CreateEntryFromFile(second, "album/1-02. Two.flac");
            archive.CreateEntry("album/cover.jpg");
        }

        var scan = SettleAndScan();

        Assert.Equal(2, scan.Outcomes.OfType<IntakeOutcome.TakenIn>().Count());
        Assert.False(File.Exists(zip));
        Assert.Equal(2, _world.Intake.Read().Count);
        Assert.Empty(
            Directory.EnumerateDirectories(Path.Combine(_world.Paths.ImportsFolder, ".incoming"))
        );
    }

    [Fact]
    public void A_folder_named_for_a_recipe_gives_its_files_that_recipe_and_any_other_folder_does_not()
    {
        IntakeWorld.TaggedFlac(Path.Combine(Inbox, "Instrumental"), "a.flac", seed: "1");
        IntakeWorld.TaggedFlac(Path.Combine(Inbox, "instrumental", "disc 1"), "b.flac", seed: "2");
        IntakeWorld.TaggedFlac(Path.Combine(Inbox, "Some Album"), "c.flac", seed: "3");

        var scan = SettleAndScan();

        var keys = scan
            .Outcomes.OfType<IntakeOutcome.TakenIn>()
            .ToDictionary(taken => taken.Name + taken.TrackKey);
        var plans = _world
            .Instructions.Read()
            .Where(track => track.Plan is not null)
            .Select(track => track.TrackKey)
            .ToHashSet();

        Assert.Equal(3, keys.Count);
        Assert.Equal(2, plans.Count);
        Assert.All(
            _world.Instructions.Read().Where(track => track.Plan is not null),
            track => Assert.Equivalent(StemPlan.InstrumentalRelease, track.Plan)
        );
    }

    [Fact]
    public void An_unreadable_audio_file_is_reported_once_and_left()
    {
        var file = Path.Combine(Directory.CreateDirectory(Inbox).FullName, "broken.flac");

        File.WriteAllText(file, "not audio");

        Assert.IsType<IntakeOutcome.Refused>(Assert.Single(SettleAndScan().Outcomes));
        Assert.Empty(Scanner.Scan().Outcomes);
        Assert.True(File.Exists(file));
    }
}
