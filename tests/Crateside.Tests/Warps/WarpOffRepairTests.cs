using AbleKit.Analysis;
using AbleKit.Sets;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Warps;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Warps;

public sealed class WarpOffRepairTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly WrittenWarps _written = new();
    private readonly LiveAnalyses _waiting = new();
    private readonly SetsUsing _sets = new();

    public WarpOffRepairTests()
    {
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
            WarpBackups = Path.Combine(_root, "backups"),
        };
        Directory.CreateDirectory(_paths.StagingFolder);
        Directory.CreateDirectory(Path.Combine(_paths.LibraryFolder, "Artist"));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Only_the_marker_list_changes_in_staging_and_the_library()
    {
        var staged = Sample(Path.Combine("Staging", "Song (Drums).flac"));
        var filed = Sample(Path.Combine("Library", "Artist", "Song (Vocal - Full).flac"));
        var before = AnalysisFile.Parse(CrashingSidecar.Bytes());

        var (repaired, deferred) = Repair().Run();

        Assert.Equal(new[] { staged, filed }.Order(), repaired.Order());
        Assert.Empty(deferred);

        foreach (var path in repaired)
        {
            Assert.Equal(Warp.Off(0).Markers, AnalysisFile.Read($"{path}.asd").Warp.Markers);

            // Every other byte is the original's: the same file with only the pair in its marker list.
            Assert.Equal(
                (
                    before with
                    {
                        Warp = before.Warp with { Markers = Warp.Off(0).Markers },
                    }
                ).ToBytes(),
                File.ReadAllBytes($"{path}.asd")
            );
        }
    }

    [Fact]
    public void A_sidecar_is_backed_up_untouched_before_it_is_rewritten()
    {
        var staged = Sample(Path.Combine("Staging", "Song (Drums).flac"));
        var original = File.ReadAllBytes($"{staged}.asd");

        Repair().Run();

        Assert.Equal(
            original,
            File.ReadAllBytes(Assert.Single(Directory.GetFiles(_paths.WarpBackups)))
        );
    }

    [Fact]
    public void Clips_that_are_unsaved_or_already_have_markers_are_not_touched()
    {
        var crashing = Sample(
            Path.Combine("Staging", "Song (Drums).flac"),
            CrashingSidecar.Bytes()
        );
        var unsaved = Sample(
            Path.Combine("Staging", "Song (Bass).flac"),
            File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "sidecar-clicks-unsaved.asd")
            )
        );
        var saved = Sample(
            Path.Combine("Staging", "Song (Other).flac"),
            File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "sidecar-clicks.asd")
            )
        );
        var untouched = new[] { unsaved, saved }.ToDictionary(
            path => path,
            path => File.ReadAllBytes($"{path}.asd")
        );

        var (repaired, _) = Repair().Run();

        Assert.Equal([crashing], repaired);

        foreach (var (path, bytes) in untouched)
            Assert.Equal(bytes, File.ReadAllBytes($"{path}.asd"));

        Assert.Empty(Repair().Run().Repaired);
    }

    [Fact]
    public void A_sample_an_open_set_uses_waits_and_is_repaired_once_the_set_is_closed()
    {
        var inUse = Sample(Path.Combine("Staging", "Song (Drums).flac"));
        var free = Sample(Path.Combine("Staging", "Song (Bass).flac"));
        var original = File.ReadAllBytes($"{inUse}.asd");
        _sets.Uses[inUse] = "Mashup";
        _sets.Open = new OpenSet(IsRunning: true, "Mashup");
        var repair = Repair();

        var (repaired, deferred) = repair.Run();

        Assert.Equal([free], repaired);
        Assert.Equal([inUse], deferred);
        Assert.Equal(original, File.ReadAllBytes($"{inUse}.asd"));
        Assert.False(repair.IsDone);

        _sets.Open = OpenSet.NotRunning;

        Assert.Equal([inUse], repair.Run().Repaired);
        Assert.Equal(Warp.Off(0).Markers, AnalysisFile.Read($"{inUse}.asd").Warp.Markers);
        Assert.True(repair.IsDone);
    }

    [Fact]
    public void A_sample_only_a_closed_set_uses_is_repaired_while_another_set_is_open()
    {
        var staged = Sample(Path.Combine("Staging", "Song (Drums).flac"));
        _sets.Uses[staged] = "Old set";
        _sets.Open = new OpenSet(IsRunning: true, "Other set");

        Assert.Equal([staged], Repair().Run().Repaired);
    }

    [Fact]
    public void A_sample_waiting_for_Live_to_analyze_it_is_left_for_later()
    {
        var staged = Sample(Path.Combine("Staging", "Song (Drums).flac"));
        _waiting.Await(staged, new AwaitedAnalysis("backup", null, false, DateTime.UtcNow));

        var (repaired, deferred) = Repair().Run();

        Assert.Empty(repaired);
        Assert.Equal([staged], deferred);
    }

    private WarpOffRepair Repair() =>
        new(
            _paths,
            new SidecarWriter(
                new NoDecoder(),
                new OnsetDetector(),
                _written,
                new TransientOrigins()
            ),
            new SidecarBackups(_paths.WarpBackups, new FakeTimeProvider()),
            _sets,
            new BeatReader(new ProcessRunner()),
            _waiting
        );

    private string Sample(string relative, byte[]? sidecar = null)
    {
        var path = Path.Combine(_root, relative);

        File.WriteAllBytes(path, [1, 2, 3]);
        File.WriteAllBytes($"{path}.asd", sidecar ?? CrashingSidecar.Bytes());

        return path;
    }

    private sealed class SetsUsing : ISetLinks
    {
        public Dictionary<string, string> Uses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public OpenSet Open { get; set; } = OpenSet.NotRunning;

        public IReadOnlyList<string> Using(IReadOnlyCollection<string> samples) =>
            [
                .. samples
                    .Select(sample => Uses.GetValueOrDefault(sample))
                    .OfType<string>()
                    .Distinct(),
            ];

        public OpenSet ReadOpenSet() => Open;

        public SetRelink Relink(IReadOnlyList<SampleMove> moves) => new([], []);
    }

    private sealed class NoDecoder : IAudioDecoder
    {
        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<DecodedAudio?>(null);
    }
}
