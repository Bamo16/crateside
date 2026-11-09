using Crateside.Core;

namespace Crateside.Tests;

public sealed class NameMatchTests
{
    [Theory]
    [InlineData("kesha", "Ke$ha")]
    [InlineData("jay-z, kanye west", "JAŸ-Z")]
    [InlineData("t.a.t.u.", "t.A.T.u.")]
    [InlineData("san-z, hoyo-mix", "Sān-Z")]
    [InlineData("nsync", "*NSYNC")]
    public void Artists_match_however_a_source_spells_them(string credited, string found) =>
        Assert.True(NameMatch.ByOneOf(credited, found));

    [Fact]
    public void A_cover_by_someone_else_is_not_by_one_of_the_artists() =>
        Assert.False(NameMatch.ByOneOf("Bananarama", "Ace of Base"));

    [Theory]
    [InlineData("Adele", "Adele Taylor")]
    [InlineData("Adele", "Hello I'm Adele")]
    [InlineData("Adele", "Adele Tribute Band")]
    [InlineData("Kanye West", "Kanye")]
    public void A_longer_name_containing_the_artist_is_not_the_artist(
        string credited,
        string found
    ) => Assert.False(NameMatch.ByOneOf(credited, found));

    [Theory]
    [InlineData("Polo & Pan", "Polo and Pan")]
    [InlineData("Hall and Oates", "Hall & Oates")]
    [InlineData("Estelle & Kanye West", "Kanye West")]
    [InlineData("Estelle, Kanye West", "Estelle")]
    public void Ampersands_and_credit_splits_still_match(string credited, string found) =>
        Assert.True(NameMatch.ByOneOf(credited, found));

    [Theory]
    [InlineData("Party In The USA", "Party in the U.S.A.", true)]
    [InlineData(
        "Heaven Is a Place on Earth (Extended Version)",
        "Heaven Is A Place On Earth",
        true
    )]
    [InlineData("Hello", "Hello Goodbye", false)]
    [InlineData("Latch", "Latch (Disclosure song)", true)]
    public void A_title_is_the_same_whole_once_squashed(
        string found,
        string wanted,
        bool expected
    ) => Assert.Equal(expected, NameMatch.SameTitle(found, wanted));
}
