using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

/// <summary>
/// Undoing text that was written through a Windows-1252 misreading of its own UTF-8 bytes.
/// </summary>
/// <remarks>
/// The damage is in keytag's ledger rather than in the reading of it, and every damaged line
/// predates <c>a40bb92</c>. The file is append-only and not crateside's, so the repair happens on
/// the way to the screen — which makes "leaves undamaged text exactly alone" the property that
/// actually matters here.
/// </remarks>
public class LedgerTextTests
{
    [Theory]
    // What 19 lines of the real ledger hold: an arrow, and a right single quote.
    [InlineData("vâ†’i and IIIâ†’i both land there", "v→i and III→i both land there")]
    [InlineData("the evidence BPM â€” no octave", "the evidence BPM — no octave")]
    [InlineData("Câ€™s cadence", "C’s cadence")]
    public void Text_mangled_through_Windows_1252_is_put_back(string damaged, string intended) =>
        Assert.Equal(intended, LedgerText.Repaired(damaged));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("librosa and ffmpeg fully agree on F minor")]
    [InlineData("Cm->Fm cadences (v->i in natural minor)")]
    public void Plain_text_comes_back_untouched(string? text) =>
        Assert.Equal(text, LedgerText.Repaired(text));

    [Theory]
    // Accented Latin: representable in Windows-1252, so the first half succeeds — and 0xE9 alone
    // is not valid UTF-8, so the second half refuses and the original stands.
    [InlineData("café society")]
    [InlineData("Sigur Rós")]
    [InlineData("Beyoncé")]
    // Nothing outside Windows-1252 can have come from this mistake at all.
    [InlineData("星街すいせい - Stellar Stellar")]
    [InlineData("thørns - honestly (feat. 重音テト)")]
    [InlineData("→ already correct ←")]
    public void Text_that_was_never_damaged_is_never_changed(string text) =>
        Assert.Equal(text, LedgerText.Repaired(text));

    [Fact]
    public void Repairing_twice_changes_nothing_the_second_time()
    {
        // The output has to be a fixed point, or a second pass over an already-repaired string
        // would keep eating it.
        var once = LedgerText.Repaired("vâ†’i resolves");

        Assert.Equal(once, LedgerText.Repaired(once));
    }

    [Fact]
    public void A_replacement_character_is_not_mistaken_for_an_undefined_slot()
    {
        // U+FFFD sits in the lookup table for the five positions Windows-1252 leaves undefined.
        // Matching a real one against those would invent a byte that was never there.
        Assert.Equal("half a character: �", LedgerText.Repaired("half a character: �"));
    }
}
