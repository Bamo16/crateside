using Crateside.Core.Genres;

namespace Crateside.Tests.Genres;

/// <summary>Labels taken from the 2026-09-24 survey of iTunes, Deezer and MusicBrainz (#4).</summary>
public sealed class GenreVocabularyTests
{
    [Theory]
    [InlineData("drum and bass", "Drum & Bass")]
    [InlineData("dancefloor drum and bass", "Drum & Bass")]
    [InlineData("Drum & Bass", "Drum & Bass")]
    [InlineData("Drum 'n' Bass", "Drum & Bass")]
    [InlineData("future bass", "Future Bass")]
    [InlineData("Hip-Hop/Rap", "Hip Hop")]
    [InlineData("Rap/Hip Hop", "Hip Hop")]
    [InlineData("west coast hip hop", "Hip Hop")]
    [InlineData("pop rap", "Hip Hop")]
    [InlineData("J-Pop", "J-Pop")]
    [InlineData("k-pop", "K-Pop")]
    [InlineData("Pop", "Pop")]
    [InlineData("dance-pop", "Pop")]
    [InlineData("synthpop", "Pop")]
    [InlineData("Rock", "Rock")]
    [InlineData("pop rock", "Rock")]
    public void A_label_maps_onto_the_list(string heard, string genre) =>
        Assert.Equal(genre, GenreVocabulary.FromHeard(heard));

    [Theory]
    [InlineData("Dance")]
    [InlineData("Electronic")]
    [InlineData("Electro")]
    [InlineData("breakcore")]
    [InlineData("Singer/Songwriter")]
    [InlineData("Asian Music")]
    [InlineData("Karaoke")]
    [InlineData("trapped")]
    [InlineData("")]
    [InlineData(null)]
    public void A_label_off_the_list_maps_onto_nothing(string? heard) =>
        Assert.Null(GenreVocabulary.FromHeard(heard));

    [Fact]
    public void Every_genre_on_the_list_maps_onto_itself()
    {
        Assert.All(
            GenreVocabulary.All,
            genre => Assert.Equal(genre, GenreVocabulary.FromHeard(genre))
        );
    }
}
