using Crateside.Core;
using Crateside.Core.Beats;

namespace Crateside.Tests.Beats;

public sealed class KnownBuildsTests : IDisposable
{
    private const string HarnessBuilds = @"V:\Repos\beatdetect-harness\builds";

    private readonly string _folder = Directory.CreateTempSubdirectory("crateside-builds").FullName;

    private static string Newest => Path.Combine(HarnessBuilds, "1.3.0-1e57fec", "ffmpeg.exe");

    private static string Older => Path.Combine(HarnessBuilds, "1.2.0-563e4e4", "ffmpeg.exe");

    private string StatePath => Path.Combine(_folder, "builds.json");

    private CratesidePaths Paths(string? seed = null) =>
        new() { KnownBuildsFile = StatePath, BeatdetectFfmpeg = seed ?? string.Empty };

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task A_path_that_is_not_ffmpeg_is_refused_and_nothing_is_kept()
    {
        var builds = new KnownBuilds(Paths());

        var outcome = await builds.AddAsync(
            Path.Combine(_folder, "nothing.exe"),
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BuildAddOutcome.NotFfmpeg>(outcome);
        Assert.Empty(builds.All);
        Assert.Null(builds.Pinned);
    }

    [Fact]
    public async Task The_first_build_added_is_pinned_and_a_second_is_known_but_not_pinned()
    {
        if (!SkipUnlessHarness())
            return;

        var builds = new KnownBuilds(Paths());

        await builds.AddAsync(Newest, TestContext.Current.CancellationToken);
        await builds.AddAsync(Older, TestContext.Current.CancellationToken);

        Assert.Equal(2, builds.All.Count);
        Assert.Equal("1.3.0-1e57fec", builds.Pinned?.Build.Label);
    }

    [Fact]
    public async Task Adding_the_same_executable_twice_keeps_one_entry()
    {
        if (!SkipUnlessHarness())
            return;

        var builds = new KnownBuilds(Paths());

        await builds.AddAsync(Newest, TestContext.Current.CancellationToken);
        var again = await builds.AddAsync(Newest, TestContext.Current.CancellationToken);

        Assert.IsType<BuildAddOutcome.AlreadyKnown>(again);
        Assert.Single(builds.All);
    }

    [Fact]
    public async Task The_pin_moves_to_another_known_build_and_survives_a_restart()
    {
        if (!SkipUnlessHarness())
            return;

        var builds = new KnownBuilds(Paths());
        await builds.AddAsync(Newest, TestContext.Current.CancellationToken);
        var older = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(Older, TestContext.Current.CancellationToken)
        );

        Assert.True(builds.Pin(older.Known.Build.Sha256));

        var restarted = new KnownBuilds(Paths());

        Assert.Equal(2, restarted.All.Count);
        Assert.Equal(older.Known.Build.Label, restarted.Pinned?.Build.Label);
    }

    [Fact]
    public async Task The_pinned_build_cannot_be_removed_and_an_unknown_one_cannot_be_pinned()
    {
        if (!SkipUnlessHarness())
            return;

        var builds = new KnownBuilds(Paths());
        var added = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(Newest, TestContext.Current.CancellationToken)
        );

        Assert.False(builds.Remove(added.Known.Build.Sha256));
        Assert.False(builds.Pin("0000"));
        Assert.Single(builds.All);
    }

    [Fact]
    public async Task A_machine_that_knows_no_build_starts_on_the_seed_and_pins_it()
    {
        if (!SkipUnlessHarness())
            return;

        var builds = new KnownBuilds(Paths(Newest));

        await builds.SeedAsync(TestContext.Current.CancellationToken);
        await builds.SeedAsync(TestContext.Current.CancellationToken);

        Assert.Single(builds.All);
        Assert.Equal("1.3.0-1e57fec", builds.Pinned?.Build.Label);
    }

    [Fact]
    public async Task A_reading_made_before_the_pin_moved_stays_visible_and_is_marked_older()
    {
        if (!SkipUnlessHarness())
            return;

        var audio = Path.Combine(_folder, "tone.wav");
        WriteSilence(audio);
        var builds = new KnownBuilds(Paths());
        await builds.AddAsync(Older, TestContext.Current.CancellationToken);
        var newer = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(Newest, TestContext.Current.CancellationToken)
        );
        var reader = new BeatReader(Path.Combine(_folder, "beats.json"), builds: builds);

        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);
        await reader.MeasureAsync(audio, TestContext.Current.CancellationToken);

        Assert.False(reader.IsFromOlderBuild(audio));

        builds.Pin(newer.Known.Build.Sha256);
        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(reader.Read(audio));
        Assert.True(reader.IsFromOlderBuild(audio));
        Assert.Equal("1.2.0-563e4e4", reader.BuildOf(audio)?.Label);
    }

    [Fact]
    public async Task A_reading_from_an_older_build_is_read_again_after_those_with_none()
    {
        if (!SkipUnlessHarness())
            return;

        var (old, fresh) = (Path.Combine(_folder, "old.wav"), Path.Combine(_folder, "fresh.wav"));
        WriteSilence(old);
        WriteSilence(fresh);
        var builds = new KnownBuilds(Paths());
        await builds.AddAsync(Older, TestContext.Current.CancellationToken);
        var newer = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(Newest, TestContext.Current.CancellationToken)
        );
        var reader = new BeatReader(Path.Combine(_folder, "beats.json"), builds: builds);
        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);
        await reader.MeasureAsync(old, TestContext.Current.CancellationToken);

        builds.Pin(newer.Known.Build.Sha256);
        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal([fresh, old], reader.ToRead([old, fresh]));

        await reader.MeasureAsync(old, TestContext.Current.CancellationToken);

        Assert.Equal([fresh], reader.ToRead([old, fresh]));
        Assert.Equal("1.3.0-1e57fec", reader.BuildOf(old)?.Label);
    }

    [Fact]
    public async Task A_reading_from_a_newer_build_than_the_pin_is_kept()
    {
        if (!SkipUnlessHarness())
            return;

        var audio = Path.Combine(_folder, "tone.wav");
        WriteSilence(audio);
        var builds = new KnownBuilds(Paths());
        var older = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(Older, TestContext.Current.CancellationToken)
        );
        var newer = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(Newest, TestContext.Current.CancellationToken)
        );
        builds.Pin(newer.Known.Build.Sha256);
        var reader = new BeatReader(Path.Combine(_folder, "beats.json"), builds: builds);
        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);
        await reader.MeasureAsync(audio, TestContext.Current.CancellationToken);

        builds.Pin(older.Known.Build.Sha256);
        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);

        Assert.Empty(reader.ToRead([audio]));
    }

    private static bool SkipUnlessHarness()
    {
        if (File.Exists(Newest) && File.Exists(Older))
            return true;

        Assert.Skip("The harness builds are not on this machine");

        return false;
    }

    private static void WriteSilence(string path)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        const int samples = 44100;

        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(44100);
        writer.Write(88200);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        writer.Write(new byte[samples * 2]);
    }
}
