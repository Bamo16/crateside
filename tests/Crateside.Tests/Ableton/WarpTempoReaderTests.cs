using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Ableton;

namespace Crateside.Tests.Ableton;

public sealed class WarpTempoReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public WarpTempoReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_saved_default_clip_gives_the_warped_tempo()
    {
        var audio = Sample("sidecar-clicks.asd");

        Assert.Equal(120, new WarpTempoReader().Read(audio)!.Bpm, 4);
    }

    [Fact]
    public void A_starter_warp_gives_no_tempo_since_it_is_only_keytags_guess()
    {
        var audio = Sample("sidecar-clicks.asd");

        Assert.Null(new WarpTempoReader(isStarter: path => path == audio).Read(audio));
    }

    [Fact]
    public void A_sidecar_read_once_is_not_read_again_until_it_changes()
    {
        var audio = Sample("sidecar-clicks.asd");
        var reader = new WarpTempoReader();

        var first = reader.Read(audio);

        Assert.Same(first, reader.Read(audio));

        File.Copy(Fixture("sidecar-clicks-unsaved.asd"), $"{audio}.asd", overwrite: true);

        Assert.Null(reader.Read(audio));
    }

    [Fact]
    public void A_saved_tempo_is_kept_across_a_restart_while_its_sidecar_is_unchanged()
    {
        var audio = Sample("sidecar-clicks.asd");
        var kept = Path.Combine(_root, "tempos.json");
        var before = new WarpTempoReader(kept);

        var tempo = before.Read(audio);
        before.Save();

        // Same length and time, different bytes: only a kept tempo can still answer.
        var sidecar = new FileInfo($"{audio}.asd");
        var written = sidecar.LastWriteTimeUtc;
        File.WriteAllBytes(sidecar.FullName, new byte[sidecar.Length]);
        File.SetLastWriteTimeUtc(sidecar.FullName, written);

        Assert.Equivalent(tempo, new WarpTempoReader(kept).Read(audio), strict: true);
        Assert.Null(new WarpTempoReader().Read(audio));
    }

    [Fact]
    public void A_kept_tempo_is_read_again_once_its_sidecar_changes()
    {
        var audio = Sample("sidecar-clicks.asd");
        var kept = Path.Combine(_root, "tempos.json");
        var before = new WarpTempoReader(kept);

        before.Read(audio);
        before.Save();

        File.Copy(Fixture("sidecar-clicks-unsaved.asd"), $"{audio}.asd", overwrite: true);

        Assert.Null(new WarpTempoReader(kept).Read(audio));
    }

    [Fact]
    public void A_sample_with_no_sidecar_has_no_tempo()
    {
        var audio = Path.Combine(_root, "bare.flac");
        File.Copy(Fixture("silence.flac"), audio);

        Assert.Null(new WarpTempoReader().Read(audio));
    }

    private string Sample(string sidecar)
    {
        var audio = Path.Combine(_root, "sample.flac");
        File.Copy(Fixture("silence.flac"), audio, overwrite: true);
        File.Copy(Fixture(sidecar), $"{audio}.asd", overwrite: true);

        return audio;
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}

/// <summary>ADR 0038's rule against warps the user knows, in the real library.</summary>
public sealed class WarpTempoRealLibraryTests
{
    private static readonly CratesidePaths Paths = new();
    private static readonly WarpTempoReader Reader = new();

    [Theory]
    [InlineData(
        "ReeK, Asatsumei, L4hee - Deity Mode (Instrumental - Full)",
        TempoShape.MostlySteady,
        180.00
    )]
    [InlineData("T & Sugah - TumDaDaDum (Instrumental - Full)", TempoShape.MostlySteady, 174.00)]
    [InlineData(
        "Paul Oakenfold, Asher D - Ready Steady Go (Extended Mix) (Instrumental - Full)",
        TempoShape.Steady,
        128.02
    )]
    [InlineData(
        "Cypress Hill - Hits from the Bong (Instrumental - Full)",
        TempoShape.Drifting,
        92.1
    )]
    [InlineData(
        "Becky Hill, Chase & Status - Disconnect (Instrumental - Full)",
        TempoShape.Steady,
        175.00
    )]
    public void A_warp_the_user_knows_reads_as_they_warped_it(
        string name,
        TempoShape shape,
        double bpm
    )
    {
        var tempo = Read(name);

        Assert.Equal(shape, tempo.Shape);
        Assert.Equal(bpm, tempo.Bpm, 1);
    }

    [Fact]
    public void Deity_Mode_keeps_its_132_section()
    {
        var tempo = Read("ReeK, Asatsumei, L4hee - Deity Mode (Instrumental - Full)");

        Assert.Contains(
            tempo.Stable,
            stable => Math.Abs(stable.Bpm - 132.007) < 0.01 && stable.Bars >= 20
        );
    }

    [Fact]
    public void A_silent_stretch_does_not_pass_for_the_tempo()
    {
        // Without silence left out, a silent stretch at 131.9 outlasts the song's own drifting 170.
        var tempo = Read(
            "Group X - Arabian Rap Sensations - Jonny Poopoopants (Instrumental - Full)"
        );

        Assert.InRange(tempo.Bpm, 165, 175);
    }

    [Fact]
    public void Every_saved_default_clip_in_the_library_has_a_tempo()
    {
        SkipUnlessPresent();

        var missing = Directory
            .EnumerateFiles(Paths.LibraryFolder, "*.asd", SearchOption.AllDirectories)
            .Where(asd =>
                AnalysisFile.TryRead(asd, out var analysis) && analysis is { DefaultClip: not null }
            )
            .Where(asd => Reader.Read(asd[..^4]) is null)
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(missing);
    }

    private static WarpTempo Read(string name)
    {
        SkipUnlessPresent();

        var audio = Path.Combine(Paths.LibraryFolder, $"{name}.flac");
        Assert.SkipUnless(File.Exists(audio), $"{name} is no longer in the library.");

        return Reader.Read(audio) ?? throw new InvalidOperationException($"{name}: no tempo");
    }

    private static void SkipUnlessPresent() =>
        Assert.SkipUnless(
            Directory.Exists(Paths.LibraryFolder),
            $"{Paths.LibraryFolder} not present on this machine."
        );
}
