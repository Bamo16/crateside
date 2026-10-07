using Crateside.Core.Orchestration.Handoff;

namespace Crateside.Tests.Orchestration.Handoff;

public sealed class KeyCorrectionsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private static readonly KeyDisagreement Fever = new(
        "M:/Fever (Vocal - Full).flac",
        "Lewis Thompson - Fever",
        "G# major",
        "D# major"
    );

    private static readonly KeyDisagreement Tigerlily = new(
        "M:/Tigerlily (Vocal - Full).flac",
        "La Roux - Tigerlily",
        "C# minor",
        "F# minor"
    );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private KeyCorrections Store() => new(Path.Combine(_root, "key-corrections.json"));

    [Fact]
    public void An_approval_survives_a_restart()
    {
        Store().Approve([Fever]);

        Assert.Equal("D# major", Store().Approved()[Fever.Path]);
    }

    [Fact]
    public void An_approval_is_kept_until_the_disagreement_is_gone()
    {
        var store = Store();
        store.Approve([Fever, Tigerlily]);

        store.Found([Tigerlily]);

        Assert.Equal([Tigerlily.Path], store.Approved().Keys);
    }

    [Fact]
    public void An_approval_overtaken_by_a_new_verdict_is_dropped()
    {
        var store = Store();
        store.Approve([Fever]);

        store.Found([Fever with { Verdict = "C minor" }]);

        Assert.Empty(store.Approved());
    }
}
