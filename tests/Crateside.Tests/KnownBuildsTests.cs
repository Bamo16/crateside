using Crateside.Core;
using Crateside.Core.Beats;

namespace Crateside.Tests;

public sealed class KnownBuildsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("crateside-builds").FullName;
    private readonly FakeFfmpegBuilds _ffmpeg;
    private readonly string _older;
    private readonly string _newest;
    private readonly string _keydetectBuild;

    private string StatePath => Path.Combine(_folder, "builds.json");

    public KnownBuildsTests()
    {
        _ffmpeg = new(_folder);
        _older = _ffmpeg.Add(FfmpegFilter.Beatdetect, "1.2.0");
        _newest = _ffmpeg.Add(FfmpegFilter.Beatdetect, "1.3.0");
        _keydetectBuild = _ffmpeg.Add(FfmpegFilter.Keydetect, "1.2.1");
    }

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
        var builds = new KnownBuilds(_ffmpeg, Paths());

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
        var builds = new KnownBuilds(_ffmpeg, Paths());

        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            _newest,
            TestContext.Current.CancellationToken
        );
        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            _older,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, builds.All(FfmpegFilter.Beatdetect).Count);
        Assert.Equal("1.3.0", builds.Pinned(FfmpegFilter.Beatdetect)?.Build.Label);
    }

    [Fact]
    public async Task Adding_the_same_executable_twice_keeps_one_entry()
    {
        var builds = new KnownBuilds(_ffmpeg, Paths());

        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            _newest,
            TestContext.Current.CancellationToken
        );
        var again = await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            _newest,
            TestContext.Current.CancellationToken
        );

        Assert.IsType<BuildAddOutcome.AlreadyKnown>(again);
        Assert.Single(builds.All(FfmpegFilter.Beatdetect));
    }

    [Fact]
    public async Task The_pin_moves_to_another_known_build_and_survives_a_restart()
    {
        var builds = new KnownBuilds(_ffmpeg, Paths());
        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            _newest,
            TestContext.Current.CancellationToken
        );
        var older = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                _older,
                TestContext.Current.CancellationToken
            )
        );

        Assert.True(builds.Pin(FfmpegFilter.Beatdetect, older.Known.Build.Sha256));

        var restarted = new KnownBuilds(_ffmpeg, Paths());

        Assert.Equal(2, restarted.All(FfmpegFilter.Beatdetect).Count);
        Assert.Equal(
            older.Known.Build.Label,
            restarted.Pinned(FfmpegFilter.Beatdetect)?.Build.Label
        );
    }

    [Fact]
    public async Task The_pinned_build_cannot_be_removed_and_an_unknown_one_cannot_be_pinned()
    {
        var builds = new KnownBuilds(_ffmpeg, Paths());
        var added = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                _newest,
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
        var builds = new KnownBuilds(_ffmpeg, Paths(_newest));

        await builds.SeedAsync(TestContext.Current.CancellationToken);
        await builds.SeedAsync(TestContext.Current.CancellationToken);

        Assert.Single(builds.All(FfmpegFilter.Beatdetect));
        Assert.Equal("1.3.0", builds.Pinned(FfmpegFilter.Beatdetect)?.Build.Label);
    }

    [Fact]
    public async Task A_reading_made_before_the_pin_moved_stays_visible_and_is_marked_older()
    {
        var audio = Path.Combine(_folder, "tone.wav");
        WriteSilence(audio);
        var builds = new KnownBuilds(_ffmpeg, Paths());
        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            _older,
            TestContext.Current.CancellationToken
        );
        var newer = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                _newest,
                TestContext.Current.CancellationToken
            )
        );
        var reader = new BeatReader(_ffmpeg, Path.Combine(_folder, "beats.json"), builds: builds);

        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);
        await reader.MeasureAsync(audio, TestContext.Current.CancellationToken);

        Assert.False(reader.IsFromOlderBuild(audio));

        builds.Pin(FfmpegFilter.Beatdetect, newer.Known.Build.Sha256);
        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(reader.Read(audio));
        Assert.True(reader.IsFromOlderBuild(audio));
        Assert.Equal("1.2.0", reader.BuildOf(audio)?.Label);
    }

    [Fact]
    public async Task A_reading_from_an_older_build_is_read_again_after_those_with_none()
    {
        var (old, fresh) = (Path.Combine(_folder, "old.wav"), Path.Combine(_folder, "fresh.wav"));
        WriteSilence(old);
        WriteSilence(fresh);
        var builds = new KnownBuilds(_ffmpeg, Paths());
        await builds.AddAsync(
            FfmpegFilter.Beatdetect,
            _older,
            TestContext.Current.CancellationToken
        );
        var newer = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                _newest,
                TestContext.Current.CancellationToken
            )
        );
        var reader = new BeatReader(_ffmpeg, Path.Combine(_folder, "beats.json"), builds: builds);
        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);
        await reader.MeasureAsync(old, TestContext.Current.CancellationToken);

        builds.Pin(FfmpegFilter.Beatdetect, newer.Known.Build.Sha256);
        await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal([fresh, old], reader.ToRead([old, fresh]));

        await reader.MeasureAsync(old, TestContext.Current.CancellationToken);

        Assert.Equal([fresh], reader.ToRead([old, fresh]));
        Assert.Equal("1.3.0", reader.BuildOf(old)?.Label);
    }

    [Fact]
    public async Task A_reading_from_a_newer_build_than_the_pin_is_kept()
    {
        var audio = Path.Combine(_folder, "tone.wav");
        WriteSilence(audio);
        var builds = new KnownBuilds(_ffmpeg, Paths());
        var older = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                _older,
                TestContext.Current.CancellationToken
            )
        );
        var newer = Assert.IsType<BuildAddOutcome.Added>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                _newest,
                TestContext.Current.CancellationToken
            )
        );
        builds.Pin(FfmpegFilter.Beatdetect, newer.Known.Build.Sha256);
        var reader = new BeatReader(_ffmpeg, Path.Combine(_folder, "beats.json"), builds: builds);
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

        var builds = new KnownBuilds(_ffmpeg, Paths());

        Assert.Equal("1.3.1", builds.Pinned(FfmpegFilter.Beatdetect)?.Build.Label);
        Assert.Empty(builds.All(FfmpegFilter.Keydetect));
        Assert.True(builds.Remove(FfmpegFilter.Beatdetect, "bb"));

        var restarted = new KnownBuilds(_ffmpeg, Paths());

        Assert.Equal("1.3.1", restarted.Pinned(FfmpegFilter.Beatdetect)?.Build.Label);
        Assert.Single(restarted.All(FfmpegFilter.Beatdetect));
        Assert.Contains("\"filters\"", File.ReadAllText(StatePath));
    }

    [Fact]
    public void A_filter_with_no_pin_runs_on_its_configured_executable()
    {
        var builds = new KnownBuilds(_ffmpeg, Paths(keydetectSeed: "ffmpeg"));

        Assert.Equal("ffmpeg", builds.FileNameFor(FfmpegFilter.Keydetect));
    }

    [Fact]
    public async Task Each_filter_keeps_its_own_pin_and_a_build_without_the_filter_is_refused()
    {
        var builds = new KnownBuilds(_ffmpeg, Paths(_newest, _keydetectBuild));

        await builds.SeedAsync(TestContext.Current.CancellationToken);

        Assert.IsType<BuildAddOutcome.NotFfmpeg>(
            await builds.AddAsync(
                FfmpegFilter.Beatdetect,
                _keydetectBuild,
                TestContext.Current.CancellationToken
            )
        );

        var restarted = new KnownBuilds(_ffmpeg, Paths());

        Assert.Equal("1.3.0", restarted.Pinned(FfmpegFilter.Beatdetect)?.Build.Label);
        Assert.Equal("1.2.1", restarted.Pinned(FfmpegFilter.Keydetect)?.Build.Label);
        Assert.Equal(_keydetectBuild, restarted.FileNameFor(FfmpegFilter.Keydetect));
    }

    private static void WriteSilence(string path) => TestWav.Write(path, 1, _ => 0);
}
