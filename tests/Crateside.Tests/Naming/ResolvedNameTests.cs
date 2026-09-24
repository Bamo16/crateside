using Crateside.Core.Naming;

namespace Crateside.Tests.Naming;

/// <summary>The names StemForge resolves, and the store that keeps them between runs.</summary>
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

    /// <summary>Reading <c>artist</c> or <c>title</c> is the first step to composing a name (ADR 0014).</summary>
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
