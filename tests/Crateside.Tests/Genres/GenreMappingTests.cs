using System.Text.Json;
using Crateside.Core.Genres;

namespace Crateside.Tests.Genres;

public sealed class GenreMappingTests
{
    /// <summary>Labels that describe what a recording is for, or where it is from, not what it sounds like (ADR 0088).</summary>
    internal static readonly string[] NotGenres =
    [
        "asian music",
        "ballad",
        "comedy",
        "film scores",
        "films games",
        "instrumental",
        "soundtrack",
    ];

    [Fact]
    public void Every_label_in_the_committed_snapshot_is_mapped_unless_it_names_no_genre()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "genre-label-counts.json");
        var counts = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path))!;
        var unmapped = counts
            .Where(label => GenreMapping.Map(label.Key).Count is 0)
            .Select(label => label.Key)
            .Order()
            .ToList();

        Assert.Equal([.. NotGenres.Order()], unmapped);
    }

    [Theory]
    [InlineData("drum and bass", "Drum & Bass")]
    [InlineData("dancefloor drum and bass", "Drum & Bass")]
    [InlineData("Drum & Bass", "Drum & Bass")]
    [InlineData("Drum 'n' Bass", "Drum & Bass")]
    [InlineData("Hip-Hop/Rap", "Hip Hop")]
    [InlineData("Rap/Hip Hop", "Hip Hop")]
    [InlineData("west coast hip hop", "Hip Hop")]
    [InlineData("Dance", "EDM")]
    [InlineData("Electronic", "Electronica")]
    [InlineData("future bass", "EDM")]
    [InlineData("Kawaii Future Bass", "EDM")]
    [InlineData("J-Pop", "Pop")]
    [InlineData("k-pop", "Pop")]
    [InlineData("dance-pop", "Pop")]
    [InlineData("synth-pop", "Synthpop")]
    [InlineData("Contemporary R&B", "R&B")]
    public void A_label_maps_onto_a_genre_of_Abletons(string heard, string genre) =>
        Assert.Equal(genre, GenreMapping.Map(heard)[0]);

    [Theory]
    [InlineData("pop rap", new[] { "Pop", "Hip Hop" })]
    [InlineData("R&B/Soul", new[] { "R&B", "Soul" })]
    [InlineData("indie rock", new[] { "Indie", "Rock" })]
    public void A_label_can_map_onto_two(string heard, string[] genres) =>
        Assert.Equal(genres, GenreMapping.Map(heard));

    [Theory]
    [InlineData("Soundtrack")]
    [InlineData("Karaoke")]
    [InlineData("trapped")]
    [InlineData("")]
    public void A_label_the_table_does_not_know_maps_onto_nothing(string heard) =>
        Assert.Empty(GenreMapping.Map(heard));

    [Fact]
    public void Every_genre_of_Abletons_maps_onto_itself() =>
        Assert.All(GenreVocabulary.All, genre => Assert.Equal([genre], GenreMapping.Map(genre)));

    [Fact]
    public void Labels_that_agree_give_one_genre() =>
        Assert.Equal(["Hip Hop"], GenreMapping.Pick(["Hip-Hop/Rap", "hip hop", "rap"]));

    [Fact]
    public void A_second_genre_needs_at_least_half_as_many_labels_as_the_first() =>
        Assert.Equal(["Rock"], GenreMapping.Pick(["rock", "hard rock", "alternative", "pop"]));

    [Fact]
    public void Two_genres_that_labels_split_between_are_both_given_in_the_order_heard() =>
        Assert.Equal(["Pop", "Rock"], GenreMapping.Pick(["pop", "rock", "dance pop", "hard rock"]));

    [Fact]
    public void A_tie_is_settled_by_the_label_heard_first() =>
        Assert.Equal(["Jazz", "Soul"], GenreMapping.Pick(["jazz", "soul", "blues"]));

    [Fact]
    public void Never_more_than_two_are_given() =>
        Assert.Equal(2, GenreMapping.Pick(["pop", "rock", "jazz", "soul", "folk"]).Count);

    [Fact]
    public void Labels_that_map_onto_nothing_give_nothing() =>
        Assert.Empty(GenreMapping.Pick(["soundtrack", "comedy"]));

    [Fact]
    public void The_same_label_in_two_spellings_counts_once() =>
        Assert.Equal(["Pop", "Rock"], GenreMapping.Pick(["Pop", "pop", "rock", "Rock"]));
}
