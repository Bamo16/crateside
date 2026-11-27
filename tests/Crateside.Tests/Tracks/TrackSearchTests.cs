using Crateside.Core.Familiarity;
using Crateside.Core.Ledger;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>What counts as a match, and what the familiarity filter keeps.</summary>
public sealed class TrackSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public TrackSearchTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly TrackRow CrazyInLove = new()
    {
        TrackKey = "T6xbOPSUe8M",
        Title = "Beyoncé, JAŸ-Z - Crazy In Love",
    };

    private static readonly TrackRow Huey = new()
    {
        TrackKey = "zOmccz4mCBQ",
        Title = "Huey - Pop, Lock & Drop It",
        TagArtist = "Huey",
        TagTitle = "Pop, Lock & Drop It",
    };

    private static readonly TrackRow Scattered = new()
    {
        TrackKey = "sssssssssss",
        Title = "Love Is Crazy Sometimes In My Head",
    };

    private string Tagged(string name, string title)
    {
        var path = Path.Combine(_root, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Title = title;
        file.Save();

        return path;
    }

    private TrackRow WithUpload(string title) =>
        new()
        {
            TrackKey = "mainmainmai",
            Title = "Artist - Song",
            Samples =
            [
                new TrackSample
                {
                    Path = Tagged("up.flac", title),
                    Label = "source",
                    SourceTrackKey = "uuuuuuuuuuu",
                },
            ],
        };

    [Theory]
    [InlineData("jay z")]
    [InlineData("jay-z")]
    [InlineData("jayz")]
    [InlineData("JAŸ-Z")]
    public void An_accented_name_is_found_without_the_accent_or_the_punctuation(string search) =>
        Assert.True(CrazyInLove.Matches(search));

    [Fact]
    public void An_apostrophe_is_deleted_not_made_a_space() =>
        Assert.True(
            new TrackRow { TrackKey = "iiiiiiiiiii", Title = "Pharrell - I’m Back" }.Matches("im")
        );

    [Theory]
    [InlineData("poplock")]
    [InlineData("pop lock")]
    [InlineData("lock pop")]
    public void Spaces_do_not_matter_between_the_words_of_a_name(string search) =>
        Assert.True(Huey.Matches(search));

    [Fact]
    public void Words_may_come_in_any_order_and_from_different_names()
    {
        var row = new TrackRow
        {
            TrackKey = "aaaaaaaaaaa",
            Title = "Huey - Pop, Lock & Drop It",
            TagArtist = "Someone Else",
        };

        Assert.True(row.Matches("huey lock"));
        Assert.True(row.Matches("someone drop"));
        Assert.False(row.Matches("huey nonesuch"));
    }

    [Fact]
    public void Words_are_found_across_the_verdict_and_the_tag_names()
    {
        var row = Huey with
        {
            Verdict = new LedgerRun
            {
                Timestamp = DateTimeOffset.UnixEpoch,
                Song = "Huey - Pop, Lock & Drop It",
                Artist = "Huey Lewis",
                Title = "Pop Lock",
            },
            TagTitle = "Soulja",
        };

        Assert.True(row.Matches("lewis soulja"));
    }

    [Fact]
    public void An_upload_title_answers_the_search()
    {
        var row = WithUpload("Song (Instrumental Mix)");

        Assert.True(row.Matches("instrumental"));
    }

    [Theory]
    [InlineData("zOmccz4mCBQ")]
    [InlineData("https://www.youtube.com/watch?v=zOmccz4mCBQ")]
    [InlineData("https://music.youtube.com/watch?v=zOmccz4mCBQ&list=x")]
    public void A_video_id_bare_or_in_a_link_finds_the_track_first(string search)
    {
        var result = new[] { CrazyInLove, Scattered, Huey }.Search(search);

        Assert.Equal([Huey], result.Rows);
        Assert.False(result.NearMatches);
    }

    [Fact]
    public void A_video_id_of_an_upload_finds_the_track()
    {
        var row = WithUpload("Song");

        Assert.True(row.Matches("uuuuuuuuuuu"));
    }

    [Fact]
    public void A_video_id_in_the_wrong_case_does_not_match()
    {
        Assert.False(Huey.Matches("zomccz4mcbq"));
        Assert.Empty(new[] { CrazyInLove, Huey }.Search("zomccz4mcbq").Rows);
    }

    [Fact]
    public void A_phrase_hit_sorts_ahead_of_a_row_with_the_words_scattered()
    {
        var result = new[] { Scattered, CrazyInLove }.Search("crazy in love");

        Assert.Equal([CrazyInLove, Scattered], result.Rows);
        Assert.False(result.NearMatches);
    }

    [Fact]
    public void The_video_id_hit_comes_before_a_phrase_hit()
    {
        var named = new TrackRow { TrackKey = "nnnnnnnnnnn", Title = "zOmccz4mCBQ" };

        Assert.Equal([Huey, named], new[] { named, Huey }.Search("zOmccz4mCBQ").Rows);
    }

    [Fact]
    public void Near_matches_appear_only_when_nothing_matches_exactly()
    {
        var typo = new[] { CrazyInLove, Huey }.Search("pop lokc");
        var exact = new[] { CrazyInLove, Huey }.Search("pop lock");

        Assert.True(typo.NearMatches);
        Assert.Equal([Huey], typo.Rows);
        Assert.False(exact.NearMatches);
        Assert.Equal([Huey], exact.Rows);
    }

    [Fact]
    public void Upload_titles_are_left_out_of_near_matches()
    {
        var row = WithUpload("Official Video Instrumental");

        Assert.Empty(new[] { row }.Search("oficial vidoe").Rows);
        Assert.Empty(new[] { row }.Search("instrumntal").Rows);
    }

    [Fact]
    public void A_key_is_no_longer_searched()
    {
        Assert.False(Finished.Matches("F# minor"));
        Assert.False(Finished.Matches("minor"));
    }

    [Fact]
    public void Nothing_found_when_the_search_folds_to_nothing() =>
        Assert.Empty(All.Search("!!!").Rows);

    private static readonly TrackRow Staged = new()
    {
        TrackKey = "aaaaaaaaaaa",
        Title = "Ludacris, Pharrell - Southern Hospitality",
        Samples = [new TrackSample { Label = "Vocal - Full", AbletonKey = "B major" }],
    };

    private static readonly TrackRow Finished = new()
    {
        TrackKey = "bbbbbbbbbbb",
        Title = "TLC - No Scrubs",
        Verdict = new LedgerRun
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Song = "TLC - No Scrubs",
            Center = new TonalCenter("F#", DiatonicMode.Aeolian),
        },
        Samples = [new TrackSample { Label = "Vocal - Full", IsInLibrary = true }],
    };

    private static readonly TrackRow Renamed = new()
    {
        TrackKey = "ccccccccccc",
        Title = "topic - Provided to YouTube",
        DerivedTitle = "Diana Ross - Upside Down",
        Samples = [new TrackSample { Label = "Vocal - Full", IsInLibrary = true }],
    };

    private static readonly TrackRow[] All = [Staged, Finished, Renamed];

    [Theory]
    [InlineData("southern")]
    [InlineData("SOUTHERN")]
    [InlineData("  southern  ")]
    [InlineData("Ludacris, Pharrell")]
    public void A_track_is_found_by_part_of_its_name(string search) =>
        Assert.True(Staged.Matches(search));

    [Fact]
    public void An_empty_search_matches_everything()
    {
        Assert.All(All, row => Assert.True(row.Matches(string.Empty)));
        Assert.All(All, row => Assert.True(row.Matches("   ")));
        Assert.All(All, row => Assert.True(row.Matches(null)));
    }

    [Fact]
    public void A_renamed_track_answers_to_both_its_names()
    {
        Assert.True(Renamed.Matches("Upside Down"));
        Assert.True(Renamed.Matches("Provided to YouTube"));
    }

    [Fact]
    public void A_track_is_found_by_its_video_id() => Assert.True(Finished.Matches("bbbbbbbbbbb"));

    [Fact]
    public void Nothing_matches_a_term_no_row_carries() =>
        Assert.All(All, row => Assert.False(row.Matches("nonesuch")));

    [Fact]
    public void Searching_does_not_change_what_the_pills_count()
    {
        var before = All.Count(TrackFilter.All);

        var shown = All.Where(row => row.Matches("southern")).ToArray();

        Assert.Single(shown);
        Assert.Equal(before, All.Count(TrackFilter.All));
    }

    [Fact]
    public void The_familiarity_filter_keeps_only_the_rows_settled_that_way()
    {
        var mainstream = Finished with
        {
            Familiarity = new FamiliarityRecord
            {
                Song = "TLC - No Scrubs",
                Settled = "Mainstream",
            },
        };

        Assert.Equal(
            [mainstream],
            new[] { mainstream, Staged }.Where(row => row.HasFamiliarity("Mainstream"))
        );
        Assert.DoesNotContain(
            mainstream,
            new[] { mainstream, Staged }.Where(row => row.HasFamiliarity("Obscure"))
        );
        Assert.Equal(2, new[] { mainstream, Staged }.Count(row => row.HasFamiliarity(null)));
    }
}
