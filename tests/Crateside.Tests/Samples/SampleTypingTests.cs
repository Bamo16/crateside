using Crateside.Core.Samples;

namespace Crateside.Tests.Samples;

/// <summary>Writing an added source's type into the provenance it already carries (ADR 0028).</summary>
public sealed class SampleTypingTests : IDisposable
{
    private const string Downloaded =
        "stemforge/0.3.1.0 | date: 2026-09-18 | source: https://www.youtube.com/watch?v=C_DIhh3sWO0";

    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n"),
        "Lil Wayne - A Milli (A Cappella).flac"
    );

    public SampleTypingTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), _path);
    }

    public void Dispose()
    {
        Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void Comment(string? comment)
    {
        using var file = TagLib.File.Create(_path);
        file.Tag.Comment = comment;
        file.Save();
    }

    private StemProvenance? ReadBack() => StemTagReader.Read(_path)?.Provenance;

    [Fact]
    public void The_type_is_appended_and_everything_already_there_survives()
    {
        Comment(Downloaded);

        Assert.True(SampleTyping.TryWrite(_path, SampleType.Vocal, out _));

        var provenance = ReadBack();
        Assert.Equal(SampleType.Vocal, provenance?.Type);
        Assert.Equal("C_DIhh3sWO0", provenance?.SourceTrackKey);
        Assert.Null(provenance?.Preset);
    }

    [Fact]
    public void Writing_the_same_type_again_changes_nothing()
    {
        Comment(Downloaded);
        SampleTyping.TryWrite(_path, SampleType.Vocal, out _);

        Assert.True(SampleTyping.TryWrite(_path, SampleType.Vocal, out _));

        using var file = TagLib.File.Create(_path);
        Assert.Equal($"{Downloaded} | type: Vocal", file.Tag.Comment);
    }

    [Fact]
    public void A_file_already_typed_otherwise_is_refused()
    {
        Comment(Downloaded);
        SampleTyping.TryWrite(_path, SampleType.Vocal, out _);

        Assert.False(SampleTyping.TryWrite(_path, SampleType.Instrumental, out var problem));
        Assert.NotNull(problem);
        Assert.Equal(SampleType.Vocal, ReadBack()?.Type);
    }

    [Fact]
    public void A_file_with_no_provenance_is_refused_rather_than_given_some()
    {
        Comment("Ripped from vinyl, 2004");

        Assert.False(SampleTyping.TryWrite(_path, SampleType.Vocal, out _));
    }

    [Fact]
    public void A_stem_is_refused_because_its_preset_already_says_what_it_is()
    {
        Comment("stemforge/0.3.1.0 | preset: Vocal - Full | date: 2026-09-18");

        Assert.False(SampleTyping.TryWrite(_path, SampleType.Instrumental, out _));
    }
}
