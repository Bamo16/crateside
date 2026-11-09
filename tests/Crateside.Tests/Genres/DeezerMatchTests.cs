using System.Text.Json;
using Crateside.Core.Genres;

namespace Crateside.Tests.Genres;

public sealed class DeezerMatchTests
{
    [Theory]
    [InlineData("Party In The USA", "Party in the U.S.A.")]
    [InlineData("Ni**as in Paris", "Niggas in Paris")]
    [InlineData("All The Things She Said (Radio Version)", "All the Things She Said")]
    [InlineData("Ante Up Remix", "Ante Up (Robbin Hoodz Theory)")]
    public void Titles_match_however_deezer_spells_or_censors_them(string found, string wanted) =>
        Assert.True(DeezerMatch.SameTitle(found, wanted));

    [Fact]
    public void A_different_song_is_not_the_same_title() =>
        Assert.False(DeezerMatch.SameTitle("Party In the CIA", "Party in the U.S.A."));

    [Theory]
    [InlineData("Hello", "Ameritz Karaoke", "Hello", true)]
    [InlineData("Hello (Karaoke Version)", "Adele", "Hello", true)]
    [InlineData("Hello (Cover)", "Adele", "Hello", true)]
    [InlineData("Hello (Originally Performed by Adele)", "Studio Band", "Hello", true)]
    [InlineData("Hello", "Adele Tribute Project", "Hello", true)]
    [InlineData("Cover Me", "Bruce Springsteen", "Cover Me", false)]
    [InlineData("Karaoke", "Hanson", "Karaoke", false)]
    [InlineData("Hello", "Adele", "Hello", false)]
    public void Karaoke_tribute_and_cover_uploads_are_recognised(
        string title,
        string artist,
        string wanted,
        bool expected
    ) => Assert.Equal(expected, DeezerMatch.IsImitation(title, artist, wanted));

    [Fact]
    public void A_cover_upload_is_passed_over_for_the_real_artists_track()
    {
        using var search = JsonDocument.Parse(
            """
            {"data":[
              {"id":1,"title":"Hello","rank":107048,"artist":{"name":"Adele Taylor"}},
              {"id":2,"title":"Hello","rank":90000,"artist":{"name":"Hello I'm Adele"}},
              {"id":3,"title":"Hello (Karaoke Version)","rank":500000,"artist":{"name":"Adele"}},
              {"id":4,"title":"Hello","rank":800000,"artist":{"name":"Karaoke Hits"}},
              {"id":5,"title":"Hello","rank":700000,"artist":{"name":"Adele"},"album":{"id":55}}
            ]}
            """
        );

        Assert.Equal(
            (5L, 700_000, (long?)55L),
            DeezerMatch.Match(search.RootElement, "Adele", "Hello")!.Value
        );
    }

    [Fact]
    public void The_most_popular_hit_by_the_artist_outranks_a_remix()
    {
        using var search = JsonDocument.Parse(
            """
            {"data":[
              {"id":1,"title":"American Boy (Remix)","rank":100,"artist":{"name":"Someone Else"}},
              {"id":2,"title":"American Boy","rank":900317,"artist":{"name":"Estelle"}}
            ]}
            """
        );

        Assert.Equal(
            2,
            DeezerMatch.Match(search.RootElement, "Estelle", "American Boy (feat. Kanye West)")?.Id
        );
    }

    [Fact]
    public void Only_cover_uploads_match_nothing()
    {
        using var search = JsonDocument.Parse(
            """{"data":[{"id":1,"title":"Hello","rank":107048,"artist":{"name":"Adele Taylor"}}]}"""
        );

        Assert.Null(DeezerMatch.Match(search.RootElement, "Adele", "Hello"));
    }
}
