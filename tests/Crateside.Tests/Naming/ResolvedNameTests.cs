using Crateside.Core.Naming;

namespace Crateside.Tests.Naming;

/// <summary>
/// The names StemForge resolves, and the store that keeps them between runs.
/// </summary>
/// <remarks>
/// The resolver itself shells out to <c>stemforge-cli</c>, so what is asserted here is the store
/// and the shape of what a resolve produces. The rule the resolver exists to enforce — that
/// crateside never composes a name from <c>artist</c> and <c>title</c> — is asserted by
/// <see cref="Nothing_in_crateside_composes_a_display_name"/>.
/// </remarks>
public class ResolvedNameTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public ResolvedNameTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void An_unwritten_store_resolves_nothing() => Assert.Empty(Store().Read());

    [Fact]
    public void A_saved_name_survives_a_new_store_over_the_same_file()
    {
        Store().Save("aaaaaaaaaaa", Name("Diana Ross - Upside Down"));

        var reread = Store().Read();

        Assert.Equal("Diana Ross - Upside Down", reread["aaaaaaaaaaa"].DisplayTitle);
    }

    [Fact]
    public void A_later_save_replaces_an_earlier_one()
    {
        var store = Store();

        store.Save("aaaaaaaaaaa", Name("first"));
        store.Save("aaaaaaaaaaa", Name("second"));

        Assert.Equal("second", Store().Read()["aaaaaaaaaaa"].DisplayTitle);
    }

    [Fact]
    public void A_corrupt_store_reads_as_nothing_resolved_rather_than_throwing()
    {
        File.WriteAllText(Path.Combine(_root, "names.json"), "{ not json");

        Assert.Empty(Store().Read());
    }

    /// <summary>
    /// crateside cannot rebuild a display name from its parts because it never reads the parts:
    /// StemForge's row carries <c>artist</c> and <c>title</c> and this deserializes neither.
    /// </summary>
    /// <remarks>
    /// A display name collapses credits YouTube Music repeats per role and drops one the title
    /// already carries, so it is deliberately lossier than <c>{artist} - {title}</c> and
    /// composing it locally reproduces neither field. Adding either property here is the first
    /// move anyone would make towards doing that (ADR 0014).
    /// </remarks>
    [Fact]
    public void The_resolver_never_reads_artist_or_title_from_stemforge()
    {
        var read = typeof(FormatListOutput).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("Artist", read);
        Assert.DoesNotContain("Title", read);
        Assert.Contains("DisplayTitle", read);
    }

    private ResolvedNameStore Store() => new(Path.Combine(_root, "names.json"));

    private static ResolvedName Name(string display) =>
        new() { DisplayTitle = display, BaseName = display };
}
