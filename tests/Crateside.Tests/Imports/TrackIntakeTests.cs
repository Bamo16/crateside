using Crateside.Core;
using Crateside.Core.Imports;
using Crateside.Core.Plans;

namespace Crateside.Tests.Imports;

public sealed class TrackTagsTests
{
    [Theory]
    [InlineData("1-01. GRADUATION ~Every Tomorrow~.flac", "GRADUATION ~Every Tomorrow~")]
    [InlineData("19. Chemical Plant Zone.flac", "Chemical Plant Zone")]
    [InlineData("1-28 - Stickerbush Symphony.flac", "Stickerbush Symphony")]
    [InlineData("99 Luftballons.mp3", "99 Luftballons")]
    [InlineData("7-Eleven.flac", "7-Eleven")]
    [InlineData("Plain Name.ogg", "Plain Name")]
    public void With_no_tags_the_name_is_the_file_without_extension_or_leading_track_number(
        string fileName,
        string expected
    ) => Assert.Equal(expected, TrackTags.NameFor(null, fileName));

    [Fact]
    public void Several_artists_are_joined_in_the_name()
    {
        var tags = new TrackTags { Artists = ["A", "B"], Title = "Song" };

        Assert.Equal("A, B - Song", TrackTags.NameFor(tags, "x.flac"));
    }

    [Fact]
    public void A_title_without_an_artist_is_the_name()
    {
        Assert.Equal("Song", TrackTags.NameFor(new TrackTags { Title = "Song" }, "x.flac"));
    }

    [Theory]
    [InlineData("2003", 2003)]
    [InlineData("March 9, 1996", 1996)]
    [InlineData("2020-05-01", 2020)]
    [InlineData("12345", null)]
    [InlineData("no year", null)]
    [InlineData(null, null)]
    public void The_year_is_the_first_four_digit_run_in_the_date(string? date, int? expected) =>
        Assert.Equal(expected, TrackTags.FirstYear(date));

    [Fact]
    public void Every_tag_a_gamerip_carries_is_read()
    {
        using var world = new IntakeWorld();
        var path = world.TaggedFlac(world.Root, "1-01. GRADUATION.flac");

        var tags = TrackTags.TryRead(path)!;

        Assert.Equal(["Be For U"], tags.Artists);
        Assert.Equal("GRADUATION", tags.Title);
        Assert.Equal("DDR EXTREME", tags.Release.Album);
        Assert.Equal("Konami", tags.Release.AlbumArtist);
        Assert.Equal("Naoki Maeda", tags.Release.Composer);
        Assert.Equal("Konami, Rare", tags.Release.Organization);
        Assert.Equal("KMCA-123", tags.Release.Ost);
        Assert.Equal("Game Soundtrack", tags.Release.Genre);
        Assert.Equal(
            (1, 37, 1, 2),
            (tags.Release.Track, tags.Release.TrackCount, tags.Release.Disc, tags.Release.DiscCount)
        );
        Assert.Equal(2003, tags.Release.Year);
        Assert.Equal("March 9, 2003", tags.Release.YearText);
        Assert.Equal(YearKind.Release, tags.Release.YearKind);
        Assert.Equal("image/png", tags.CoverMimeType);
    }

    [Fact]
    public void A_file_that_is_not_audio_reads_as_nothing()
    {
        using var world = new IntakeWorld();
        var path = Path.Combine(world.Root, "notes.flac");

        File.WriteAllText(path, "not audio");

        Assert.Null(TrackTags.TryRead(path));
    }
}

public sealed class TrackIntakeTests
{
    [Fact]
    public void A_tagged_file_becomes_a_track_named_from_its_tags_with_its_release_kept()
    {
        using var world = new IntakeWorld();
        var path = world.TaggedFlac(world.Root, "1-01. GRADUATION.flac");

        var outcome = Assert.IsType<IntakeOutcome.TakenIn>(
            world.Files.TakeInFile(path, IntakeMode.Copy)
        );

        Assert.Equal("Be For U - GRADUATION", outcome.Name);
        Assert.StartsWith("file~", outcome.TrackKey);

        var record = Assert.Single(world.Intake.Read());

        Assert.Equal(outcome.TrackKey, record.TrackKey);
        Assert.Equal("Be For U - GRADUATION", record.Title);
        Assert.Equal(world.Time.GetUtcNow(), record.AddedAt);
        Assert.Equal("DDR EXTREME", record.Release?.Album);
        Assert.Equal(2003, record.Release?.Year);
        Assert.Equal(YearKind.Release, record.Release?.YearKind);

        Assert.Equal("Be For U - GRADUATION", world.Names.Read()[outcome.TrackKey].DisplayTitle);
        Assert.Equal("1-01. GRADUATION", world.Names.Read()[outcome.TrackKey].BaseName);
        Assert.True(world.Imports.TryGetFile(outcome.TrackKey, out var stored));
        Assert.Equal("1-01. GRADUATION.flac", Path.GetFileName(stored));
    }

    [Fact]
    public void The_cover_is_kept_once_beside_the_working_set()
    {
        using var world = new IntakeWorld();
        var outcome = Assert.IsType<IntakeOutcome.TakenIn>(
            world.Files.TakeInFile(world.TaggedFlac(world.Root, "a.flac"), IntakeMode.Copy)
        );

        var cover = world.Intake.Read()[0].Release?.Cover;

        Assert.Equal($"{outcome.TrackKey}.png", cover);
        Assert.Equal(
            [1, 2, 3, 4],
            File.ReadAllBytes(Path.Combine(world.Paths.CoversFolder, cover!))
        );
    }

    [Fact]
    public void Dropping_copies_and_the_inbox_consumes()
    {
        using var world = new IntakeWorld();
        var dropped = world.TaggedFlac(world.Root, "dropped.flac", seed: "1");
        var inboxed = world.TaggedFlac(world.Root, "inboxed.flac", seed: "2");

        world.Files.TakeInFile(dropped, IntakeMode.Copy);
        world.Files.TakeInFile(inboxed, IntakeMode.Move);

        Assert.True(File.Exists(dropped));
        Assert.False(File.Exists(inboxed));
    }

    [Fact]
    public void The_same_file_twice_is_one_track_and_the_inbox_copy_is_consumed()
    {
        using var world = new IntakeWorld();
        var first = world.TaggedFlac(world.Root, "first.flac");
        var again = world.TaggedFlac(Path.Combine(world.Root, "elsewhere"), "again.flac");

        world.Files.TakeInFile(first, IntakeMode.Copy);
        var outcome = world.Files.TakeInFile(again, IntakeMode.Move);

        Assert.IsType<IntakeOutcome.AlreadyHeld>(outcome);
        Assert.Single(world.Intake.Read());
        Assert.False(File.Exists(again));
    }

    [Fact]
    public void A_file_that_is_not_audio_is_refused_and_left_alone()
    {
        using var world = new IntakeWorld();
        var path = Path.Combine(world.Root, "notes.flac");

        File.WriteAllText(path, "not audio");

        Assert.IsType<IntakeOutcome.Refused>(world.Files.TakeInFile(path, IntakeMode.Move));
        Assert.True(File.Exists(path));
        Assert.Empty(world.Intake.Read());
    }

    [Fact]
    public void The_default_recipe_is_standard_and_leaves_the_plan_unsaid()
    {
        using var world = new IntakeWorld();
        var outcome = Assert.IsType<IntakeOutcome.TakenIn>(
            world.Files.TakeInFile(world.TaggedFlac(world.Root, "a.flac"), IntakeMode.Copy)
        );

        Assert.DoesNotContain(
            world.Instructions.Read(),
            track => track.TrackKey == outcome.TrackKey && track.Plan is not null
        );
    }

    [Fact]
    public void A_recipe_sets_the_plan_at_intake_and_the_settings_default_is_what_none_says()
    {
        using var world = new IntakeWorld();
        var named = Assert.IsType<IntakeOutcome.TakenIn>(
            world.Files.TakeInFile(
                world.TaggedFlac(world.Root, "a.flac", seed: "1"),
                IntakeMode.Copy,
                Recipe.Instrumental
            )
        );

        world.Recipes.Choose(Recipe.Instrumental);

        var defaulted = Assert.IsType<IntakeOutcome.TakenIn>(
            world.Files.TakeInFile(
                world.TaggedFlac(world.Root, "b.flac", seed: "2"),
                IntakeMode.Copy
            )
        );

        foreach (var key in new[] { named.TrackKey, defaulted.TrackKey })
            Assert.Equivalent(
                StemPlan.InstrumentalRelease,
                world.Instructions.Read().Single(track => track.TrackKey == key).Plan
            );
    }

    [Fact]
    public void The_recipe_choice_is_kept()
    {
        using var world = new IntakeWorld();

        Assert.Equal(Recipe.Standard, world.Recipes.Default);

        world.Recipes.Choose(Recipe.Instrumental);

        Assert.Equal(Recipe.Instrumental, new RecipeStore(world.Paths).Default);
    }

    [Fact]
    public void A_site_track_with_no_release_year_keeps_its_upload_date_as_an_upload()
    {
        using var world = new IntakeWorld();
        var audio = world.TaggedFlac(Path.Combine(world.Root, "working", "soundcloud~1"), "t.flac");

        using (var file = TagLib.File.Create(audio))
        {
            file.Tag.Performers = [];
            ((TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph, true)).RemoveField("DATE");
            file.Tag.Title = "Some Track";
            file.Save();
        }

        var outcome = world.Files.TakeInSiteTrack(
            "soundcloud~1",
            audio,
            "https://soundcloud.com/a/b",
            new SiteFacts { Uploader = "djfoo", UploadDate = "20160601" }
        );

        Assert.Equal("djfoo - Some Track", Assert.IsType<IntakeOutcome.TakenIn>(outcome).Name);

        var record = world.Intake.Read().Single();

        Assert.Equal("https://soundcloud.com/a/b", record.PageUrl);
        Assert.Equal(2016, record.Release?.Year);
        Assert.Equal(YearKind.Upload, record.Release?.YearKind);
        Assert.Null(record.Release?.ReleaseYear);
    }

    [Fact]
    public void A_work_item_for_a_key_that_is_not_a_video_is_fetched_from_its_page_or_not_at_all()
    {
        using var world = new IntakeWorld();
        var local = new Crateside.Core.WorkLists.WorkItem { TrackKey = "file~aa", Title = "x" };
        var site = local with { TrackKey = "soundcloud~1", PageUrl = "https://soundcloud.com/a/b" };
        var youtube = local with { TrackKey = "dQw4w9WgXcQ" };

        Assert.False(local.CanBeFetched);
        Assert.True(site.CanBeFetched);
        Assert.Equal("https://soundcloud.com/a/b", site.WatchUrl);
        Assert.True(youtube.CanBeFetched);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", youtube.WatchUrl);
    }
}
