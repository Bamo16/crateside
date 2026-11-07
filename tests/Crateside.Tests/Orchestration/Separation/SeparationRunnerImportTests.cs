using Crateside.Core;
using Crateside.Core.Imports;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Separation;

public sealed class SeparationRunnerImportTests : IDisposable
{
    private const string TrackKey = "file~3fa9c0d12be84471";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FakeProcessProbe _probe = new();
    private readonly string _log;

    public SeparationRunnerImportTests()
    {
        Directory.CreateDirectory(_directory);
        _log = Path.Combine(_directory, "calls.txt");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private CratesidePaths Paths =>
        field ??= new CratesidePaths
        {
            ImportsFolder = Path.Combine(_directory, "imports"),
            WorkingFolder = Path.Combine(_directory, "working"),
            StemsKeyIdentified = Path.Combine(_directory, "Key Identified"),
            StemsUnresolvedKey = Path.Combine(_directory, "Unresolved Key"),
        };

    private string ImportAudio()
    {
        var source = Path.Combine(_directory, "1-01. Graduation.flac");

        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), source);

        return new ImportStore(Paths).Take(TrackKey, source, move: true);
    }

    private SeparationRunner Runner(bool knowsSourceUrl)
    {
        var help = knowsSourceUrl ? "  --source-url value" : "  --extract-drums";
        var script = FakeCli.Write(
            _directory,
            "fake-stemforge",
            windows: $$"""
            @echo off
            echo %* >> "{{_log}}"
            echo %* | findstr /c:"--help" > nul && (echo {{help}} & exit /b 0)
            echo [{"succeeded":true,"outputFiles":["one.flac","two.flac","three.flac"]}]
            """,
            unix: $$"""
            echo "$@" >> '{{_log}}'
            case "$*" in *--help*) echo '{{help}}'; exit 0;; esac
            echo '[{"succeeded":true,"outputFiles":["one.flac","two.flac","three.flac"]}]'
            """
        );
        var jobs = new JobStore(Path.Combine(_directory, "jobs.jsonl"));

        return new SeparationRunner(
            new SeparationInterlock(jobs, _probe),
            new WorkingSet(Paths),
            Paths,
            script
        );
    }

    private static WorkOrder Order() =>
        WorkOrder.Compose(
            new WorkItem { TrackKey = TrackKey, Title = "Be For U - Graduation" },
            TestLedger.From("does-not-exist")
        );

    private string[] Calls => File.Exists(_log) ? File.ReadAllLines(_log) : [];

    [Fact]
    public async Task An_import_is_cut_from_its_own_file_and_told_which_track_it_is()
    {
        var audio = ImportAudio();

        var result = await Runner(knowsSourceUrl: true)
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Equal(SeparationOutcome.Succeeded, result.Outcome);

        var separate = Assert.Single(Calls, line => !line.Contains("--help")).Trim();

        Assert.Contains(audio, separate);
        Assert.Contains($"--source-url track:{TrackKey}", separate);
    }

    [Fact]
    public async Task An_old_stemforge_fails_the_separation_rather_than_cutting_stems_that_join_to_nothing()
    {
        ImportAudio();

        var result = await Runner(knowsSourceUrl: false)
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Equal(SeparationOutcome.Failed, result.Outcome);
        Assert.Equal(SeparationRunner.StemForgeTooOld, result.Error);
        Assert.DoesNotContain(Calls, line => !line.Contains("--help"));
    }

    [Fact]
    public async Task An_import_whose_file_has_gone_fails_and_is_not_fetched()
    {
        var result = await Runner(knowsSourceUrl: true)
            .RunAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Equal(SeparationOutcome.Failed, result.Outcome);
        Assert.Contains("not on disk", result.Error);
        Assert.Empty(Calls);
    }

    [Fact]
    public async Task A_youtube_track_is_cut_as_before_without_asking_for_the_flag()
    {
        var order = WorkOrder.Compose(
            new WorkItem { TrackKey = "dQw4w9WgXcQ", Title = "Yes - Owner" },
            TestLedger.From("does-not-exist")
        );

        await Runner(knowsSourceUrl: false).RunAsync(order, TestContext.Current.CancellationToken);

        var line = Assert.Single(Calls);

        Assert.DoesNotContain("--source-url", line);
        Assert.Contains("watch?v=dQw4w9WgXcQ", line);
    }
}
