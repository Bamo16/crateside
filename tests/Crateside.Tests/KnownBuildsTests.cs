using Crateside.Core;
using Crateside.Core.Beats;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests;

public sealed class KnownBuildsTests : IDisposable
{
    private const string HarnessBuilds = @"V:\Repos\beatdetect-harness\builds";

    private readonly string _folder = Directory.CreateTempSubdirectory("crateside-builds").FullName;

    private static string Newest => Path.Combine(HarnessBuilds, "1.3.0-1e57fec", "ffmpeg.exe");

    private static string Older => Path.Combine(HarnessBuilds, "1.2.0-563e4e4", "ffmpeg.exe");

    private string StatePath => Path.Combine(_folder, "builds.json");

    private const string KeydetectBuild =
        @"V:\Repos\keydetect-harness\builds\ffmpeg-keydetect-1.2.1.exe";

    private CratesidePaths Paths(string? seed = null, string? keydetectSeed = null) =>
        new()
        {
            KnownBuildsFile = StatePath,
            BeatdetectFfmpeg = seed ?? string.Empty,
            KeydetectFfmpeg = keydetectSeed ?? string.Empty,
        };

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task A_path_that_is_not_ffmpeg_is_refused_and_nothing_is_kept()
    {
        var builds = new KnownBuilds(new ProcessRunner(), Paths());

        var outcome = await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            Path.Combine(_folder, "nothing.exe"),
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BuildAddOutcome.NotFfmpeg>(outcome);
        Assert.Empty(builds.All(FfmpegFilter.Beatdetect));
        Assert.Null(builds.Pinned(FfmpegFilter.Beatdetect));
    }

    [Fact]
    public async Task The_first_build_added_is_pinned_and_a_second_is_known_but_not_pinned()
    {
        if (!SkipUnlessHarness())
            return;

        var builds = new KnownBuilds(new ProcessRunner(), Paths());

        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            Newest,
            TestContext.Current.CancellationToken
        );
        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            Older,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, builds.All(FfmpegFilter.Beatdetect).Count);
        Assert.Equal("1.3.0-1e57fec", builds.Pinned(FfmpegFilter.Beatdetect)?.Build.Label);
    }

    [Fact]
    public async Task Adding_the_same_executable_twice_keeps_one_entry()
    {
        if (!SkipUnlessHarness())
            return;

        var builds = new KnownBuilds(new ProcessRunner(), Paths());

        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            Newest,
            TestContext.Current.CancellationToken
        );
        var again = await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            Newest,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BuildAddOutcome.AlreadyKnown>(again);
        Assert.Single(builds.All(FfmpegFilter.Beatdetect));
    }

    [Fact]
    public async Task The_pin_moves_to_another_known_build_and_survives_a_restart()
    {
        if (!SkipUnlessHarness())
            return;

        var builds = new KnownBuilds(new ProcessRunner(), Paths());
        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            Newest,
            TestContext.Current.CancellationToken
        );
        var older = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                Older,
                TestContext.Current.CancellationToken
            )
        );

        Assert.True(builds.Pin(FfmpegFilter.Beatdetect, older.Known.Build.Sha256));

        var restarted = new KnownBuilds(new ProcessRunner(), Paths());

        Assert.Equal(2, restarted.All(FfmpegFilter.Beatdetect).Count);
        Assert.Equal(
            older.Known.Build.Label,
            restarted.Pinned(FfmpegFilter.Beatdetect)?.Build.Label
        );
    }

    [Fact]
    public async Task The_pinned_build_cannot_be_removed_and_an_unknown_one_cannot_be_pinned()
    {
        if (!SkipUnlessHarness())
            return;

        var builds = new KnownBuilds(new ProcessRunner(), Paths());
        var added = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                Newest,
                TestContext.Current.CancellationToken
            )
        );

        Assert.False(builds.Remove(FfmpegFilter.Beatdetect, added.Known.Build.Sha256));
        Assert.False(builds.Pin(FfmpegFilter.Beatdetect, "0000"));
        Assert.Single(builds.All(FfmpegFilter.Beatdetect));
    }

    [Fact]
    public async Task A_machine_that_knows_no_build_starts_on_the_seed_and_pins_it()
    {
        if (!SkipUnlessHarness())
            return;

        var builds = new KnownBuilds(new ProcessRunner(), Paths(Newest));

        await builds.SeedAsync(TestContext.Current.CancellationToken);
        await builds.SeedAsync(TestContext.Current.CancellationToken);

        Assert.Single(builds.All(FfmpegFilter.Beatdetect));
        Assert.Equal("1.3.0-1e57fec", builds.Pinned(FfmpegFilter.Beatdetect)?.Build.Label);
    }

    [Fact]
    public async Task A_reading_made_before_the_pin_moved_stays_visible_and_is_marked_older()
    {
        if (!SkipUnlessHarness())
            return;

        var audio = Path.Combine(_folder, "tone.wav");
        WriteSilence(audio);
        var builds = new KnownBuilds(new ProcessRunner(), Paths());
        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            Older,
            TestContext.Current.CancellationToken
        );
        var newer = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                Newest,
                TestContext.Current.CancellationToken
            )
        );
        var reader = new BeatReader(
            new ProcessRunner(),
            Path.Combine(_folder, "beats.json"),
            builds: builds
        );

        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);
        await reader.MeasureAsync(audio, TestContext.Current.CancellationToken);

        Assert.False(reader.IsFromOlderBuild(audio));

        builds.Pin(FfmpegFilter.Beatdetect, newer.Known.Build.Sha256);
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
        var builds = new KnownBuilds(new ProcessRunner(), Paths());
        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            Older,
            TestContext.Current.CancellationToken
        );
        var newer = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                Newest,
                TestContext.Current.CancellationToken
            )
        );
        var reader = new BeatReader(
            new ProcessRunner(),
            Path.Combine(_folder, "beats.json"),
            builds: builds
        );
        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);
        await reader.MeasureAsync(old, TestContext.Current.CancellationToken);

        builds.Pin(FfmpegFilter.Beatdetect, newer.Known.Build.Sha256);
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
        var builds = new KnownBuilds(new ProcessRunner(), Paths());
        var older = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                Older,
                TestContext.Current.CancellationToken
            )
        );
        var newer = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                Newest,
                TestContext.Current.CancellationToken
            )
        );
        builds.Pin(FfmpegFilter.Beatdetect, newer.Known.Build.Sha256);
        var reader = new BeatReader(
            new ProcessRunner(),
            Path.Combine(_folder, "beats.json"),
            builds: builds
        );
        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);
        await reader.MeasureAsync(audio, TestContext.Current.CancellationToken);

        builds.Pin(FfmpegFilter.Beatdetect, older.Known.Build.Sha256);
        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);

        Assert.Empty(reader.ToRead([audio]));
    }

    [Fact]
    public void A_file_from_before_keydetect_had_a_pin_keeps_beatdetects_builds_and_pin()
    {
        File.WriteAllText(
            StatePath,
            """
            {"builds":[
              {"path":"C:\\a\\ffmpeg.exe","build":{"version":"1.3.1","sha256":"aa","commit":null}},
              {"path":"C:\\b\\ffmpeg.exe","build":{"version":"1.3.0","sha256":"bb","commit":null}}
            ],"pinned":"aa"}
            """
        );

        var builds = new KnownBuilds(new ProcessRunner(), Paths());

        Assert.Equal("1.3.1", builds.Pinned(FfmpegFilter.Beatdetect)?.Build.Label);
        Assert.Empty(builds.All(FfmpegFilter.Keydetect));
        Assert.True(builds.Remove(FfmpegFilter.Beatdetect, "bb"));

        var restarted = new KnownBuilds(new ProcessRunner(), Paths());

        Assert.Equal("1.3.1", restarted.Pinned(FfmpegFilter.Beatdetect)?.Build.Label);
        Assert.Single(restarted.All(FfmpegFilter.Beatdetect));
        Assert.Contains("\"filters\"", File.ReadAllText(StatePath));
    }

    [Fact]
    public void A_filter_with_no_pin_runs_on_its_configured_executable()
    {
        var builds = new KnownBuilds(new ProcessRunner(), Paths(keydetectSeed: "ffmpeg"));

        Assert.Equal("ffmpeg", builds.FileNameFor(FfmpegFilter.Keydetect));
    }

    [Fact]
    public async Task Each_filter_keeps_its_own_pin_and_a_build_without_the_filter_is_refused()
    {
        if (!SkipUnlessHarness())
            return;

        if (!File.Exists(KeydetectBuild))
        {
            Assert.Skip("The keydetect 1.2.1 build is not on this machine");

            return;
        }

        var builds = new KnownBuilds(new ProcessRunner(), Paths(Newest, KeydetectBuild));

        await builds.SeedAsync(TestContext.Current.CancellationToken);

        Assert.IsType<BuildAddOutcome.NotFfmpeg>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                KeydetectBuild,
                TestContext.Current.CancellationToken
            )
        );

        var restarted = new KnownBuilds(new ProcessRunner(), Paths());

        Assert.Equal("1.3.0-1e57fec", restarted.Pinned(FfmpegFilter.Beatdetect)?.Build.Label);
        Assert.Equal("1.2.1", restarted.Pinned(FfmpegFilter.Keydetect)?.Build.Label);
        Assert.Equal(KeydetectBuild, restarted.FileNameFor(FfmpegFilter.Keydetect));
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
